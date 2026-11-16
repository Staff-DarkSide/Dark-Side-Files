using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace DarkSideLauncher;

public partial class MainWindow : Window
{
    private enum State { Checking, NotInstalled, UpdateAvailable, Ready, Busy }

    private readonly LauncherSettings _settings = LauncherSettings.Load();
    private readonly CancellationTokenSource _cts = new();
    private State _state = State.Checking;

    public MainWindow()
    {
        InitializeComponent();
        PathBox.Text = _settings.InstallPath;
        FooterText.Text = $"DARKSIDE · ULTIMA ONLINE · v{Updater.CurrentVersion}";
        Loaded += async (_, _) => await StartupAsync();
        Closing += (_, _) => _cts.Cancel();
    }

    private async Task StartupAsync()
    {
        SetState(State.Checking, "Checking for launcher updates...");
        try
        {
            var update = await Updater.CheckAsync(_cts.Token);
            if (update != null &&
                MessageBox.Show(this,
                    $"A new launcher version ({update.Version}) is available.\nUpdate now?",
                    "DARKSIDE", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            {
                SetState(State.Busy, "Downloading launcher update...");
                await Updater.DownloadAndRunAsync(update, new Progress<InstallProgress>(OnProgress), _cts.Token);
                Application.Current.Shutdown();
                return;
            }
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { App.Log(ex); }

        await RefreshClientStateAsync();
    }

    private async Task RefreshClientStateAsync()
    {
        if (!_settings.IsClientInstalled)
        {
            SetState(State.NotInstalled, "Client not installed. Press INSTALL.");
            return;
        }

        SetState(State.Checking, "Verifying game files...");
        try
        {
            var outdated = await Installer.FindOutdatedAsync(
                _settings.InstallPath, new Progress<InstallProgress>(OnProgress), _cts.Token);

            if (outdated.Count == 0)
                SetState(State.Ready, "Client up to date.");
            else
                SetState(State.UpdateAvailable, $"Update available ({outdated.Count} files).");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            App.Log(ex);
            SetState(State.Ready, "Could not check for updates. You can still play.");
        }
    }

    private async Task InstallAsync()
    {
        var path = PathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Select an install folder.");

        Directory.CreateDirectory(path);
        _settings.InstallPath = path;
        _settings.Save();

        var progress = new Progress<InstallProgress>(OnProgress);

        // 1) Base game (only if not installed yet)
        if (!_settings.IsClientInstalled)
        {
            SetState(State.Busy, "Downloading game...");
            await Installer.InstallBaseGameAsync(path, progress, _cts.Token);
        }

        // 2) Updates on top of the game
        SetState(State.Busy, "Checking updates...");
        var outdated = await Installer.FindOutdatedAsync(path, progress, _cts.Token);
        await Installer.DownloadAsync(path, outdated, progress, _cts.Token);

        SetState(State.Ready, "Installation complete.");
    }

    private void Play()
    {
        var exe = _settings.GameExePath;
        if (exe == null)
        {
            SetState(State.NotInstalled, "Game executable not found. Press INSTALL.");
            return;
        }

        Process.Start(new ProcessStartInfo(exe, LauncherSettings.GameArguments)
        {
            WorkingDirectory = Path.GetDirectoryName(exe)!,
            UseShellExecute = true
        });
        Application.Current.Shutdown();
    }

    private void SetState(State s, string status)
    {
        _state = s;
        StatusText.Text = status;

        bool busy = s is State.Busy or State.Checking;
        DownloadBar.Visibility = busy ? Visibility.Visible : Visibility.Hidden;
        DownloadBar.IsIndeterminate = s == State.Checking;
        DownloadBar.Value = 0;
        MainButton.IsEnabled = !busy;
        BrowseButton.IsEnabled = !busy;
        MainButton.Content = s switch
        {
            State.NotInstalled => "INSTALL",
            State.UpdateAvailable => "UPDATE",
            State.Ready => "PLAY",
            _ => "PLEASE WAIT"
        };
    }

    private void OnProgress(InstallProgress p)
    {
        StatusText.Text = p.Message;
        if (_state == State.Busy) DownloadBar.Value = p.Percent;
    }

    private async void MainButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            switch (_state)
            {
                case State.NotInstalled:
                case State.UpdateAvailable:
                    await InstallAsync();
                    break;
                case State.Ready:
                    Play();
                    break;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            App.Log(ex);
            SetState(_settings.IsClientInstalled ? State.UpdateAvailable : State.NotInstalled,
                     "Error: " + ex.Message);
        }
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Select the DARKSIDE client folder",
            InitialDirectory = Directory.Exists(PathBox.Text) ? PathBox.Text : string.Empty
        };
        if (dlg.ShowDialog(this) != true) return;

        PathBox.Text = dlg.FolderName;
        _settings.InstallPath = dlg.FolderName;
        _settings.Save();
        await RefreshClientStateAsync();
    }

    private void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
