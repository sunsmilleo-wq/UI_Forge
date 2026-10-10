using System;
using System.IO;

namespace UiForge.App
{
    /// <summary>Мини-настройки: пути последних открытых архивов (две строки в файле в AppData).</summary>
    public static class AppSettings
    {
        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "UiForge.App", "settings.txt");

        public static (string? InterfacePath, string? TexturePath) Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return (null, null);
                var lines = File.ReadAllLines(FilePath);
                return (lines.Length > 0 ? lines[0] : null, lines.Length > 1 ? lines[1] : null);
            }
            catch
            {
                return (null, null);
            }
        }

        public static void Save(string? interfacePath, string? texturePath)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllLines(FilePath, new[] { interfacePath ?? "", texturePath ?? "" });
            }
            catch
            {
                // не критично — просто не запомним пути
            }
        }
    }
}
