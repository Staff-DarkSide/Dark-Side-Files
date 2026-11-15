using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace DarkSide.Launcher
{
    public class Manifest
    {
        public string Version { get; set; }
        public List<ManifestFile> Files { get; set; }
    }

    public class ManifestFile
    {
        public string Path { get; set; }    // ruta relativa a la carpeta del cliente
        public string Url { get; set; }     // de donde descargarlo
        public string Sha256 { get; set; }  // hash para saber si hay que actualizar
    }

    public static class Updater
    {
        public static async Task UpdateAsync(string manifestUrl, string root, IProgress<(double, string)> progress)
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };

            progress.Report((0, "Checking for updates..."));
            string json = await http.GetStringAsync(manifestUrl);
            var manifest = JsonSerializer.Deserialize<Manifest>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (manifest?.Files == null)
                throw new InvalidDataException("Invalid manifest.");

            string rootFull = System.IO.Path.GetFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar)
                              + System.IO.Path.DirectorySeparatorChar;

            // 1) Comprobar que archivos hay que descargar
            var pending = new List<(ManifestFile file, string target)>();
            await Task.Run(() =>
            {
                foreach (var f in manifest.Files)
                {
                    string target = System.IO.Path.GetFullPath(System.IO.Path.Combine(rootFull, f.Path));
                    if (!target.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Path not allowed in manifest: " + f.Path);

                    if (!File.Exists(target) || !HashMatches(target, f.Sha256))
                        pending.Add((f, target));
                }
            });

            if (pending.Count == 0) return;

            // 2) Descargar
            for (int i = 0; i < pending.Count; i++)
            {
                var (file, target) = pending[i];
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));
                string temp = target + ".part";

                using (var resp = await http.GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead))
                {
                    resp.EnsureSuccessStatusCode();
                    long total = resp.Content.Headers.ContentLength ?? -1;
                    using var src = await resp.Content.ReadAsStreamAsync();
                    using var dst = File.Create(temp);
                    var buffer = new byte[81920];
                    long done = 0;
                    int read;
                    while ((read = await src.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await dst.WriteAsync(buffer, 0, read);
                        done += read;
                        double fraction = total > 0 ? (double)done / total : 0;
                        progress.Report(((i + fraction) / pending.Count * 100.0,
                            $"Downloading {file.Path} ({i + 1}/{pending.Count})"));
                    }
                }

                if (!string.IsNullOrWhiteSpace(file.Sha256) && !HashMatches(temp, file.Sha256))
                {
                    File.Delete(temp);
                    throw new InvalidDataException("Wrong hash: " + file.Path);
                }

                if (File.Exists(target)) File.Delete(target);
                File.Move(temp, target);
            }

            progress.Report((100, "Update complete"));
        }

        private static bool HashMatches(string path, string expected)
        {
            if (string.IsNullOrWhiteSpace(expected)) return true;
            using var sha = SHA256.Create();
            using var fs = File.OpenRead(path);
            string actual = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "");
            return string.Equals(actual, expected.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
