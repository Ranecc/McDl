using System;
using System.Drawing;
using System.Windows.Forms;
using McDl.Core;

namespace McDl.App
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.ThreadException += (sender, e) =>
                ShowFatal(e.Exception);

            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
                ShowFatal(e.ExceptionObject as Exception);

            Application.Run(new MainForm(args));
        }

        private static void ShowFatal(Exception ex)
        {
            if (ex == null) return;
            MessageBox.Show(ex.ToString(), "McDl 发生错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Palette and helpers shared by the window; one place to restyle the app.</summary>
    internal static class Theme
    {
        public static readonly Color Background = Color.FromArgb(246, 247, 249);
        public static readonly Color Surface = Color.White;
        public static readonly Color SurfaceAlt = Color.FromArgb(239, 242, 245);
        public static readonly Color Border = Color.FromArgb(221, 226, 232);
        public static readonly Color Text = Color.FromArgb(28, 33, 40);
        public static readonly Color TextMuted = Color.FromArgb(108, 118, 130);
        public static readonly Color Accent = Color.FromArgb(37, 156, 90);
        public static readonly Color AccentHover = Color.FromArgb(48, 176, 103);
        public static readonly Color AccentPressed = Color.FromArgb(28, 130, 74);
        public static readonly Color AccentSoft = Color.FromArgb(232, 245, 237);

        public static Font Ui(float size, FontStyle style = FontStyle.Regular)
        {
            return new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Point);
        }

        public static Font Mono(float size)
        {
            return new Font("Consolas", size, FontStyle.Regular, GraphicsUnit.Point);
        }

        public static string Bytes(long value)
        {
            if (value < 1024) return value + " B";
            if (value < 1024L * 1024) return (value / 1024.0).ToString("0.0") + " KB";
            if (value < 1024L * 1024 * 1024) return (value / 1024.0 / 1024).ToString("0.0") + " MB";
            return (value / 1024.0 / 1024 / 1024).ToString("0.00") + " GB";
        }

        public static string VersionType(string type)
        {
            switch ((type ?? string.Empty).ToLowerInvariant())
            {
                case "release": return "正式版";
                case "snapshot": return "快照";
                case "old_beta": return "远古 Beta";
                case "old_alpha": return "远古 Alpha";
                default: return type ?? "未知";
            }
        }

        public static string Date(string iso)
        {
            DateTime parsed;
            if (DateTime.TryParse(iso, out parsed)) return parsed.ToString("yyyy-MM-dd");
            return iso ?? string.Empty;
        }

        public static string MirrorName(string name)
        {
            switch ((name ?? string.Empty).ToLowerInvariant())
            {
                case "official": return "官方";
                case "bmclapi": return "BMCLAPI";
                default: return name ?? string.Empty;
            }
        }

        /// <summary>Assembly version trimmed to major.minor.patch, for the footer label.</summary>
        public static string ShortVersion()
        {
            var version = typeof(Theme).Assembly.GetName().Version;
            if (version == null) return "0.0";
            return version.Revision > 0
                ? version.ToString()
                : version.Major + "." + version.Minor + "." + System.Math.Max(0, version.Build);
        }
    }
}
