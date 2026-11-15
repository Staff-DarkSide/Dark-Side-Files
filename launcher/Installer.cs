using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace DarkSide.Launcher
{
    public static class Installer
    {
        public const string RarUrl =
            "https://github.com/Staff-DarkSide/Dark-Side-Files/releases/download/v1.0/Dark.Side.UO.rar";

        // .rar (0.76 GB) + cliente descomprimido (2.6 GB) + margen
        public const long RequiredBytes = 3_800_000_000L;

        public static async Task<string> InstallAsync(string destRoot, IProgress<(double, string)> progress)
        {
            Directory.CreateDirectory(destRoot);

            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(destRoot)));
                if (drive.AvailableFreeSpace < RequiredBytes)
                    throw new IOException("Not enough disk space: about 4 GB free is needed on " + drive.Name);
            }
            catch (ArgumentException) { }

            string rarPath = Path.Combine(destRoot, "Dark.Side.UO.rar");
            string part = rarPath + ".part";

            // ---- 1) Descarga (0 - 50 %) ----
            using (var http = new HttpClient { Timeout = TimeSpan.FromHours(2) })
            using (var resp = await http.GetAsync(RarUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                long total = resp.Content.Headers.ContentLength ?? -1;
                using var src = await resp.Content.ReadAsStreamAsync();
                using var dst = File.Create(part);
                var buffer = new byte[1 << 20];
                long done = 0;
                int read;
                int lastPct = -1;
                while ((read = await src.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await dst.WriteAsync(buffer, 0, read);
                    done += read;
                    if (total > 0)
                    {
                        int pct = (int)(done * 100 / total);
                        if (pct != lastPct)
                        {
                            lastPct = pct;
                            progress.Report((pct * 0.5,
                                $"Downloading client... {done / 1048576} / {total / 1048576} MB"));
                        }
                    }
                }
            }
            if (File.Exists(rarPath)) File.Delete(rarPath);
            File.Move(part, rarPath);

            // ---- 2) Descompresion (50 - 100 %) ----
            await Task.Run(() =>
            {
                using var archive = ArchiveFactory.Open(rarPath);
                var entries = archive.Entries.Where(e => !e.IsDirectory).ToList();
                var options = new ExtractionOptions { ExtractFullPath = true, Overwrite = true };
                int i = 0;
                int lastPct = -1;
                foreach (var entry in entries)
                {
                    entry.WriteToDirectory(destRoot, options);
                    i++;
                    int pct = i * 100 / entries.Count;
                    if (pct != lastPct)
                    {
                        lastPct = pct;
                        progress.Report((50 + pct * 0.5, $"Installing files... {i} / {entries.Count}"));
                    }
                }
            });

            try { File.Delete(rarPath); } catch { }

            progress.Report((100, "Installation complete"));
            return FindClient(destRoot);
        }

        public static string FindClient(string root)
        {
            return Directory.EnumerateFiles(root, "client.exe", SearchOption.AllDirectories).FirstOrDefault();
        }
    }
}
