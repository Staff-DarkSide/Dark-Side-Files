using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace DarkSideLauncher;

public sealed record UpdateInfo(Version Version, string DownloadUrl);

public static class Updater
{
    public static Version CurrentVersion =>
        Normalize(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0));

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct)
    {
        var url = $"https://api.github.com/repos/{LauncherSettings.RepoOwner}/{LauncherSettings.RepoName}/releases?per_page=30";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var resp = await Web.Client.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        UpdateInfo? best = null;

        foreach (var rel in doc.RootElement.EnumerateArray())
        {
            if (rel.GetProperty("draft").GetBoolean() || rel.GetProperty("prerelease").GetBoolean()) continue;

            var tag = rel.GetProperty("tag_name").GetString() ?? "";
            var prefix = LauncherSettings.LauncherTagPrefix;
            if (!tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (!Version.TryParse(tag[prefix.Length..], out var parsed)) continue;

            var remote = Normalize(parsed);
            if (remote <= CurrentVersion) continue;
            if (best != null && remote <= best.Version) continue;

            foreach (var asset in rel.GetProperty("assets").EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() == LauncherSettings.InstallerAssetName)
                {
                    best = new UpdateInfo(remote, asset.GetProperty("browser_download_url").GetString()!);
                    break;
                }
            }
        }
        return best;
    }

    public static async Task DownloadAndRunAsync(
        UpdateInfo info, IProgress<InstallProgress>? progress, CancellationToken ct)
    {
        var file = Path.Combine(Path.GetTempPath(), $"DARKSIDE-Setup-{info.Version}.exe");

        using (var resp = await Web.Client.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? 0;

            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(file);

            var buf = new byte[81920];
            long done = 0, last = 0;
            int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                done += n;

                var now = Environment.TickCount64;
                if (now - last > 100)
                {
                    last = now;
                    progress?.Report(new InstallProgress(
                        total > 0 ? done * 100.0 / total : 0, "Downloading launcher update..."));
                }
            }
        }

        Process.Start(new ProcessStartInfo(file, "/SILENT /NORESTART") { UseShellExecute = true });
    }
}
