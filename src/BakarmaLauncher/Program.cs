using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BakarmaCoach;
using BakarmaLauncher.Core;

namespace BakarmaLauncher;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try { new Application().Run(new LauncherWindow(args)); }
        catch (Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "launcher-error.txt"), ex.ToString()); MessageBox.Show(ex.Message, "Daeva Sentinel Launcher"); }
    }
}

public sealed class LauncherWindow : Window
{
    private readonly Grid _root;
    private readonly UpdateClient _updates = new(AppContext.BaseDirectory);
    private readonly CancellationTokenSource _cancel = new();
    private bool _busy;
    private ReleaseManifest? _latest;
    private readonly string _config = Path.Combine(AppContext.BaseDirectory, "launcher-language.txt");
    private T C<T>(string name) where T : FrameworkElement => (T)_root.FindName(name);
    public LauncherWindow(string[] args)
    {
        Title = "Daeva Sentinel Launcher"; Width = 850; Height = 620; ResizeMode = ResizeMode.CanMinimize;
        Icon = UiLayout.Icon();
        WindowStartupLocation = WindowStartupLocation.CenterScreen; FontFamily = new FontFamily("Segoe UI");
        _root = UiLayout.Load("LauncherShell.xaml"); Content = _root;
        SourceInitialized += (_, _) => UiLayout.DarkCaption(this);
        try { UiLanguage.Current = File.ReadAllText(_config).Trim() == "pl" ? "pl" : "en"; } catch { UiLanguage.Current = "en"; }
        C<ComboBox>("Language").ItemsSource = new[] { "English", "Polski" }; C<ComboBox>("Language").SelectedIndex = UiLanguage.Current == "pl" ? 1 : 0;
        C<ComboBox>("Language").SelectionChanged += (_, _) => { UiLanguage.Current = C<ComboBox>("Language").SelectedIndex == 1 ? "pl" : "en"; File.WriteAllText(_config, UiLanguage.Current); UiLanguage.Apply(_root); };
        C<TextBlock>("Version").Text = _updates.InstalledVersion;
        C<Button>("Check").Click += async (_, _) => await CheckOnly();
        C<Button>("Play").Click += async (_, _) => await Launch();
        C<Button>("Rollback").Click += (_, _) =>
        {
            try { _updates.Rollback(); C<TextBlock>("Version").Text = _updates.InstalledVersion; Say("Ready to play"); }
            catch (Exception ex) { Say(ex.Message); }
        };
        C<Button>("Github").Click += (_, _) => Process.Start(new ProcessStartInfo(UpdateClient.Repository) { UseShellExecute = true });
        Closing += (_, _) => { _cancel.Cancel(); _updates.Dispose(); };
        Loaded += async (_, _) =>
        {
            UiLanguage.Apply(_root);
            if (args.Contains("--smoke"))
            {
                int i = Array.IndexOf(args, "--screenshot");
                if (i >= 0 && i + 1 < args.Length)
                {
                    UpdateLayout(); var picture = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    picture.Render(this); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(picture)); using var output = File.Create(args[i + 1]); png.Save(output);
                }
                Close();
            }
            else if (args.Contains("--verify-feed"))
            {
                try { var release = await _updates.Check(_cancel.Token); File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "feed-verification.json"), JsonSerializer.Serialize(new { ok = true, release.Version, release.Sha256 })); }
                catch (Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "feed-verification.json"), JsonSerializer.Serialize(new { ok = false, error = ex.Message })); }
                Close();
            }
        };
    }
    private void Say(string text) { C<TextBlock>("State").Text = UiLanguage.T(text); }
    private void Busy(bool value)
    {
        _busy = value;
        foreach (string name in new[] { "Play", "Check", "Rollback" }) C<Button>(name).IsEnabled = !value;
    }
    private async Task CheckOnly()
    {
        if (_busy) return; Busy(true); Say("Checking for updates…");
        try
        {
            _latest = await _updates.Check(_cancel.Token);
            C<TextBlock>("Latest").Text = UiLanguage.T("Latest version") + ": " + _latest.Version;
            bool newer = Version.Parse(_latest.Version) > Version.Parse(_updates.InstalledVersion);
            C<Button>("Play").Content = UiLanguage.T(newer ? "Update & launch" : "Launch companion"); Say(newer ? "Update & launch" : "Up to date");
        }
        catch (Exception ex) { Say(UiLanguage.Current == "pl" ? "Nie można sprawdzić aktualizacji. Zainstalowana wersja jest dostępna." : "Could not check updates. The installed version is available."); C<TextBlock>("Details").Text = ex.Message; }
        finally { Busy(false); }
    }
    private async Task Launch()
    {
        if (_busy) return; Busy(true);
        try
        {
            try
            {
                Say("Checking for updates…"); _latest = await _updates.Check(_cancel.Token);
                if (Version.Parse(_latest.Version) > Version.Parse(_updates.InstalledVersion))
                    await _updates.Install(_latest, new Progress<double>(x => C<ProgressBar>("Progress").Value = x), Say, _cancel.Token);
            }
            catch (Exception ex)
            {
                C<TextBlock>("Details").Text = (UiLanguage.Current == "pl" ? "Aktualizacja nie powiodła się. Uruchamianie poprzedniej wersji. " : "Update unavailable. Launching the installed version. ") + ex.Message;
            }
            string root = Path.GetFullPath(AppContext.BaseDirectory);
            foreach (var process in Process.GetProcessesByName("Daeva Sentinel"))
            {
                using (process)
                {
                    if (process.MainModule?.FileName is string path && path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    {
                        process.CloseMainWindow(); if (!await Task.Run(() => process.WaitForExit(5000))) throw new InvalidOperationException("Close Daeva Sentinel before launching a new version.");
                    }
                }
            }
            Directory.CreateDirectory(_updates.SharedData);
            var start = new ProcessStartInfo(_updates.AppPath) { UseShellExecute = false };
            start.ArgumentList.Add("--data-dir"); start.ArgumentList.Add(_updates.SharedData); start.ArgumentList.Add("--language"); start.ArgumentList.Add(UiLanguage.Current);
            Process.Start(start); C<TextBlock>("Version").Text = _updates.InstalledVersion; Say("Ready to play"); WindowState = WindowState.Minimized;
        }
        catch (Exception ex) { Say(ex.Message); }
        finally { Busy(false); }
    }
}
