using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace DarkSide.Launcher
{
    public partial class MainWindow : Window
    {
        private readonly LauncherSettings _settings = LauncherSettings.Load();
        private bool _busy;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += async (s, e) =>
            {
                RefreshPath();
                if (IsInstalled) await RunUpdateAsync();
                else SetStatus("Client not installed. Click INSTALL.");
            };
        }

        private bool IsInstalled => File.Exists(_settings.ClientPath);

        private void RefreshPath()
        {
            PathBox.Text = _settings.ClientPath;
            PlayButton.Content = IsInstalled ? "PLAY" : "INSTALL";
        }

        private void SetStatus(string text) => StatusText.Text = text;

        private void SetBusy(bool busy)
        {
            _busy = busy;
            PlayButton.IsEnabled = !busy;
            BrowseButton.IsEnabled = !busy;
        }

        // Elegir manualmente un cliente ya instalado
        private bool PickClient()
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select client.exe from your DARKSIDE installation",
                Filter = "Executables (*.exe)|*.exe",
                CheckFileExists = true
            };
            if (IsInstalled) dlg.InitialDirectory = Path.GetDirectoryName(_settings.ClientPath);

            if (dlg.ShowDialog(this) != true) return false;

            _settings.ClientPath = dlg.FileName;
            _settings.Save();
            RefreshPath();
            return true;
        }

        // Descarga e instala el cliente completo desde GitHub
        private async Task<bool> InstallAsync()
        {
            var dlg = new OpenFolderDialog
            {
                Title = "Choose where to install DARKSIDE (about 4 GB free space needed)"
            };
            if (dlg.ShowDialog(this) != true) return false;

            string dest = Path.Combine(dlg.FolderName, "DARKSIDE");

            SetBusy(true);
            try
            {
                var progress = new Progress<(double, string)>(p =>
                {
                    Bar.Value = p.Item1;
                    SetStatus(p.Item2);
                });

                string exe = await Installer.InstallAsync(dest, progress);
                if (string.IsNullOrEmpty(exe))
                    throw new FileNotFoundException("client.exe was not found after installing.");

                _settings.ClientPath = exe;
                _settings.Save();
                RefreshPath();
                SetStatus("Installation complete. You can play now!");
                return true;
            }
            catch (Exception ex)
            {
                SetStatus("Install error: " + ex.Message);
                return false;
            }
            finally
            {
                SetBusy(false);
            }
        }

        // Actualizaciones por manifest. Si falla, NO bloquea el juego.
        private async Task<bool> RunUpdateAsync()
        {
            if (!IsInstalled) return true;
            if (string.IsNullOrWhiteSpace(_settings.ManifestUrl))
            {
                SetStatus("Ready");
                return true;
            }

            SetBusy(true);
            try
            {
                var progress = new Progress<(double, string)>(p =>
                {
                    Bar.Value = p.Item1;
                    SetStatus(p.Item2);
                });
                await Updater.UpdateAsync(_settings.ManifestUrl, Path.GetDirectoryName(_settings.ClientPath), progress);
                Bar.Value = 100;
                SetStatus("Client up to date");
            }
            catch (Exception)
            {
                SetStatus("Ready (could not check for updates)");
            }
            finally
            {
                SetBusy(false);
            }
            return true;
        }

        private async void Play_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;

            if (!IsInstalled)
            {
                if (!await InstallAsync()) return;
                return; // after installing, the button becomes PLAY
            }

            await RunUpdateAsync();

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _settings.ClientPath,
                    Arguments = _settings.Arguments ?? "",
                    WorkingDirectory = Path.GetDirectoryName(_settings.ClientPath),
                    UseShellExecute = true
                });

                if (_settings.CloseOnPlay) Close();
                else WindowState = WindowState.Minimized;
            }
            catch (Exception ex)
            {
                SetStatus("Could not start the client: " + ex.Message);
            }
        }

        private async void Browse_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            if (PickClient())
                await RunUpdateAsync();
        }

        private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
            {
                try { DragMove(); } catch { }
            }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
