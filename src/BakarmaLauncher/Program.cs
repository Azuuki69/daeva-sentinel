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
    private const string Layout = """
    <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
      <Grid.Background><LinearGradientBrush StartPoint="0,0" EndPoint="1,1"><GradientStop Color="#090F1A"/><GradientStop Color="#182C42" Offset="1"/></LinearGradientBrush></Grid.Background>
      <Grid.Resources>
        <Style TargetType="TextBlock"><Setter Property="Foreground" Value="#B1C0D3"/><Setter Property="TextWrapping" Value="Wrap"/></Style>
        <Style TargetType="Button"><Setter Property="Foreground" Value="#F1E6D3"/><Setter Property="Padding" Value="16,11"/><Setter Property="Background" Value="#22374C"/><Setter Property="BorderBrush" Value="#706044"/><Setter Property="BorderThickness" Value="1"/><Setter Property="Cursor" Value="Hand"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button"><Border x:Name="Surface" Padding="{TemplateBinding Padding}" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" CornerRadius="5"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Surface" Property="Background" Value="#355169"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
      </Grid.Resources>
      <StackPanel Margin="34">
        <DockPanel><ComboBox x:Name="Language" DockPanel.Dock="Right" Width="105"/><TextBlock Text="AION 2 COMPANION" Foreground="#D2B67F" FontSize="11" VerticalAlignment="Center"/></DockPanel>
        <TextBlock Text="◈" FontFamily="Georgia" Foreground="#D8BC82" FontSize="58" HorizontalAlignment="Center" Margin="0,22,0,0"/>
        <TextBlock Text="DAEVA SENTINEL" FontFamily="Georgia" Foreground="#F1D9B0" FontSize="30" HorizontalAlignment="Center" Margin="0,8,0,0"/>
        <TextBlock Text="Signed updates · English voice · No subscription" HorizontalAlignment="Center" FontSize="11" Margin="0,12,0,24"/>
        <Border BorderBrush="#6E5B3F" BorderThickness="1" CornerRadius="7" Background="#A7111B2A" Padding="18"><StackPanel>
          <TextBlock x:Name="State" Text="Ready to play" Foreground="#8ADBD2" FontSize="17"/>
          <DockPanel Margin="0,12,0,0"><TextBlock Text="Installed version"/><TextBlock x:Name="Version" HorizontalAlignment="Right" Foreground="#F2DEC0"/></DockPanel>
          <TextBlock x:Name="Latest" Margin="0,6,0,0"/>
          <ProgressBar x:Name="Progress" Height="3" Margin="0,14,0,0" Maximum="100" Background="#263B50" Foreground="#9BDECF"/>
          <TextBlock x:Name="Details" Text="Settings are preserved across updates." FontSize="11" Margin="0,12,0,0"/>
        </StackPanel></Border>
        <Button x:Name="Play" Content="Launch companion" Background="#34645F" BorderBrush="#89BEB0" Margin="0,20,0,10" FontSize="16" Padding="18,14"/>
        <Grid><Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition/></Grid.ColumnDefinitions><Button x:Name="Check" Content="Check for updates" Margin="0,0,5,0"/><Button x:Name="Rollback" Content="Rollback" Grid.Column="1" Margin="5,0,0,0"/></Grid>
        <Button x:Name="Github" Content="Open GitHub" Margin="0,10,0,0" Background="Transparent"/>
      </StackPanel>
    </Grid>
    """;
    private readonly Grid _root;
    private readonly UpdateClient _updates = new(AppContext.BaseDirectory);
    private readonly CancellationTokenSource _cancel = new();
    private bool _busy;
    private ReleaseManifest? _latest;
    private readonly string _config = Path.Combine(AppContext.BaseDirectory, "launcher-language.txt");
    private T C<T>(string name) where T : FrameworkElement => (T)_root.FindName(name);
    public LauncherWindow(string[] args)
    {
        Title = "Daeva Sentinel Launcher"; Width = 600; Height = 760; ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; FontFamily = new FontFamily("Segoe UI");
        _root = (Grid)XamlReader.Parse(Layout); Content = _root;
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
