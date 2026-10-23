using System;
using System.IO;
using McDl.App.Design;
using McDl.Core;

namespace McDl.App
{
    /// <summary>Small user-preferences blob stored under %APPDATA%\McDl.</summary>
    public sealed class Settings
    {
        public string Root { get; set; }
        public int Threads { get; set; }
        public bool WithAssets { get; set; }

        /// <summary>配色模式；旧配置文件没有这一项时按「跟随系统」处理。</summary>
        public ThemeMode ThemeMode { get; set; }

        private static string FilePath
        {
            get
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "McDl");
                return Path.Combine(dir, "settings.json");
            }
        }

        /// <summary>全新用户的默认值：%APPDATA%\.minecraft、8 线程、下 assets、跟随系统配色。</summary>
        public static Settings Defaults()
        {
            return new Settings
            {
                Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft"),
                Threads = 8,
                WithAssets = true,
                ThemeMode = ThemeMode.System
            };
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

            var result = loaded ?? Defaults();
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


