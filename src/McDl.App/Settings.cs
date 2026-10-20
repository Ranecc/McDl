using System;
using System.IO;
using McDl.Core;

namespace McDl.App
{
    /// <summary>Small user-preferences blob stored under %APPDATA%\McDl.</summary>
    internal sealed class Settings
    {
        public string Root { get; set; }
        public int Threads { get; set; }
        public bool WithAssets { get; set; }

        private static string FilePath
        {
            get
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "McDl");
                return Path.Combine(dir, "settings.json");
            }
        }

        public static Settings Load()
        {
            Settings loaded = null;
            try
            {
                loaded = Json.Load<Settings>(FilePath);
            }
            catch (Exception)
            {
                // A broken settings file must never block startup.
            }

            var result = loaded ?? new Settings();
            if (string.IsNullOrWhiteSpace(result.Root))
                result.Root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
            if (result.Threads < 1 || result.Threads > 64) result.Threads = 8;
            return result;
        }

        public void Save()
        {
            try
            {
                Json.Save(FilePath, this);
            }
            catch (Exception)
            {
                // Preferences are best-effort.
            }
        }
    }
}
