using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DarkSideLauncher;

public sealed record InstallProgress(double Percent, string Message);
public sealed record RemoteFile(string RelativePath, string Sha, long Size);

internal static class Web
{
    public static readonly HttpClient Client = Create();

    private static HttpClient Create()
    {
        var c = new HttpClient();
        c.DefaultRequestHeaders.UserAgent.ParseAdd("DarkSideLauncher/1.0");
        return c;
    }
}

public static class Installer
{
    public static async Task<List<RemoteFile>> GetRemoteFilesAsync(CancellationToken ct)
    {
        var url = $"https://api.github.com/repos/{LauncherSettings.RepoOwner}/{LauncherSettings.RepoName}" +
                  $"/git/trees/{LauncherSettings.Branch}?recursive=1";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var resp = await Web.Client.SendAsync(req, ct);

        if (resp.StatusCode == HttpStatusCode.Forbidden)
            throw new InvalidOperationException("GitHub rate limit reached. Try again in a few minutes.");
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        if (root.TryGetProperty("truncated", out var tr) && tr.GetBoolean())
            throw new InvalidOperationException("Repository tree too large.");

        var sub = LauncherSettings.ClientSubfolder.Trim('/');
        var prefix = sub.Length == 0 ? "" : sub + "/";
        var list = new List<RemoteFile>();

        foreach (var item in root.GetProperty("tree").EnumerateArray())
        {
            if (item.GetProperty("type").GetString() != "blob") continue;
            var path = item.GetProperty("path").GetString()!;
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

            var rel = path[prefix.Length..];
            var name = Path.GetFileName(rel);
            if (name is ".gitkeep" or ".gitattributes") continue;

            list.Add(new RemoteFile(rel, item.GetProperty("sha").GetString()!, item.GetProperty("size").GetInt64()));
        }

        if (list.Count == 0)
            throw new InvalidOperationException($"No files found in '{LauncherSettings.ClientSubfolder}' of the repository.");

        return list;
    }

    public static async Task<List<RemoteFile>> FindOutdatedAsync(
        string installPath, IProgress<InstallProgress>? progress, CancellationToken ct)
    {
        var remote = await GetRemoteFilesAsync(ct);
        var outdated = new List<RemoteFile>();

        for (int i = 0; i < remote.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var f = remote[i];
            var local = SafePath(installPath, f.RelativePath);

            bool ok = File.Exists(local)
                      && new FileInfo(local).Length == f.Size
                      && await Task.Run(() => GitBlobSha(local, ct), ct) == f.Sha;

            if (!ok) outdated.Add(f);
            progress?.Report(new InstallProgress((i + 1) * 100.0 / remote.Count,
                $"Verifying files... {i + 1}/{remote.Count}"));
        }
        return outdated;
    }

    public static async Task DownloadAsync(
        string installPath, List<RemoteFile> files, IProgress<InstallProgress>? progress, CancellationToken ct)
    {
        long total = files.Sum(f => f.Size);
        long done = 0;
        long lastReport = 0;
        int index = 0;

        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            index++;

            var target = SafePath(installPath, f.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var tmp = target + ".part";

            using (var resp = await Web.Client.GetAsync(RawUrl(f.RelativePath),
                       HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                await using var src = await resp.Content.ReadAsStreamAsync(ct);
                await using var dst = File.Create(tmp);

                var buf = new byte[81920];
                int n;
                while ((n = await src.ReadAsync(buf, ct)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct);
                    done += n;

                    var now = Environment.TickCount64;
                    if (now - lastReport > 100)
                    {
                        lastReport = now;
                        progress?.Report(new InstallProgress(
                            total > 0 ? done * 100.0 / total : 100,
                            $"Downloading {index}/{files.Count}: {Path.GetFileName(f.RelativePath)}"));
                    }
                }
            }

            if (GitBlobSha(tmp, ct) != f.Sha)
            {
                File.Delete(tmp);
                throw new InvalidDataException($"Corrupted download: {f.RelativePath}");
            }
            File.Move(tmp, target, true);
        }

        progress?.Report(new InstallProgress(100, "Done."));
    }

    private static string RawUrl(string relativePath)
    {
        var sub = LauncherSettings.ClientSubfolder.Trim('/');
        var full = sub.Length == 0 ? relativePath : sub + "/" + relativePath;
        var escaped = string.Join("/", full.Split('/').Select(Uri.EscapeDataString));
        return $"https://raw.githubusercontent.com/{LauncherSettings.RepoOwner}/{LauncherSettings.RepoName}/{LauncherSettings.Branch}/{escaped}";
    }

    private static string SafePath(string root, string relative)
    {
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid path: " + relative);
        return full;
    }

    // Same hash Git uses for blobs, so local files can be compared with the GitHub tree.
    private static string GitBlobSha(string file, CancellationToken ct)
    {
        using var fs = File.OpenRead(file);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        sha.AppendData(Encoding.ASCII.GetBytes($"blob {fs.Length}\0"));

        var buf = new byte[81920];
        int n;
        while ((n = fs.Read(buf, 0, buf.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            sha.AppendData(buf, 0, n);
        }
        return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
    }
}
