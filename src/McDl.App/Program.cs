using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using McDl.App.Design;
using McDl.App.Shell;

namespace McDl.App
{
    internal static class Program
    {
        /// <summary>界面里展示的版本号，取自程序集元数据（构建时由 csproj 写入）。</summary>
        public static string Version
        {
            get
            {
                var version = typeof(Program).Assembly.GetName().Version;
                if (version == null) return "v0.0.0";
                if (version.Revision > 0) return "v" + version.ToString(4);
                return "v" + version.ToString(3);
            }
        }

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.ThreadException += (sender, e) => ShowFatal(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (sender, e) => ShowFatal(e.ExceptionObject as Exception);

            ApplyUiScale();

            var settings = Settings.Load();
            ThemeManager.Initialize(settings.ThemeMode);

            Application.Run(new ShellWindow(settings, FirstPackArgument(args)));
        }

        /// <summary>
        /// 按系统 DPI 设定界面缩放系数。manifest 里声明了 dpiAware，窗口不会再被位图拉伸，
        /// 所以字号与间距由 Metrics 统一乘上 DPI 比例。
        /// MCDL_UI_SCALE 可强制指定比例（调试高 DPI 用，例如 1.25）。
        /// </summary>
        private static void ApplyUiScale()
        {
            var forced = Environment.GetEnvironmentVariable("MCDL_UI_SCALE");
            float parsed;
            if (!string.IsNullOrWhiteSpace(forced) &&
                float.TryParse(forced, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out parsed) &&
                parsed >= 1f && parsed <= 4f)
            {
                Metrics.Scale = parsed;
                return;
            }

            try
            {
                using (var screen = Graphics.FromHwnd(IntPtr.Zero))
                {
                    Metrics.Scale = screen.DpiX / 96f;
                }
            }
            catch
            {
                Metrics.Scale = 1f;
            }
        }

        /// <summary>命令行里第一个存在的文件参数，按整合包处理。</summary>
        private static string FirstPackArgument(string[] args)
        {
            if (args == null) return null;

            foreach (var arg in args)
            {
                if (string.IsNullOrWhiteSpace(arg)) continue;
                if (!File.Exists(arg)) continue;
                return arg;
            }

            return null;
        }

        private static void ShowFatal(Exception exception)
        {
            if (exception == null) return;

            try
            {
                MessageBox.Show(
                    exception.Message + Environment.NewLine + Environment.NewLine + exception.GetType().FullName,
                    "McDl 出错了",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch
            {
                // 连弹窗都失败时只能退出，交给系统日志。
            }
        }
    }
}
