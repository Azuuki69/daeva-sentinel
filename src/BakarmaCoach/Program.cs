using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BakarmaCoach.Core;

namespace BakarmaCoach;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            var app = new Application();
            var window = new CoachWindow(args);
            app.Run(window);
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "startup-error.txt"), ex.ToString());
            MessageBox.Show(ex.Message, "Daeva Sentinel could not start");
        }
    }
}

public sealed class Preferences
{
    public Difficulty Mode { get; set; } = Difficulty.Conquest;
    public string VoiceId { get; set; } = "";
    public double Volume { get; set; } = 80;
    public bool Voice { get; set; } = true;
    public bool Countdown { get; set; }
    public bool NetworkInput { get; set; } = true;
    public bool ScreenInput { get; set; } = true;
    public ScreenCalibration? Calibration { get; set; }
    public int OverlayTab { get; set; }
    public string UiLocale { get; set; } = "en";
    public Dictionary<Difficulty, string> HpThresholds { get; set; } = new()
        { [Difficulty.Exploration] = "", [Difficulty.Conquest] = "75,25", [Difficulty.ConquestHard] = "" };
    public Dictionary<Difficulty, bool> HpEnabled { get; set; } = new();
    public double OverlayLeft { get; set; } = 80;
    public double OverlayTop { get; set; } = 90;
}

public sealed class CoachWindow : Window
{
    private readonly Grid _root;
    private readonly Encounter _engine = new();
    private readonly Speech _speech;
    private readonly EventLog _log;
    private readonly string _data;
    private readonly Preferences _prefs;
    private readonly DispatcherTimer _timer;
    private readonly AlertOverlay _overlay;
    private readonly bool _mute;
    private LiveInput? _live;
    private ScreenInput? _screen;
    private ScreenFrame? _lastScreenFrame;
    private ScreenDecision? _lastScreenDecision;
    private string _networkStatus = "Network input stopped";
    private bool _session;
    private DateTime? _demoStart;
    private readonly HashSet<int> _demoSteps = [];
    private Difficulty _previousMode;
    private Warning? _warning;
    private readonly string[] _args;
    private readonly NativeControls _native;
    private bool _closed;
    private bool _voiceReady;
    private bool _changingLanguage;
    private T C<T>(string name) where T : FrameworkElement => (T)_root.FindName(name);

    public CoachWindow(string[] args)
    {
        _args = args; _mute = args.Contains("--mute");
        int dataArgument = Array.IndexOf(args, "--data-dir");
        _data = dataArgument >= 0 && dataArgument + 1 < args.Length ? Path.GetFullPath(args[dataArgument + 1]) : Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(_data);
        try { _prefs = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(Path.Combine(_data, "settings.json"))) ?? new(); }
        catch { _prefs = new(); }
        int languageArgument = Array.IndexOf(args, "--language");
        if (languageArgument >= 0 && languageArgument + 1 < args.Length) _prefs.UiLocale = args[languageArgument + 1] == "pl" ? "pl" : "en";
        UiLanguage.Current = _prefs.UiLocale;
        _root = UiLayout.Load("AppShell.xaml"); Content = _root;
        C<TextBlock>("VersionBadge").Text = "EU / GLOBAL · " + typeof(CoachWindow).Assembly.GetName().Version?.ToString(3);
        Title = "Daeva Sentinel · Aion 2"; Width = 1160; Height = 900; MinWidth = 980; MinHeight = 760;
        Icon = UiLayout.Icon();
        Background = Brushes.Black; FontFamily = new FontFamily("Segoe UI"); WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _speech = new Speech(_data); _log = new EventLog(_data);
        if (Speech.Voices.Any(v => v.Id == _prefs.VoiceId)) _speech.VoiceId = _prefs.VoiceId;
        _speech.Error += text => Dispatcher.BeginInvoke(() => { if (!_closed) C<TextBlock>("VoiceStatus").Text = text; });
        _overlay = new AlertOverlay();
        _overlay.Left = Math.Clamp(_prefs.OverlayLeft, SystemParameters.VirtualScreenLeft, Math.Max(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 340));
        _overlay.Top = Math.Clamp(_prefs.OverlayTop, SystemParameters.VirtualScreenTop, Math.Max(SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 120));
        _overlay.HideRequested += () => { _overlay.Hide(); UpdateOverlayButton(); };
        C<ComboBox>("Mode").ItemsSource = new[] { "Exploration", "Conquest", "Conquest Hard" };
        C<ComboBox>("Mode").SelectedIndex = (int)_prefs.Mode;
        _previousMode = _prefs.Mode; LoadProfile();
        C<ComboBox>("Voices").ItemsSource = Speech.Voices.Select(v => v.DisplayName + " · " + v.Language).ToArray();
        C<ComboBox>("Voices").SelectedIndex = Speech.Voices.ToList().FindIndex(v => v.Id == _speech.VoiceId);
        C<CheckBox>("VoiceOn").IsChecked = _prefs.Voice; C<CheckBox>("Countdown").IsChecked = _prefs.Countdown;
        C<Slider>("Volume").Value = _prefs.Volume;
        C<CheckBox>("NetworkOn").IsChecked = _prefs.NetworkInput; C<CheckBox>("ScreenOn").IsChecked = _prefs.ScreenInput;
        C<ComboBox>("OverlayTab").ItemsSource = new[] { "Boss alerts", "DPS meter", "Threat / boss target" };
        C<ComboBox>("OverlayTab").SelectedIndex = Math.Clamp(_prefs.OverlayTab, 0, 2);
        _overlay.SelectTab(C<ComboBox>("OverlayTab").SelectedIndex);
        _overlay.TabChanged += tab => C<ComboBox>("OverlayTab").SelectedIndex = tab;
        C<ComboBox>("OverlayTab").SelectionChanged += (_, _) => { _overlay.SelectTab(Math.Max(0, C<ComboBox>("OverlayTab").SelectedIndex)); HighlightNavigation(); };
        C<ComboBox>("UiLocale").ItemsSource = new[] { "English", "Polski" }; C<ComboBox>("UiLocale").SelectedIndex = _prefs.UiLocale == "pl" ? 1 : 0;
        C<ComboBox>("UiLocale").SelectionChanged += (_, _) => SetLanguage(C<ComboBox>("UiLocale").SelectedIndex == 1 ? "pl" : "en");
        foreach (var pair in new[] { ("BossNav", 0), ("DpsNav", 1), ("ThreatNav", 2) })
            C<Button>(pair.Item1).Click += (_, _) => { C<ComboBox>("OverlayTab").SelectedIndex = pair.Item2; if (!_overlay.IsVisible) ToggleOverlay(); };
        C<Button>("ScreenNav").Click += (_, _) => C<TabControl>("SettingsTabs").SelectedIndex = 1;
        C<Button>("ResetMeters").Click += (_, _) => _live?.ResetMeters();
        C<Button>("Calibrate").Click += async (_, _) => await CalibrateScreen();
        C<Button>("Preview").Click += (_, _) =>
        {
            if (_lastScreenFrame is { } frame) new ScreenPreviewWindow(frame, _lastScreenDecision) { Owner = this }.Show();
            else MessageBox.Show(this, "Start live alerts and bring Aion 2 to the foreground. The most recent captured text regions will be available here.", "Screen preview");
        };
        if (_prefs.Calibration?.Valid == true) C<TextBlock>("ScreenStatus").Text = "Screen calibrated · ready to read boss and ability names";
        foreach (string name in new[] { "NetworkOn", "ScreenOn" }) C<CheckBox>(name).Click += (_, _) => { StopAll(); Save(); };
        C<Button>("Start").Click += (_, _) => StartLive();
        C<Button>("Demo").Click += (_, _) => StartDemo();
        C<Button>("Test").Click += (_, _) =>
        {
            ApplySettings();
            var now = DateTime.Now;
            _speech.Stop();
            _speech.Say(new("test", "Voice alerts are ready. Watch the ground and save your stagger skills.", 200, now, now.AddSeconds(15)));
        };
        C<Button>("Stop").Click += (_, _) => StopAll();
        C<Button>("Overlay").Click += (_, _) => ToggleOverlay();
        C<ComboBox>("Mode").SelectionChanged += (_, _) =>
        {
            if (_changingLanguage) return;
            SaveProfile(); StopAll(); _previousMode = (Difficulty)C<ComboBox>("Mode").SelectedIndex; LoadProfile(); Save();
        };
        C<ComboBox>("Voices").SelectionChanged += async (_, _) =>
        {
            int i = C<ComboBox>("Voices").SelectedIndex;
            if (i >= 0) { _speech.Stop(); _speech.VoiceId = Speech.Voices[i].Id; await WarmVoice(); }
        };
        C<CheckBox>("VoiceOn").Unchecked += (_, _) => _speech.Stop();
        _engine.Alert += warning =>
        {
            _warning = warning;
            C<TextBlock>("Alert").Text = warning.Text;
            C<ListBox>("History").Items.Insert(0, $"{warning.At:HH:mm:ss}  {(_demoStart is null ? warning.Source : "DEMO")}  {warning.Text}");
            while (C<ListBox>("History").Items.Count > 40) C<ListBox>("History").Items.RemoveAt(40);
            _log.Add(new { kind = "warning", at = warning.At, mode = _engine.Mode.ToString(), demo = _demoStart is not null, warning.Key, warning.Text, warning.Source });
            _speech.Say(warning);
        };
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, _) => Tick(); _timer.Start();
        _native = new NativeControls(this, ToggleOverlay, () => C<CheckBox>("VoiceOn").IsChecked = C<CheckBox>("VoiceOn").IsChecked != true,
            tab => C<ComboBox>("OverlayTab").SelectedIndex = tab);
        SourceInitialized += (_, _) => { GameCapture.Exclude(this); UiLayout.DarkCaption(this); };
        Loaded += async (_, _) =>
        {
            await WarmVoice();
            SetLanguage(_prefs.UiLocale);
            if (args.Contains("--smoke")) await Smoke();
        };
        Closing += (_, _) =>
        {
            _closed = true; if (!args.Contains("--smoke")) Save(); _timer.Stop(); _screen?.Dispose(); _live?.Dispose(); _speech.Dispose(); _log.Dispose(); _native.Dispose(); _overlay.Close();
        };
    }

    private void LoadProfile()
    {
        _engine.Mode = _previousMode;
        C<TextBox>("HpValues").Text = _prefs.HpThresholds.GetValueOrDefault(_previousMode, "");
        C<CheckBox>("HpOn").IsChecked = _prefs.HpEnabled.GetValueOrDefault(_previousMode, false);
    }
    private void SaveProfile()
    {
        _prefs.HpThresholds[_previousMode] = C<TextBox>("HpValues").Text;
        _prefs.HpEnabled[_previousMode] = C<CheckBox>("HpOn").IsChecked == true;
    }
    private void ApplySettings()
    {
        _speech.Enabled = !_mute && C<CheckBox>("VoiceOn").IsChecked == true;
        _speech.Volume = C<Slider>("Volume").Value / 100;
        _engine.Countdown = C<CheckBox>("Countdown").IsChecked == true;
        _engine.HpReminders = C<CheckBox>("HpOn").IsChecked == true;
        _engine.WaveHp = C<TextBox>("HpValues").Text.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => double.TryParse(x.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : double.NaN)
            .Where(v => v > 0 && v < 95).Distinct().OrderDescending().ToArray();
    }
    private void Save()
    {
        SaveProfile(); _prefs.Mode = _previousMode; _prefs.VoiceId = _speech.VoiceId; _prefs.Volume = C<Slider>("Volume").Value;
        _prefs.Voice = C<CheckBox>("VoiceOn").IsChecked == true; _prefs.Countdown = C<CheckBox>("Countdown").IsChecked == true;
        _prefs.NetworkInput = C<CheckBox>("NetworkOn").IsChecked == true; _prefs.ScreenInput = C<CheckBox>("ScreenOn").IsChecked == true;
        _prefs.OverlayTab = C<ComboBox>("OverlayTab").SelectedIndex;
        _prefs.UiLocale = UiLanguage.Current;
        _prefs.OverlayLeft = _overlay.Left; _prefs.OverlayTop = _overlay.Top;
        try { File.WriteAllText(Path.Combine(_data, "settings.json.tmp"), JsonSerializer.Serialize(_prefs, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(Path.Combine(_data, "settings.json.tmp"), Path.Combine(_data, "settings.json"), true); }
        catch (Exception ex) { C<TextBlock>("Status").Text = "Could not save settings: " + ex.Message; }
    }
    private async Task WarmVoice()
    {
        _voiceReady = false; C<TextBlock>("VoiceStatus").Text = "Preparing local voice clips…";
        bool ready = await _speech.PrepareAsync();
        if (_closed) return;
        _voiceReady = ready;
        if (ready) C<TextBlock>("VoiceStatus").Text = "English voice pack ready · no internet needed during combat";
    }
    private void StopAll()
    {
        _screen?.Dispose(); _screen = null; _live?.Dispose(); _live = null; _session = false; _demoStart = null; _demoSteps.Clear(); _engine.Reset(); _speech.Stop(); _warning = null;
        C<TextBlock>("State").Text = "STOPPED"; C<TextBlock>("Alert").Text = "Ready for the next pull.";
        C<TextBlock>("Status").Text = "Capture stopped. Choose live alerts or play a synthetic demo.";
        C<TextBlock>("Cast").Text = "No live fight connected"; C<ProgressBar>("CastBar").Value = 0;
        _overlay.Update("Stopped", "Ready for the next pull.", "", 0);
        _overlay.UpdateMeters(DamageView.Empty, ThreatView.Empty);
    }
    private void StartLive()
    {
        StopAll(); ApplySettings(); Save();
        _session = true;
        if (C<CheckBox>("NetworkOn").IsChecked == true) try
        {
            _live = new LiveInput(_engine, _log.Add);
            _live.Reset += () => { _speech.Stop(); _warning = null; _screen?.ResetRecognition(); };
            _live.Start();
            C<TextBlock>("State").Text = "LIVE · EXPERIMENTAL";
            C<TextBlock>("Alert").Text = "Listening for Bakarma's casts.";
            C<TextBlock>("Status").Text = _live.Status;
        }
        catch (Exception ex) { _networkStatus = "Network unavailable: " + ex.Message; _live?.Dispose(); _live = null; }
        if (C<CheckBox>("ScreenOn").IsChecked == true)
        {
            if (_prefs.Calibration?.Valid == true) _screen = new ScreenInput(_prefs.Calibration, _engine, _log.Add);
            else C<TextBlock>("ScreenStatus").Text = "Click Calibrate screen first. Network alerts can still run.";
        }
        if (_live is null && _screen is null) { _session = false; C<TextBlock>("Status").Text = "No input started. Enable a source and calibrate screen input if selected."; }
    }
    private void StartDemo()
    {
        StopAll(); ApplySettings(); _demoStart = DateTime.Now;
        C<TextBlock>("State").Text = "DEMO · SYNTHETIC EVENTS";
        C<TextBlock>("Status").Text = "A 26-second demonstration, not Bakarma's real attack rotation. No capture is running.";
        C<TextBlock>("Alert").Text = "Demo begins in a moment…";
    }
    private void DemoTick(DateTime now)
    {
        if (_demoStart is not DateTime start) return;
        double t = (now - start).TotalSeconds;
        void Step(int second, Action action) { if (t >= second && _demoSteps.Add(second)) action(); }
        Step(2, () => _engine.OnCast(new(42, 2300471, 1800560, 1, now, 1500), 1, now));
        Step(6, () => _engine.OnCast(new(42, 2300471, 1800655, 42, now, 18000), 1, now));
        Step(12, () => _engine.OnStagger(42, 1200, 1200, false, now, now));
        Step(18, () => _engine.OnStagger(42, 0, 0, true, now, now));
        Step(23, () => _engine.OnCast(new(42, 2300471, 1800540, 1, now, 900), 1, now));
        _engine.Tick(now);
        if (t >= 26) { StopAll(); C<TextBlock>("Status").Text = "Demo finished. Live accuracy still needs an in-game test."; }
    }
    private void Tick()
    {
        try
        {
            ApplySettings(); DateTime now = DateTime.Now;
            bool foreground = NativeControls.GameForeground;
            if (_live is not null)
            {
                _live.Tick(now, foreground);
                C<TextBlock>("Status").Text = _live.Status + (foreground ? "" : " · Voice paused while Aion 2 is in the background.");
                C<TextBlock>("State").Text = _live.Failed ? "CAPTURE UNAVAILABLE" : "LIVE · EXPERIMENTAL";
            }
            if (_screen is not null)
            {
                _screen.Tick(now); C<TextBlock>("ScreenStatus").Text = _screen.Status;
                if (_screen.Preview is { } frame) { _lastScreenFrame = frame; _lastScreenDecision = _screen.Decision; }
                if (_live is null) _engine.Tick(now);
            }
            if (_session)
            {
                C<TextBlock>("State").Text = _screen is not null ? _live is not null && !_live.Failed ? "LIVE · HYBRID" : "LIVE · SCREEN" : _live?.Failed == false ? "LIVE · NETWORK" : "CAPTURE UNAVAILABLE";
                if (_live is null) C<TextBlock>("Status").Text = _screen is not null ? "Screen recognition active · network input is off or unavailable" : _networkStatus;
            }
            DemoTick(now);
            var active = _engine.Active;
            double remaining = active is null ? 0 : Math.Max(0, (active.End - now).TotalSeconds);
            string castLine = active is null ? _screen?.Decision is { BossConfirmed: true, Skill: int screenSkill } ? $"Screen: {Encounter.Rules[screenSkill].Label} · timing unavailable" : _live is null ? "No active cast" : $"Bakarma casts observed: {_live.CastsSeen} · HP {(_live.Hp is double hp ? hp.ToString("F1") + "%" : "unknown")}" : $"{Encounter.Rules[active.Skill].Label} · cast ends in {remaining:F1}s";
            C<TextBlock>("Cast").Text = castLine;
            double percent = active is null ? 0 : remaining * 1000 / active.DurationMs * 100;
            C<ProgressBar>("CastBar").Value = percent;
            if (_warning is { } warn && now > warn.Expires)
            {
                _warning = null;
                C<TextBlock>("Alert").Text = _engine.StaggerOpen ? "Stagger window detected." : "Watching for the next mechanic…";
            }
            _overlay.Update(C<TextBlock>("State").Text, C<TextBlock>("Alert").Text, castLine, percent, _engine.LastSource);
            _overlay.UpdateMeters(_live?.Damage ?? DemoDamage(now), _live?.Threat ?? DemoThreat());
            if (_overlay.IsVisible && _overlay.Locked) _overlay.Opacity = _session && !foreground ? 0 : 1;
            UiLanguage.Apply(_root);
        }
        catch (Exception ex)
        {
            _speech.Stop(); _engine.Reset(); C<TextBlock>("Status").Text = "Input paused after an error: " + ex.Message;
            _screen?.Dispose(); _screen = null; _live?.Dispose(); _live = null; _session = false;
            _log.Add(new { kind = "error", at = DateTime.Now, message = ex.Message });
        }
    }
    private void ToggleOverlay()
    {
        if (_overlay.IsVisible) _overlay.Hide(); else { _overlay.Opacity = 1; _overlay.Show(); }
        UpdateOverlayButton();
    }
    private void UpdateOverlayButton() => C<Button>("Overlay").Content = _overlay.IsVisible ? "Hide overlay" : "Show overlay";
    private void SetLanguage(string locale)
    {
        UiLanguage.Current = locale; _prefs.UiLocale = locale; _changingLanguage = true;
        int tab = C<ComboBox>("OverlayTab").SelectedIndex;
        C<ComboBox>("OverlayTab").ItemsSource = new[] { "Boss alerts", "DPS meter", "Threat / boss target" }.Select(UiLanguage.T).ToArray();
        C<ComboBox>("OverlayTab").SelectedIndex = Math.Max(0, tab);
        _changingLanguage = false; UiLanguage.Apply(_root); _overlay.Localize(); HighlightNavigation();
    }
    private void HighlightNavigation()
    {
        int selected = C<ComboBox>("OverlayTab").SelectedIndex;
        string[] names = ["BossNav", "DpsNav", "ThreatNav"];
        for (int i = 0; i < names.Length; i++)
        {
            var button = C<Button>(names[i]);
            button.Background = new SolidColorBrush(i == selected ? Color.FromRgb(24, 43, 61) : Colors.Transparent);
            button.Foreground = new SolidColorBrush(i == selected ? Color.FromRgb(222, 201, 160) : Color.FromRgb(143, 166, 193));
            button.BorderBrush = new SolidColorBrush(i == selected ? Color.FromRgb(63, 76, 87) : Colors.Transparent);
        }
    }

    private async Task CalibrateScreen()
    {
        StopAll();
        var target = GameCapture.Find();
        if (target is null) { MessageBox.Show(this, "Open Aion 2 in borderless/windowed mode, target Bakarma so his name is visible, then click Calibrate screen.", "Aion 2 not found"); return; }
        bool overlayVisible = _overlay.IsVisible;
        _overlay.Hide(); Hide();
        try
        {
            GameCapture.Focus(target); await Task.Delay(500);
            var current = GameCapture.Foreground();
            if (current is null || current.Handle != target.Handle) throw new InvalidOperationException("Aion 2 could not be brought to the foreground. Select it and try calibration again.");
            using var bitmap = GameCapture.Capture(current.Client);
            var picker = new ScreenCalibrationWindow(current, GameCapture.Png(bitmap));
            if (picker.ShowDialog() == true && picker.Calibration is { Valid: true } calibration)
            {
                _prefs.Calibration = calibration; Save();
                C<TextBlock>("ScreenStatus").Text = "Calibrated. Click Start live alerts, then return to Aion 2.";
            }
        }
        catch (Exception ex) { C<TextBlock>("ScreenStatus").Text = "Calibration failed: " + ex.Message; }
        finally { Show(); Activate(); if (overlayVisible) _overlay.Show(); UpdateOverlayButton(); }
    }
    private DamageView DemoDamage(DateTime now)
    {
        if (_demoStart is not DateTime start) return DamageView.Empty;
        double elapsed = Math.Max(1, (now - start).TotalSeconds);
        return new(elapsed, elapsed * 28500, true, new[]
        {
            new DamageRow(1, "You", 9800, elapsed * 9800, 34.4, true),
            new DamageRow(2, "Aetherblade", 7600, elapsed * 7600, 26.7, false),
            new DamageRow(3, "Moonweaver", 6400, elapsed * 6400, 22.5, false),
            new DamageRow(4, "Ironwarden", 3200, elapsed * 3200, 11.2, false),
            new DamageRow(5, "Dawnkeeper", 1500, elapsed * 1500, 5.2, false)
        });
    }
    private ThreatView DemoThreat() => _demoStart is null ? ThreatView.Empty : new("Ironwarden", false, new[]
    {
        new ThreatRow(1, "You", 100, true, false), new ThreatRow(2, "Aetherblade", 77.6, false, false), new ThreatRow(3, "Moonweaver", 65.3, false, false)
    });

    private async Task Smoke()
    {
        var ocr = await Task.Run(() => OcrValidation.Run(_data));
        // Exercise the real audio decoder silently, even with --mute.
        _speech.Enabled = true; _speech.Volume = 0;
        _speech.Say(new("audio-selftest", "Voice alerts are ready. Watch the ground and save your stagger skills.", 200, DateTime.Now, DateTime.Now.AddSeconds(10)));
        await Task.Delay(4200);
        StartDemo(); await Task.Delay(6400);
        ToggleOverlay(); _overlay.SetLocked(true);
        bool clickThrough = NativeControls.IsClickThrough(_overlay);
        int i = Array.IndexOf(_args, "--screenshot");
        if (i >= 0 && i + 1 < _args.Length)
        {
            int settingsTab = C<TabControl>("SettingsTabs").SelectedIndex;
            foreach (int page in new[] { 1, 2 })
            {
                C<TabControl>("SettingsTabs").SelectedIndex = page; UiLanguage.Apply(_root); UpdateLayout();
                var pagePicture = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
                pagePicture.Render(this); var pageEncoder = new PngBitmapEncoder(); pageEncoder.Frames.Add(BitmapFrame.Create(pagePicture));
                using var pageFile = File.Create(Path.ChangeExtension(_args[i + 1], page == 1 ? ".capture.png" : ".audio.png")); pageEncoder.Save(pageFile);
            }
            C<TabControl>("SettingsTabs").SelectedIndex = settingsTab;
            UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(this); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(_args[i + 1]); png.Save(file);
            _overlay.UpdateLayout();
            var overlayBitmap = new RenderTargetBitmap((int)_overlay.ActualWidth, (int)_overlay.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            overlayBitmap.Render(_overlay); var overlayPng = new PngBitmapEncoder(); overlayPng.Frames.Add(BitmapFrame.Create(overlayBitmap));
            using var overlayFile = File.Create(Path.ChangeExtension(_args[i + 1], ".overlay.png")); overlayPng.Save(overlayFile);
            foreach (int tab in new[] { 1, 2 })
            {
                _overlay.SelectTab(tab); _overlay.UpdateMeters(DemoDamage(DateTime.Now), DemoThreat()); _overlay.UpdateLayout();
                var picture = new RenderTargetBitmap((int)_overlay.ActualWidth, (int)_overlay.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                picture.Render(_overlay); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(picture));
                using var tabFile = File.Create(Path.ChangeExtension(_args[i + 1], tab == 1 ? ".dps.png" : ".threat.png")); encoder.Save(tabFile);
            }
        }
        if (_args.Contains("--full-demo")) await Task.Delay(20500);
        File.WriteAllText(Path.Combine(_data, "smoke-result.json"), JsonSerializer.Serialize(new
        {
            ok = ocr.Ok && _voiceReady && _speech.OpenedClips > 0 && _speech.LastError is null && clickThrough,
            ocr,
            mode = _engine.Mode.ToString(), voiceReady = _voiceReady, openedAudioClips = _speech.OpenedClips, audioError = _speech.LastError,
            overlayClickThrough = clickThrough,
            voices = Speech.Voices.Select(v => new { v.DisplayName, v.Language }),
            history = C<ListBox>("History").Items.Cast<string>().ToArray(),
            status = C<TextBlock>("Status").Text
        }, new JsonSerializerOptions { WriteIndented = true }));
        Close();
    }
}

public sealed class AlertOverlay : Window
{
    private readonly TextBlock _label = new() { Foreground = Brush("#9CAEC5"), FontSize = 10, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
    private readonly TextBlock _state = new() { Foreground = Brush("#70839D"), FontSize = 9, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _text = new() { Foreground = Brush("#EFF5FC"), FontSize = 17, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap, Margin = new Thickness(0, 5, 0, 0) };
    private readonly TextBlock _time = new() { Foreground = Brush("#83E5D5"), FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly ProgressBar _bar = new() { Height = 2, Maximum = 100, Margin = new Thickness(0, 9, 0, 0), Foreground = Brush("#83E5D5"), Background = Brush("#263547"), BorderThickness = new Thickness(0) };
    private readonly Border _accent = new() { Width = 2, Background = Brush("#83E5D5"), CornerRadius = new CornerRadius(1), Margin = new Thickness(0, 1, 11, 1) };
    private readonly Grid _controls = new() { Margin = new Thickness(0, 10, 0, 0) };
    private readonly Button _lock = SmallButton("Lock", "Lock overlay and let clicks through to the game");
    private readonly StackPanel _bossPanel = new();
    private readonly StackPanel _meterPanel = new() { Margin = new Thickness(0, 9, 0, 0) };
    private readonly Grid _tabs = new() { Margin = new Thickness(0, 10, 0, 0) };
    private readonly List<Button> _tabButtons = [];
    private DamageView _damage = DamageView.Empty;
    private ThreatView _threat = ThreatView.Empty;
    private int _tab = -1;
    private DateTime _meterPaint;
    private string _bossLabel = "DAEVA SENTINEL", _bossTime = "";
    public bool Locked { get; private set; }
    public event Action? HideRequested;
    public event Action<int>? TabChanged;
    public AlertOverlay()
    {
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(Path.Combine(AppContext.BaseDirectory, "ui", "Theme.xaml")) });
        Title = "Daeva Sentinel overlay"; Width = 340; SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true; ShowInTaskbar = false; ShowActivated = false;
        var panel = new StackPanel();
        var heading = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_time, Dock.Right); heading.Children.Add(_time);
        var badge = new Border { Background = Brush("#1A2B3B"), CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 2, 5, 2), Margin = new Thickness(5, 0, 0, 0), Child = _state };
        _state.Margin = new Thickness(0);
        DockPanel.SetDock(badge, Dock.Right); heading.Children.Add(badge);
        var crest = new TextBlock { Text = "◇", Foreground = Brush("#B99D6B"), FontSize = 13, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(crest, Dock.Left); heading.Children.Add(crest); heading.Children.Add(_label);
        panel.Children.Add(heading); _bossPanel.Children.Add(_text); _bossPanel.Children.Add(_bar);
        panel.Children.Add(_bossPanel); panel.Children.Add(_meterPanel);
        for (int tab = 0; tab < 3; tab++)
        {
            int selected = tab; _tabs.ColumnDefinitions.Add(new ColumnDefinition());
            var button = SmallButton(new[] { "◈  Boss", "▥  DPS", "◇  Threat" }[tab], "Ctrl+Alt+" + (tab + 1));
            button.Margin = new Thickness(0, 0, tab == 2 ? 0 : 5, 0); button.Click += (_, _) => { SelectTab(selected); TabChanged?.Invoke(selected); };
            Grid.SetColumn(button, tab); _tabs.Children.Add(button); _tabButtons.Add(button);
        }
        panel.Children.Add(_tabs);
        _controls.ColumnDefinitions.Add(new ColumnDefinition());
        _controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _controls.Children.Add(new TextBlock { Text = "Drag to position", FontSize = 10, Foreground = Brush("#778AA4"), VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(_lock, 1); _controls.Children.Add(_lock);
        var hide = SmallButton("×", "Hide overlay · Ctrl+Alt+B to show"); hide.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(hide, 2); _controls.Children.Add(hide); panel.Children.Add(_controls);
        var body = new DockPanel { Margin = new Thickness(12, 10, 12, 10) };
        DockPanel.SetDock(_accent, Dock.Left); body.Children.Add(_accent); body.Children.Add(panel);
        Content = new Border { Background = new LinearGradientBrush(Color.FromArgb(228, 10, 17, 29), Color.FromArgb(228, 21, 36, 53), 35), BorderBrush = Brush("#806D573B"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Child = body };
        MouseLeftButtonDown += (_, e) =>
        {
            var element = e.OriginalSource as DependencyObject;
            while (element is not null) { if (element is Button) return; element = VisualTreeHelper.GetParent(element); }
            if (!Locked) try { DragMove(); } catch { }
        };
        _lock.Click += (_, _) => SetLocked(true); hide.Click += (_, _) => HideRequested?.Invoke();
        IsVisibleChanged += (_, _) => { if (IsVisible) SetLocked(false); };
        SourceInitialized += (_, _) => GameCapture.Exclude(this);
        SelectTab(0);
    }
    private static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
    private static Button SmallButton(string text, string tooltip)
    {
        var button = new Button { Content = text, ToolTip = tooltip, FontFamily = new FontFamily("Segoe UI"), FontSize = 10,
            Foreground = Brush("#C4D1E2"), Background = Brush("#253347"), BorderThickness = new Thickness(0), Padding = new Thickness(10, 4, 10, 4), Cursor = System.Windows.Input.Cursors.Hand, Focusable = false };
        button.Template = (ControlTemplate)XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button">
              <Border x:Name="Surface" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="{TemplateBinding Background}" CornerRadius="4" Padding="{TemplateBinding Padding}">
                <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
              </Border>
              <ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Surface" Property="Background" Value="#3B506A"/></Trigger></ControlTemplate.Triggers>
            </ControlTemplate>
            """);
        return button;
    }
    public void SetLocked(bool value)
    {
        Locked = value; _controls.Visibility = _tabs.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
        Cursor = value ? System.Windows.Input.Cursors.Arrow : System.Windows.Input.Cursors.SizeAll;
        NativeControls.ClickThrough(this, value);
    }
    public void Update(string state, string text, string cast, double value, string source = "")
    {
        text = UiLanguage.English(text);
        bool casting = value > 0;
        const string separator = " · cast ends in ";
        int split = cast.IndexOf(separator, StringComparison.Ordinal);
        _bossLabel = casting && split >= 0 ? cast[..split].ToUpperInvariant() : "DAEVA SENTINEL";
        _bossTime = casting && split >= 0 ? cast[(split + separator.Length)..] : "";
        _state.Text = state.Contains("DEMO") ? "DEMO" : source.Length > 0 ? source : state.Contains("LIVE") ? "LIVE" : "";
        _text.Text = text switch
        {
            "Waves incoming. Move toward Bakarma and jump." => "Move in · jump the waves",
            "Frontal attack. Watch the boss." => "Watch the frontal attack",
            "Frontal sweep. Get behind Bakarma." => "Get behind Bakarma",
            "Frontal charge. Move out of the line." => "Sidestep the frontal charge",
            "Projectiles incoming. Watch the ground." => "Dodge incoming projectiles",
            "Water bomb on you. Keep moving." => "Bomb on you · keep moving",
            "Water bombs incoming. Watch the ground." => "Water bombs · watch the ground",
            "Area attack. Watch the ground." => "Area attack · watch the ground",
            "Wave phase soon. Save your stagger skills." => "Waves soon · save stagger",
            "Stagger now. Use your stagger skills." => "Use stagger skills · now",
            "Stagger broken. Attack Bakarma." => "Stagger broken · attack",
            "Watching for the next mechanic…" => "Waiting for next mechanic",
            "Listening for Bakarma's casts." => "Waiting for Bakarma",
            "Your next warning appears here." => "Waiting for Bakarma",
            _ => text
        };
        _text.ToolTip = text;
        var color = Brush(text.StartsWith("Stagger now") ? "#FFD38A" : text.StartsWith("Frontal") || text.StartsWith("Water bomb on you") ? "#FFAD9D" : "#83E5D5");
        _accent.Background = color; _bar.Foreground = color; _time.Foreground = color;
        _bar.Value = value; _bar.Visibility = casting ? Visibility.Visible : Visibility.Collapsed;
        Header();
        UiLanguage.Apply((DependencyObject)Content);
    }
    public void SelectTab(int tab)
    {
        tab = Math.Clamp(tab, 0, 2); if (_tab == tab) return; _tab = tab;
        _bossPanel.Visibility = tab == 0 ? Visibility.Visible : Visibility.Collapsed;
        _meterPanel.Visibility = tab == 0 ? Visibility.Collapsed : Visibility.Visible;
        for (int i = 0; i < _tabButtons.Count; i++) { _tabButtons[i].Background = Brush(i == tab ? "#24594F" : "#202D40"); _tabButtons[i].Foreground = Brush(i == tab ? "#A0F4DD" : "#92A7C1"); }
        _meterPaint = default; Header(); PaintMeter();
    }
    private void Header()
    {
        _label.Text = UiLanguage.T(_tab == 0 ? _bossLabel : _tab == 1 ? "DAMAGE PER SECOND  /  PARTY" : "ESTIMATED THREAT");
        _time.Text = _tab == 0 ? _bossTime : _tab == 1 && _damage.Rows.Count > 0 ? TimeSpan.FromSeconds(_damage.Seconds).ToString(@"mm\:ss") : "";
        _label.Foreground = Brush("#DCC18F");
    }
    public void UpdateMeters(DamageView damage, ThreatView threat)
    {
        _damage = damage; _threat = threat; Header();
        if (_tab > 0 && DateTime.Now - _meterPaint > TimeSpan.FromMilliseconds(250)) PaintMeter();
    }
    private static string Number(double value) => value >= 1000000 ? (value / 1000000).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "m" : value >= 1000 ? (value / 1000).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "k" : value.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
    private void Caption(string text, string color = "#8DA3BE") => _meterPanel.Children.Add(new TextBlock { Text = text, FontSize = 10, Foreground = Brush(color), Margin = new Thickness(0, 4, 0, 3), TextWrapping = TextWrapping.Wrap });
    private void MeterRow(int rank, string name, string value, double width, string color, string tooltip)
    {
        var row = new Grid { Height = 26, Margin = new Thickness(0, 0, 0, 3), ToolTip = tooltip };
        row.Children.Add(new Border { Background = Brush("#111F30"), CornerRadius = new CornerRadius(3) });
        Color tint = ((SolidColorBrush)Brush(color)).Color;
        var bar = new Border { Background = new LinearGradientBrush(Color.FromArgb(78, tint.R, tint.G, tint.B), Color.FromArgb(30, tint.R, tint.G, tint.B), 0), CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left };
        row.SizeChanged += (_, _) => bar.Width = Math.Max(0, row.ActualWidth * Math.Clamp(width, 0, 100) / 100);
        row.Children.Add(bar);
        row.Children.Add(new Border { Width = 2, HorizontalAlignment = HorizontalAlignment.Left, Background = Brush(color), Opacity = .6, Margin = new Thickness(0, 4, 0, 4), CornerRadius = new CornerRadius(1) });
        var content = new Grid { Margin = new Thickness(8, 0, 8, 0) }; content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) }); content.ColumnDefinitions.Add(new ColumnDefinition()); content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.Children.Add(new TextBlock { Text = rank.ToString(), FontSize = 10, Foreground = Brush("#7189A4"), VerticalAlignment = VerticalAlignment.Center });
        var label = new TextBlock { Text = name, FontSize = 12, Foreground = Brush("#E5EDF9"), TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center, Tag = "GameName" };
        Grid.SetColumn(label, 1); content.Children.Add(label);
        var number = new TextBlock { Text = value, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brush(color), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(number, 2); content.Children.Add(number); row.Children.Add(content); _meterPanel.Children.Add(row);
    }
    private void PaintMeter()
    {
        _meterPaint = DateTime.Now; _meterPanel.Children.Clear();
        if (_tab == 1)
        {
            if (_damage.Rows.Count == 0) { Caption("Waiting for recorded party damage…"); Caption("Network events are required for this view.", "#647D9C"); return; }
            double lead = Math.Max(1, _damage.Rows.Max(x => x.Dps)); int i = 0;
            string[] colors = ["#83E5D5", "#A7A0FF", "#7DBAFB", "#F1C276", "#E69ECC"];
            foreach (var row in _damage.Rows) MeterRow(++i, row.IsYou ? UiLanguage.T("You") : row.Name, Number(row.Dps), row.Dps / lead * 100, row.IsYou ? "#83E5D5" : colors[(i - 1) % colors.Length], UiLanguage.Current == "pl" ? $"{Number(row.Total)} obrażeń · {row.Share:F1}% obrażeń drużyny" : $"{Number(row.Total)} damage · {row.Share:F1}% of recorded party damage");
            Caption(UiLanguage.Current == "pl" ? $"SUMA {Number(_damage.Total)}     DPS DRUŻYNY {Number(_damage.Total / Math.Max(1, _damage.Seconds))}" + (_damage.Active ? "" : "  ·  OSTATNIA WALKA") : $"TOTAL {Number(_damage.Total)}     PARTY DPS {Number(_damage.Total / Math.Max(1, _damage.Seconds))}" + (_damage.Active ? "" : "  ·  LAST FIGHT"));
        }
        else if (_tab == 2)
        {
            Caption(UiLanguage.T("Boss target  →  ") + (_threat.TargetIsYou ? UiLanguage.T("You") : _threat.Target), _threat.TargetIsYou ? "#FFD38A" : "#A7BDD7");
            Caption("DAMAGE PROXY · NOT SERVER ENMITY", "#687F9E");
            int i = 0;
            foreach (var row in _threat.Rows) MeterRow(++i, row.IsYou ? UiLanguage.T("You") : row.Name, row.Relative.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%", row.Relative, row.IsYou ? "#83E5D5" : "#A7A0FF", UiLanguage.Current == "pl" ? "Obrażenia w porównaniu do lidera. To nie jest rzeczywiste aggro; prowokacje i leczenie nie są uwzględnione." : "Recorded damage relative to the damage leader. Not actual threat; taunts and healing are not modeled.");
            if (_threat.Rows.Count == 0) Caption("Waiting for Bakarma network events…");
        }
        UiLanguage.Apply(_meterPanel);
    }
    public void Localize() { UiLanguage.Apply((DependencyObject)Content); _meterPaint = default; PaintMeter(); }
}

public sealed class NativeControls : IDisposable
{
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    private HwndSource? _source;
    public NativeControls(Window window, Action overlay, Action mute, Action<int> tab)
    {
        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle; _source = HwndSource.FromHwnd(handle);
            RegisterHotKey(handle, 1, 0x4003, 0x42); RegisterHotKey(handle, 2, 0x4003, 0x4D);
            for (int i = 0; i < 3; i++) RegisterHotKey(handle, i + 3, 0x4003, (uint)(0x31 + i));
            _source.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                if (msg == 0x312) { int id = wParam.ToInt32(); if (id == 1) overlay(); else if (id == 2) mute(); else if (id is >= 3 and <= 5) tab(id - 3); handled = true; }
                return IntPtr.Zero;
            });
        };
    }
    public static bool GameForeground
    {
        get
        {
            try
            {
                GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
                string name = Process.GetProcessById((int)pid).ProcessName;
                return name.Equals("AION2", StringComparison.OrdinalIgnoreCase) || name.Equals("AION2-Win64-Shipping", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }
    public static void ClickThrough(Window window, bool enabled)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        long style = GetWindowLongPtr(handle, -20).ToInt64();
        SetWindowLongPtr(handle, -20, new IntPtr(enabled ? style | 0x20 | 0x8000000 : style & ~0x20 & ~0x8000000));
    }
    public static bool IsClickThrough(Window window) => (GetWindowLongPtr(new WindowInteropHelper(window).Handle, -20).ToInt64() & 0x20) != 0;
    public void Dispose() { if (_source is not null) for (int i = 1; i <= 5; i++) UnregisterHotKey(_source.Handle, i); }
}
