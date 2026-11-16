using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DarkSideLauncher;

public sealed class LauncherSettings
{
    // ===== EDIT HERE =====
    public const string RepoOwner = "Staff-DarkSide";
    public const string RepoName = "Dark-Side-Files";
    public const string Branch = "main";

    // Base game (downloaded first, then extracted)
    public const string GameArchiveUrl = "https://github.com/Staff-DarkSide/Dark-Side-Files/releases/download/v1.0.0/Dark.side.uo.rar";
    public const string GameArchiveName = "Dark.side.uo.rar";

    // Updates (applied on top of the base game): repo folder, same structure as the game folder
    public const string ClientSubfolder = "updates";

    public const string GameExecutable = "";               // "" = auto-detect (ClassicUO.exe, Razor.exe, client.exe...)
    public const string GameArguments = "";

    // Launcher self-update: GitHub Releases with tag "launcher-v1.0.1" and asset DARKSIDE-Setup.exe
    public const string LauncherTagPrefix = "launcher-v";
    public const string InstallerAssetName = "DARKSIDE-Setup.exe";
    // =====================

    public const string ExtractingMarker = ".extracting";

    private static readonly string[] KnownGameExecutables =
    {
        "ClassicUO.exe", "OrionUO.exe", "Razor.exe", "client.exe"
    };

    public string InstallPath { get; set; } = DefaultInstallPath;

    [JsonIgnore]
    public string? GameExePath
    {
        get
        {
            try
            {
                if (!Directory.Exists(InstallPath)) return null;

                if (GameExecutable.Length > 0)
                {
                    var fixedPath = Path.Combine(InstallPath, GameExecutable);
                    return File.Exists(fixedPath) ? fixedPath : null;
                }

                foreach (var name in KnownGameExecutables)
                {
                    var p = Path.Combine(InstallPath, name);
                    if (File.Exists(p)) return p;
                }

                return Directory.EnumerateFiles(InstallPath, "*.exe", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(f => !Path.GetFileName(f).StartsWith("unins", StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return null;
            }
        }
    }

    [JsonIgnore]
    public bool IsClientInstalled =>
        GameExePath != null && !File.Exists(Path.Combine(InstallPath, ExtractingMarker));

    public static string DataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DARKSIDE");

    public static string DefaultInstallPath => Path.Combine(DataDir, "Client");

    private static string FilePath => Path.Combine(DataDir, "settings.json");

    public static LauncherSettings Load()
    {
        var s = new LauncherSettings();
        try
        {
            if (File.Exists(FilePath))
                s = JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(FilePath)) ?? s;
        }
        catch { }

        if (string.IsNullOrWhiteSpace(s.InstallPath))
            s.InstallPath = DefaultInstallPath;
        return s;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(FilePath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
