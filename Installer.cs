using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SharpCompress.Archives.Rar;
using SharpCompress.Readers;

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
    // ================= STEP 1: BASE GAME (download .rar + extract) =================

    public static async Task InstallBaseGameAsync(
        string installPath, IProgress<InstallProgress>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(installPath);
        var rar = Path.Combine(installPath, LauncherSettings.GameArchiveName);
        var part = rar + ".part";

        if (!File.Exists(rar))
        {
            await DownloadWithResumeAsync(LauncherSettings.GameArchiveUrl, part, "Downloading game", progress, ct);
            File.Move(part, rar, true);
        }

        var marker = Path.Combine(installPath, LauncherSettings.ExtractingMarker);
        File.WriteAllText(marker, "");

        try
        {
            await Task.Run(() => Extract(rar, installPath, progress, ct), ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Corrupted archive: remove it so the next attempt downloads it again.
            try { File.Delete(rar); } catch { }
            throw;
        }

        File.Delete(rar);
        File.Delete(marker);
    }

    private static async Task DownloadWithResumeAsync(
        string url, string part, string label, IProgress<InstallProgress>? progress, CancellationToken ct)
    {
        long existing = File.Exists(part) ? new FileInfo(part).Length : 0;

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (existing > 0) req.Headers.Range = new RangeHeaderValue(existing, null);
        using var resp = await Web.Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

        if (resp.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable) return; // already complete
        resp.EnsureSuccessStatusCode();

        bool append = resp.StatusCode == HttpStatusCode.PartialContent;
        long done = append ? existing : 0;
        long total = append
            ? resp.Content.Headers.ContentRange?.Length ?? 0
            : resp.Content.Headers.ContentLength ?? 0;

        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = new FileStream(part, append ? FileMode.Append : FileMode.Create,
                                             FileAccess.Write, FileShare.None);

        var buf = new byte[81920];
        long last = 0;
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
                    total > 0 ? done * 100.0 / total : 0,
                    $"{label}... {done / 1048576} / {total / 1048576} MB"));
            }
        }
    }

    private static void Extract(string rarPath, string root, IProgress<InstallProgress>? progress, CancellationToken ct)
    {
        // Scan headers first: sizes for progress + detect a single top-level folder to strip.
        List<(string Key, long Size)> list;
        using (var archive = RarArchive.Open(rarPath))
        {
            list = archive.Entries
                .Where(e => !e.IsDirectory)
                .Select(e => (Key: Norm(e.Key), Size: e.Size))
                .ToList();
        }

        string strip = "";
        if (list.Count > 0 && list.All(x => x.Key.Contains('/')))
        {
            var first = list[0].Key.Split('/')[0];
            if (list.All(x => x.Key.StartsWith(first + "/", StringComparison.OrdinalIgnoreCase)))
                strip = first + "/";
        }

        long total = list.Sum(x => x.Size);
        long done = 0, last = 0;

        using var fs = File.OpenRead(rarPath);
        using var reader = ReaderFactory.Open(fs);

        while (reader.MoveToNextEntry())
        {
            ct.ThrowIfCancellationRequested();
            if (reader.Entry.IsDirectory) continue;

            var rel = Norm(reader.Entry.Key);
            if (strip.Length > 0 && rel.StartsWith(strip, StringComparison.OrdinalIgnoreCase))
                rel = rel[strip.Length..];
            if (rel.Length == 0) continue;

            var target = SafePath(root, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            using var src = reader.OpenEntryStream();
            using var dst = File.Create(target);

            var buf = new byte[81920];
            int n;
            while ((n = src.Read(buf, 0, buf.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                dst.Write(buf, 0, n);
                done += n;

                var now = Environment.TickCount64;
                if (now - last > 100)
                {
                    last = now;
                    var pct = total > 0 ? done * 100.0 / total : 0;
                    progress?.Report(new InstallProgress(pct, $"Extracting game... {pct:0}%"));
                }
            }
        }
    }

    private static string Norm(string? key) => (key ?? "").Replace('\\', '/').TrimStart('/');

    // ================= STEP 2: UPDATES (repo folder applied over the game) =================

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

        return list; // empty = no updates published yet
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
                $"Checking updates... {i + 1}/{remote.Count}"));
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
                            $"Downloading updates {index}/{files.Count}: {Path.GetFileName(f.RelativePath)}"));
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
