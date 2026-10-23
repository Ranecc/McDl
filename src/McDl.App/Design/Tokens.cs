using System;
using System.Drawing;

namespace McDl.App.Design
{
    /// <summary>用户可选的配色模式；System 表示跟随 Windows 的应用主题设置。</summary>
    public enum ThemeMode
    {
        System,
        Light,
        Dark
    }

    /// <summary>
    /// 一套配色。名字对应"角色"而不是具体色号，控件只认角色，
    /// 换主题时只换这一层，不必改绘制代码。
    /// </summary>
    public sealed class Palette
    {
        public bool IsDark;

        public Color Backdrop;
        public Color BackdropTop;
        public Color BackdropBottom;
        public Color Surface;
        public Color SurfaceAlt;
        public Color Border;
        public Color BorderStrong;
        public Color Text;
        public Color TextMuted;
        public Color Accent;
        public Color AccentHover;
        public Color AccentPressed;
        public Color AccentSoft;

        /// <summary>落在 Accent 实心块上的文字/图标颜色。</summary>
        public Color OnAccent;
        public Color Track;
        public Color Danger;

        /// <summary>投影颜色（含透明度），在浅色下很淡，深色下更深但更短。</summary>
        public Color Shadow;

        /// <summary>悬停发光颜色（含透明度）。</summary>
        public Color Glow;

        public static readonly Palette Light = new Palette
        {
            IsDark = false,
            Backdrop = Color.FromArgb(237, 241, 244),
            BackdropTop = Color.FromArgb(245, 248, 251),
            BackdropBottom = Color.FromArgb(226, 233, 240),
            Surface = Color.FromArgb(255, 255, 255),
            SurfaceAlt = Color.FromArgb(239, 242, 245),
            Border = Color.FromArgb(201, 211, 220),
            BorderStrong = Color.FromArgb(180, 192, 202),
            Text = Color.FromArgb(20, 25, 31),
            TextMuted = Color.FromArgb(91, 104, 117),
            Accent = Color.FromArgb(37, 121, 79),
            AccentHover = Color.FromArgb(42, 133, 88),
            AccentPressed = Color.FromArgb(31, 107, 70),
            AccentSoft = Color.FromArgb(231, 244, 236),
            OnAccent = Color.FromArgb(255, 255, 255),
            Track = Color.FromArgb(213, 222, 230),
            Danger = Color.FromArgb(196, 74, 66),
            Shadow = Color.FromArgb(40, 15, 23, 32),
            Glow = Color.FromArgb(46, 70, 175, 120)
        };

        public static readonly Palette Dark = new Palette
        {
            IsDark = true,
            Backdrop = Color.FromArgb(11, 15, 19),
            BackdropTop = Color.FromArgb(17, 22, 27),
            BackdropBottom = Color.FromArgb(8, 11, 14),
            Surface = Color.FromArgb(23, 28, 33),
            SurfaceAlt = Color.FromArgb(30, 36, 42),
            Border = Color.FromArgb(70, 82, 94),
            BorderStrong = Color.FromArgb(90, 104, 117),
            Text = Color.FromArgb(230, 236, 242),
            TextMuted = Color.FromArgb(149, 162, 174),
            Accent = Color.FromArgb(79, 190, 134),
            AccentHover = Color.FromArgb(95, 208, 150),
            AccentPressed = Color.FromArgb(63, 160, 112),
            AccentSoft = Color.FromArgb(27, 44, 34),
            OnAccent = Color.FromArgb(15, 19, 23),
            Track = Color.FromArgb(56, 67, 78),
            Danger = Color.FromArgb(226, 110, 100),
            Shadow = Color.FromArgb(120, 0, 0, 0),
            Glow = Color.FromArgb(70, 79, 190, 134)
        };

        public static Palette For(ThemeMode mode, bool systemPrefersDark)
        {
            switch (mode)
            {
                case ThemeMode.Dark: return Dark;
                case ThemeMode.Light: return Light;
                default: return systemPrefersDark ? Dark : Light;
            }
        }
    }

    /// <summary>尺寸与字体：所有控件的取值都从这里来，保证间距节奏统一。</summary>
    /// <remarks>
    /// 布局写的是 96 DPI 下的设计像素，实际像素一律经过 Px 换算，
    /// 这样 125% / 150% 缩放下间距与字号等比放大，不会出现“字变大了但格子没变”的挤压。
    /// </remarks>
    public static class Metrics
    {
        private static float _scale = 1f;
        private static Font _body, _bodyStrong, _small, _tiny, _title, _section, _hero, _button, _monoSmall;

        /// <summary>缩放系数（1 = 96 DPI）。启动时按窗口 DPI 设置，设置后字体缓存会重建。</summary>
        public static float Scale
        {
            get { return _scale; }
            set
            {
                var next = value <= 0 ? 1f : value;
                if (Math.Abs(next - _scale) < 0.01f) return;
                _scale = next;
                DisposeFonts();
            }
        }

        /// <summary>把设计像素换成实际像素。</summary>
        public static int Px(float value)
        {
            return (int)Math.Round(value * _scale);
        }

        public static float Pxf(float value)
        {
            return (float)Math.Round(value * _scale, 2);
        }

        public static int RadiusCard { get { return Px(10); } }
        public static int RadiusControl { get { return Px(8); } }
        public static int RadiusSmall { get { return Px(6); } }

        public static int PagePad { get { return Px(24); } }
        public static int Gap { get { return Px(16); } }
        public static int PadCard { get { return Px(24); } }
        public static int PadControl { get { return Px(14); } }

        public static int ButtonHeight { get { return Px(40); } }
        public static int CompactButtonHeight { get { return Px(32); } }
        public static int CompactButtonWidth { get { return Px(84); } }
        public static int InputHeight { get { return Px(36); } }
        public static int RowHeight { get { return Px(56); } }
        public static int TitleBarHeight { get { return Px(48); } }
        public static int NavWidth { get { return Px(176); } }

        public static Font Ui(float pixels, FontStyle style = FontStyle.Regular)
        {
            return new Font("Microsoft YaHei UI", pixels * _scale, style, GraphicsUnit.Pixel);
        }

        public static Font Mono(float pixels, FontStyle style = FontStyle.Regular)
        {
            return new Font("Consolas", pixels * _scale, style, GraphicsUnit.Pixel);
        }

        public static Font Body { get { return _body ?? (_body = Ui(13f)); } }
        public static Font BodyStrong { get { return _bodyStrong ?? (_bodyStrong = Ui(13f, FontStyle.Bold)); } }
        public static Font Small { get { return _small ?? (_small = Ui(12f)); } }
        public static Font Tiny { get { return _tiny ?? (_tiny = Ui(11f)); } }
        public static Font Title { get { return _title ?? (_title = Ui(21f, FontStyle.Bold)); } }
        public static Font Section { get { return _section ?? (_section = Ui(15f, FontStyle.Bold)); } }
        public static Font Hero { get { return _hero ?? (_hero = Ui(26f, FontStyle.Bold)); } }
        public static Font Button { get { return _button ?? (_button = Ui(14f, FontStyle.Bold)); } }
        public static Font MonoSmall { get { return _monoSmall ?? (_monoSmall = Mono(13f)); } }

        private static void DisposeFonts()
        {
            foreach (var font in new[] { _body, _bodyStrong, _small, _tiny, _title, _section, _hero, _button, _monoSmall })
            {
                if (font != null) font.Dispose();
            }

            _body = _bodyStrong = _small = _tiny = _title = _section = _hero = _button = _monoSmall = null;
        }
    }
}


