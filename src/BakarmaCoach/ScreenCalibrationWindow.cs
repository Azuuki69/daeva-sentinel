using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Runtime.InteropServices;
using D = System.Drawing;
using BakarmaCoach.Core;

namespace BakarmaCoach;

/// <summary>A frozen view of the game only; two user-drawn rectangles, normalized to client size.</summary>
public sealed class ScreenCalibrationWindow : Window
{
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int width, int height, uint flags);
    private readonly Canvas _canvas = new();
    private readonly Rectangle _selection = new() { Stroke = new SolidColorBrush(Color.FromRgb(131, 229, 213)), StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(35, 131, 229, 213)), Visibility = Visibility.Collapsed };
    private readonly TextBlock _instruction = new() { Foreground = Brushes.White, FontSize = 18, TextWrapping = TextWrapping.Wrap };
    private readonly Border _help;
    private System.Windows.Point? _start;
    private ScreenRegion? _boss;
    private readonly GameTarget _target;
    public ScreenCalibration? Calibration { get; private set; }
    public ScreenCalibrationWindow(GameTarget target, byte[] screenshot)
    {
        _target = target; Title = "Select boss and cast-name areas";
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; Topmost = true; Background = Brushes.Black; ShowInTaskbar = false;
        var root = new Grid(); root.Children.Add(new Image { Source = GameCapture.Image(screenshot), Stretch = Stretch.Fill, Opacity = .85 });
        root.Children.Add(_canvas); _canvas.Children.Add(_selection);
        _instruction.Text = "1 / 2   Drag tightly around the boss NAME: Transcendent Bakarma\nThen select the ability name. Escape cancels. Keep the name text on one line.";
        _help = new Border { Background = new SolidColorBrush(Color.FromArgb(240, 16, 23, 36)), CornerRadius = new CornerRadius(8), Padding = new Thickness(18),
            Margin = new Thickness(24), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, MaxWidth = 820, Child = _instruction, IsHitTestVisible = false };
        root.Children.Add(_help); Content = root;
        SourceInitialized += (_, _) => SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), target.Client.X, target.Client.Y, target.Client.Width, target.Client.Height, 0x0040);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { DialogResult = false; Close(); } };
        _canvas.Background = Brushes.Transparent; _canvas.Cursor = Cursors.Cross;
        _canvas.MouseLeftButtonDown += (_, e) =>
        {
            _start = e.GetPosition(_canvas); _canvas.CaptureMouse(); _selection.Visibility = Visibility.Visible; _help.Visibility = Visibility.Collapsed;
            Place(_start.Value, _start.Value);
        };
        _canvas.MouseMove += (_, e) => { if (_start is { } start) Place(start, e.GetPosition(_canvas)); };
        _canvas.MouseLeftButtonUp += (_, e) =>
        {
            if (_start is not { } start) return;
            var end = e.GetPosition(_canvas); _start = null; _canvas.ReleaseMouseCapture(); _help.Visibility = Visibility.Visible;
            double x = Math.Clamp(Math.Min(start.X, end.X) / _canvas.ActualWidth, 0, 1), y = Math.Clamp(Math.Min(start.Y, end.Y) / _canvas.ActualHeight, 0, 1);
            double w = Math.Min(1 - x, Math.Abs(start.X - end.X) / _canvas.ActualWidth), h = Math.Min(1 - y, Math.Abs(start.Y - end.Y) / _canvas.ActualHeight);
            var region = new ScreenRegion(x, y, w, h); var pixels = region.Pixels(_target.Client);
            if (pixels.Width < 40 || pixels.Height < 12 || pixels.Width > 1400 || pixels.Height > 250)
            { _instruction.Text = "Select a tighter rectangle around one line of text (40–1400 pixels wide, 12–250 tall). Escape cancels."; return; }
            if (_boss is null)
            {
                _boss = region; _selection.Visibility = Visibility.Collapsed;
                _instruction.Text = "2 / 2   Drag tightly around the boss CAST / ABILITY NAME\nChoose where the spell name appears, excluding the boss name and other labels. Escape cancels.";
            }
            else
            {
                Calibration = new(_boss, region, _target.Client.Width / (double)_target.Client.Height);
                DialogResult = true; Close();
            }
        };
    }
    private void Place(System.Windows.Point start, System.Windows.Point end)
    {
        Canvas.SetLeft(_selection, Math.Min(start.X, end.X)); Canvas.SetTop(_selection, Math.Min(start.Y, end.Y));
        _selection.Width = Math.Abs(start.X - end.X); _selection.Height = Math.Abs(start.Y - end.Y);
    }
}

public sealed class ScreenPreviewWindow : Window
{
    public ScreenPreviewWindow(ScreenFrame frame, ScreenDecision? decision)
    {
        Title = "Screen reading · captured game regions"; Width = 680; SizeToContent = SizeToContent.Height; MaxHeight = 700;
        Background = new SolidColorBrush(Color.FromRgb(16, 23, 36)); WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(20) };
        void Text(string text, int size = 14) => panel.Children.Add(new TextBlock { Text = text, Foreground = Brushes.White, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) });
        Text("Most recent game capture", 21);
        Text($"Boss: {frame.Boss} · OCR confidence {frame.BossConfidence:P0}");
        panel.Children.Add(new Image { Source = GameCapture.Image(frame.BossPreview), Stretch = Stretch.Uniform, MaxHeight = 120 });
        Text($"Ability: {frame.Ability} · OCR confidence {frame.Confidence:P0}");
        panel.Children.Add(new Image { Source = GameCapture.Image(frame.AbilityPreview), Stretch = Stretch.Uniform, MaxHeight = 120 });
        Text(decision?.Reason ?? "Waiting for a second frame");
        Text("Only the two selected game regions are read. Recognition is local; screenshots are kept in memory only. Recalibrate if the crop misses the names.");
        Content = panel;
        SourceInitialized += (_, _) => GameCapture.Exclude(this);
    }
}
