using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BakarmaCoach.Core;
using Tesseract;
using D = System.Drawing;

namespace BakarmaCoach;

public sealed record ScreenRegion(double X, double Y, double Width, double Height)
{
    public bool Valid => double.IsFinite(X + Y + Width + Height) && X >= 0 && Y >= 0 && Width > 0 && Height > 0 && X + Width <= 1.001 && Y + Height <= 1.001;
    public D.Rectangle Pixels(D.Rectangle client) => new(client.X + (int)Math.Round(X * client.Width), client.Y + (int)Math.Round(Y * client.Height),
        Math.Max(1, (int)Math.Round(Width * client.Width)), Math.Max(1, (int)Math.Round(Height * client.Height)));
}
public sealed record ScreenCalibration(ScreenRegion Boss, ScreenRegion Ability, double Aspect)
{
    public bool Valid => Boss.Valid && Ability.Valid && Aspect > 0;
}
public sealed record GameTarget(IntPtr Handle, D.Rectangle Client);

public static class GameCapture
{
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr h, out Rect r);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr h, ref Point p);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr h, uint affinity);
    public static bool GameName(string name) => name.Equals("AION2", StringComparison.OrdinalIgnoreCase) || name.Equals("AION2-Win64-Shipping", StringComparison.OrdinalIgnoreCase);
    public static GameTarget? Foreground()
    {
        IntPtr h = GetForegroundWindow();
        try
        {
            GetWindowThreadProcessId(h, out uint pid);
            using var process = Process.GetProcessById((int)pid);
            return GameName(process.ProcessName) ? Describe(h) : null;
        }
        catch { return null; }
    }
    public static GameTarget? Find()
    {
        if (Foreground() is { } front) return front;
        foreach (string name in new[] { "AION2", "AION2-Win64-Shipping" })
            foreach (var p in Process.GetProcessesByName(name))
                using (p) { if (Describe(p.MainWindowHandle) is { } found) return found; }
        return null;
    }
    private static GameTarget? Describe(IntPtr h)
    {
        var point = new Point();
        if (h == IntPtr.Zero || IsIconic(h) || !GetClientRect(h, out var rect) || !ClientToScreen(h, ref point) || rect.Right < 320 || rect.Bottom < 200) return null;
        return new(h, new D.Rectangle(point.X, point.Y, rect.Right, rect.Bottom));
    }
    public static void Focus(GameTarget target) => SetForegroundWindow(target.Handle);
    public static void Exclude(Window window) => SetWindowDisplayAffinity(new WindowInteropHelper(window).EnsureHandle(), 0x11);
    public static D.Bitmap Capture(D.Rectangle region)
    {
        if (region.Width < 8 || region.Height < 8 || region.Width > 10000 || region.Height > 10000) throw new InvalidOperationException("Invalid capture area.");
        var bitmap = new D.Bitmap(region.Width, region.Height, D.Imaging.PixelFormat.Format24bppRgb);
        try { using var graphics = D.Graphics.FromImage(bitmap); graphics.CopyFromScreen(region.Location, D.Point.Empty, region.Size, D.CopyPixelOperation.SourceCopy); return bitmap; }
        catch { bitmap.Dispose(); throw; }
    }
    public static byte[] Png(D.Bitmap bitmap) { using var stream = new MemoryStream(); bitmap.Save(stream, D.Imaging.ImageFormat.Png); return stream.ToArray(); }
    public static BitmapSource Image(byte[] png)
    {
        using var memory = new MemoryStream(png); var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = memory; image.EndInit(); image.Freeze(); return image;
    }
}

public sealed class LocalOcr : IDisposable
{
    private readonly TesseractEngine _engine = new(Path.Combine(AppContext.BaseDirectory, "tessdata"), "eng", EngineMode.LstmOnly);
    public (string Text, float Confidence) Read(D.Bitmap crop)
    {
        // Enlarge small UI text and convert light-on-dark lettering to dark-on-white.
        int scale = crop.Height < 40 ? 3 : 2;
        using var prepared = new D.Bitmap(crop.Width * scale + 24, crop.Height * scale + 24);
        using (var g = D.Graphics.FromImage(prepared))
        {
            g.Clear(D.Color.White); g.InterpolationMode = D.Drawing2D.InterpolationMode.HighQualityBicubic;
            using var attributes = new D.Imaging.ImageAttributes();
            attributes.SetColorMatrix(new D.Imaging.ColorMatrix(new[]
            {
                new float[] { -.299f, -.299f, -.299f, 0, 0 }, new float[] { -.587f, -.587f, -.587f, 0, 0 },
                new float[] { -.114f, -.114f, -.114f, 0, 0 }, new float[] { 0, 0, 0, 1, 0 }, new float[] { 1, 1, 1, 0, 1 }
            }));
            g.DrawImage(crop, new D.Rectangle(12, 12, crop.Width * scale, crop.Height * scale), 0, 0, crop.Width, crop.Height, D.GraphicsUnit.Pixel, attributes);
        }
        using var pix = Pix.LoadFromMemory(GameCapture.Png(prepared));
        using var page = _engine.Process(pix, PageSegMode.SingleLine);
        return (page.GetText().Trim(), page.GetMeanConfidence());
    }
    public void Dispose() => _engine.Dispose();
}

public sealed record ScreenFrame(DateTime Captured, string Boss, float BossConfidence, string Ability, float Confidence, byte[] BossPreview, byte[] AbilityPreview);

public sealed class ScreenInput : IDisposable
{
    private readonly ScreenCalibration _calibration;
    private readonly Encounter _encounter;
    private readonly ScreenDetector _detector = new();
    private readonly Action<object> _log;
    private readonly ConcurrentQueue<(int Generation, ScreenFrame? Frame, string? Error)> _results = new();
    private LocalOcr? _ocr;
    private DateTime _lastCapture;
    private int _busy, _generation;
    private volatile bool _disposed;
    private bool _paused;
    public ScreenFrame? Preview { get; private set; }
    public ScreenDecision? Decision { get; private set; }
    public string Status { get; private set; } = "Waiting for Aion 2 in the foreground";
    public int Frames { get; private set; }
    public ScreenInput(ScreenCalibration calibration, Encounter encounter, Action<object> log) { _calibration = calibration; _encounter = encounter; _log = log; }
    public void ResetRecognition() { Interlocked.Increment(ref _generation); _detector.Reset(); Decision = null; }
    public void Tick(DateTime now)
    {
        if (_disposed) return;
        var game = GameCapture.Foreground();
        if (game is null)
        {
            if (!_paused) { ResetRecognition(); _paused = true; }
            Status = "Screen paused · Aion 2 must be in the foreground";
            while (_results.TryDequeue(out _)) { }
            return;
        }
        _paused = false;
        if (Math.Abs(game.Client.Width / (double)game.Client.Height / _calibration.Aspect - 1) > .025)
        {
            ResetRecognition(); Status = "Game aspect ratio changed · calibrate screen again"; return;
        }
        while (_results.TryDequeue(out var result))
        {
            if (result.Generation != Volatile.Read(ref _generation)) continue;
            if (result.Error is not null) { Status = "Screen OCR unavailable: " + result.Error; continue; }
            var frame = result.Frame!; Preview = frame; Frames++;
            Decision = _detector.Observe(frame.Boss, frame.BossConfidence, frame.Ability, frame.Confidence, frame.Captured, now);
            Status = Decision.Reason + (Decision.Skill is int skill ? " · " + Encounter.Rules[skill].Label : "");
            if (Decision.Trigger && Decision.Skill is int recognized)
            {
                _log(new { kind = "screen-cast", at = frame.Captured, skill = recognized, confidence = frame.Confidence });
                _encounter.OnScreenCast(recognized, Decision.Episode, frame.Captured, now);
            }
        }
        if (now - _lastCapture < TimeSpan.FromMilliseconds(300) || Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
        _lastCapture = now; int generation = Volatile.Read(ref _generation);
        _ = Task.Run(() =>
        {
            try
            {
                // Recheck focus before and after recognition. Never process a queued background image.
                var current = GameCapture.Foreground();
                if (current is null || current.Handle != game.Handle) return;
                var bossRect = _calibration.Boss.Pixels(current.Client); var abilityRect = _calibration.Ability.Pixels(current.Client);
                if (bossRect.Width > 1400 || abilityRect.Width > 1400 || bossRect.Height > 250 || abilityRect.Height > 250) throw new InvalidOperationException("Selected regions are too large. Select only the name text.");
                DateTime captured = DateTime.Now;
                using var bossImage = GameCapture.Capture(bossRect); using var castImage = GameCapture.Capture(abilityRect);
                _ocr ??= new LocalOcr(); var bossText = _ocr.Read(bossImage); var castText = _ocr.Read(castImage);
                if (_disposed || generation != Volatile.Read(ref _generation) || GameCapture.Foreground()?.Handle != current.Handle) return;
                _results.Enqueue((generation, new ScreenFrame(captured, bossText.Text, bossText.Confidence, castText.Text, castText.Confidence,
                    GameCapture.Png(bossImage), GameCapture.Png(castImage)), null));
            }
            catch (Exception ex) { if (!_disposed) _results.Enqueue((generation, null, ex.GetBaseException().Message)); }
            finally
            {
                if (_disposed) Interlocked.Exchange(ref _ocr, null)?.Dispose();
                Interlocked.Exchange(ref _busy, 0);
            }
        });
    }
    public void Dispose()
    {
        _disposed = true; ResetRecognition();
        if (Volatile.Read(ref _busy) == 0) Interlocked.Exchange(ref _ocr, null)?.Dispose();
    }
}
