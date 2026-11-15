using System;
using System.IO;
using System.Text.Json;

namespace DarkSide.Launcher
{
    public class LauncherSettings
    {
        // Ruta completa al ejecutable del cliente (ClassicUO.exe, client.exe, etc.)
        public string ClientPath { get; set; } = "";

        // Argumentos opcionales al lanzar el cliente
        public string Arguments { get; set; } = "";

        // URL del manifest.json para actualizaciones. Vacio = sin actualizaciones.
        // Si el archivo aun no existe en GitHub, el launcher lo ignora y deja jugar.
        public string ManifestUrl { get; set; } =
            "https://raw.githubusercontent.com/Staff-DarkSide/Dark-Side-Files/main/updates/manifest.json";

        // Cerrar el launcher al pulsar JUGAR
        public bool CloseOnPlay { get; set; } = true;

        private static string Dir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DarkSide");
        private static string FilePath => Path.Combine(Dir, "launcher.json");

        public static LauncherSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(FilePath)) ?? new LauncherSettings();
            }
            catch { }
            return new LauncherSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
    }
}
