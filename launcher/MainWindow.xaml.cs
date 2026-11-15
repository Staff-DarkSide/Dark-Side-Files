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
                else SetStatus("Cliente no instalado. Pulsa INSTALAR.");
            };
        }

        private bool IsInstalled => File.Exists(_settings.ClientPath);

        private void RefreshPath()
        {
            PathBox.Text = _settings.ClientPath;
            PlayButton.Content = IsInstalled ? "JUGAR" : "INSTALAR";
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
                Title = "Selecciona client.exe de tu instalacion de DARKSIDE",
                Filter = "Ejecutables (*.exe)|*.exe",
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
                Title = "Elige donde instalar DARKSIDE (necesita unos 4 GB libres)"
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
                    throw new FileNotFoundException("No se encontro client.exe tras instalar.");

                _settings.ClientPath = exe;
                _settings.Save();
                RefreshPath();
                SetStatus("Instalacion completada. Ya puedes jugar.");
                return true;
            }
            catch (Exception ex)
            {
                SetStatus("Error al instalar: " + ex.Message);
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
                SetStatus("Preparado");
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
                SetStatus("Cliente actualizado");
            }
            catch (Exception)
            {
                SetStatus("Preparado (no se pudo comprobar actualizaciones)");
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
                return; // tras instalar, el boton pasa a JUGAR
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
                SetStatus("No se pudo iniciar el cliente: " + ex.Message);
            }
        }

        private async void Browse_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            if (PickClient())
                await RunUpdateAsync();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
