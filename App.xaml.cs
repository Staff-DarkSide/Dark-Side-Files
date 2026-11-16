using System.IO;
using System.Windows;

namespace DarkSideLauncher;

public partial class App : Application
{
    private static Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, "DARKSIDE_Launcher_SingleInstance", out var isNew);
        if (!isNew)
        {
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Log(args.Exception);
            MessageBox.Show(args.Exception.Message, "DARKSIDE", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        base.OnStartup(e);
    }

    public static void Log(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(LauncherSettings.DataDir);
            File.AppendAllText(
                Path.Combine(LauncherSettings.DataDir, "launcher.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { }
    }
}
