using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace McDl.App.Design
{
    /// <summary>窗口级别的系统效果：圆角、深色标题栏、亚克力背景。</summary>
    public static class Backdrop
    {
        public static bool CompositionEnabled { get; private set; }

        /// <summary>云母背板是否生效（Win11 22H2+）。生效时客户区可以留出半透明，让桌面透上来。</summary>
        public static bool MicaApplied { get; private set; }
        public static bool RoundedCornersSupported { get; private set; }
        public static bool DarkModeSupported { get; private set; }
        public static bool AcrylicSupported { get; private set; }

        private const int DwmwaUseImmersiveDarkMode = 20;
        private const int DwmwaUseImmersiveDarkModeLegacy = 19;
        private const int DwmwaWindowCornerPreference = 33;
        private const int DwmwcpRound = 2;

        private const int WcaAccentPolicy = 19;
        private const int AccentEnableAcrylicBlurBehind = 4;
        private const int AccentEnableBlurBehind = 3;

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public int AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [DllImport("dwmapi.dll")]
        private static extern int DwmIsCompositionEnabled(out bool enabled);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        /// <summary>
        /// 给窗口挂上系统效果。每一步都独立失败：某个不支持就退回纯色绘制，
        /// 界面其余部分不需要知道。
        /// </summary>
        public static void Attach(IntPtr handle, bool dark, Color tint)
        {
            if (handle == IntPtr.Zero) return;

            try
            {
                bool enabled;
                CompositionEnabled = DwmIsCompositionEnabled(out enabled) == 0 && enabled;
            }
            catch
            {
                CompositionEnabled = false;
            }

            if (!CompositionEnabled) return;

            RoundedCornersSupported = TrySetCornerPreference(handle);
            DarkModeSupported = TrySetDarkMode(handle, dark);

            // 云母先开：Win11 22H2+ 上只要客户区不是整片不透明，DWM 就会把桌面模糊垫在底下。
            MicaApplied = TrySetMica(handle);

            // 系统不认云母（Win10 / 老版本 11）时才退回旧的 Aero 模糊 + 半透明着色。
            // 亚克力（ACCENT_ENABLE_ACRYLICBLURBEHIND）在这里是关的：它会在客户区之上再糊一层，
            // 实测浅色主题文字会被冲白，所以只用比它轻的 blur-behind，靠自己的半透明叠色控制观感。
            AcrylicSupported = false;
            if (!MicaApplied)
            {
                AcrylicSupported = TrySetBlurBehind(handle, tint);
            }
        }

        public static void UpdateTint(IntPtr handle, bool dark, Color tint)
        {
            if (handle == IntPtr.Zero || !CompositionEnabled) return;
            TrySetDarkMode(handle, dark);
        }

        private static bool TrySetCornerPreference(IntPtr handle)
        {
            try
            {
                var pref = DwmwcpRound;
                return DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref pref, sizeof(int)) == 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool TrySetDarkMode(IntPtr handle, bool dark)
        {
            try
            {
                var value = dark ? 1 : 0;
                if (DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref value, sizeof(int)) == 0) return true;
                return DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeLegacy, ref value, sizeof(int)) == 0;
            }
            catch
            {
                return false;
            }
        }

        private const int DwmwaSystemBackdropType = 38;

        private static bool TrySetMica(IntPtr handle)
        {
            try
            {
                var type = 2; // DWMSBT_MAINWINDOW
                return DwmSetWindowAttribute(handle, DwmwaSystemBackdropType, ref type, sizeof(int)) == 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>旧式 blur-behind（Win7 起的 Aero 模糊）：只模糊、不额外糊一层色。</summary>
        private static bool TrySetBlurBehind(IntPtr handle, Color tint)
        {
            return SetAccent(handle, AccentEnableBlurBehind, tint);
        }

        /// <summary>亚克力：模糊之上再加一层用户指定的着色。MCDL_GLASS=acrylic 可试。</summary>
        public static bool TrySetAcrylicGlass(IntPtr handle, Color tint)
        {
            return SetAccent(handle, AccentEnableAcrylicBlurBehind, tint);
        }

        private static bool SetAccent(IntPtr handle, int state, Color tint)
        {
            var data = IntPtr.Zero;
            try
            {
                var policy = new AccentPolicy
                {
                    AccentState = state,
                    AccentFlags = 2,
                    // GradientColor 的字节序是 AABBGGRR。
                    GradientColor = (tint.A << 24) | (tint.B << 16) | (tint.G << 8) | tint.R
                };

                var size = Marshal.SizeOf(typeof(AccentPolicy));
                data = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(policy, data, false);

                var payload = new WindowCompositionAttributeData
                {
                    Attribute = WcaAccentPolicy,
                    Data = data,
                    SizeOfData = size
                };

                return SetWindowCompositionAttribute(handle, ref payload) != 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (data != IntPtr.Zero) Marshal.FreeHGlobal(data);
            }
        }

        private static bool TrySetAcrylic(IntPtr handle, Color tint)
        {
            var data = IntPtr.Zero;
            try
            {
                var policy = new AccentPolicy
                {
                    AccentState = AccentEnableAcrylicBlurBehind,
                    AccentFlags = 2,
                    // GradientColor 的字节序是 AABBGGRR。
                    GradientColor = (tint.A << 24) | (tint.B << 16) | (tint.G << 8) | tint.R
                };

                var size = Marshal.SizeOf(typeof(AccentPolicy));
                data = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(policy, data, false);

                var payload = new WindowCompositionAttributeData
                {
                    Attribute = WcaAccentPolicy,
                    Data = data,
                    SizeOfData = size
                };

                if (SetWindowCompositionAttribute(handle, ref payload) != 0) return true;

                // 亚克力在部分驱动/远程桌面上不可用，退到普通模糊；再不行就算了。
                policy.AccentState = AccentEnableBlurBehind;
                Marshal.StructureToPtr(policy, data, false);
                payload.Data = data;
                payload.SizeOfData = size;
                return SetWindowCompositionAttribute(handle, ref payload) != 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (data != IntPtr.Zero) Marshal.FreeHGlobal(data);
            }
        }
    }

    /// <summary>自绘基础：圆角路径、投影、发光、文字对齐。</summary>
    public static class Surface
    {
        /// <summary>按比例把 b 叠到 a 上，用来在同一主题内做"更淡/更沉"的派生色。</summary>
        public static Color Blend(Color a, Color b, float amount)
        {
            amount = Math.Max(0f, Math.Min(1f, amount));
            return Color.FromArgb(
                a.A,
                (int)Math.Round(a.R + (b.R - a.R) * amount),
                (int)Math.Round(a.G + (b.G - a.G) * amount),
                (int)Math.Round(a.B + (b.B - a.B) * amount));
        }

        public static GraphicsPath RoundRect(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            var r = Math.Max(0.5f, Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2f));
            var d = r * 2f;

            path.AddArc(rect.X, rect.Y, d, d, 180f, 90f);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270f, 90f);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0f, 90f);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90f, 90f);
            path.CloseFigure();
            return path;
        }

        public static void FillRound(Graphics g, Color color, RectangleF rect, float radius)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return;
            using (var path = RoundRect(rect, radius))
            using (var brush = new SolidBrush(color))
            {
                g.FillPath(brush, path);
            }
        }

        public static void StrokeRound(Graphics g, Color color, RectangleF rect, float radius, float width = 1f)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return;
            var inset = width / 2f;
            var target = RectangleF.Inflate(rect, -inset, -inset);
            using (var path = RoundRect(target, radius))
            using (var pen = new Pen(color, width))
            {
                g.DrawPath(pen, path);
            }
        }

        /// <summary>短而软的投影：两三层就够，层数一多就成了拟物贴图。</summary>
        public static void Shadow(Graphics g, RectangleF rect, float radius, Color color, int offsetY = 2)
        {
            for (var i = 2; i >= 0; i--)
            {
                var alpha = (int)(color.A / (i + 1.5f));
                if (alpha <= 1) continue;

                var layer = new RectangleF(rect.X - i, rect.Y - i + offsetY, rect.Width + i * 2, rect.Height + i * 2);
                FillRound(g, Color.FromArgb(Math.Min(255, alpha), color.R, color.G, color.B), layer, radius + i);
            }
        }

        /// <summary>悬停发光：在描边外侧叠一圈很淡的同色边。</summary>
        public static void Glow(Graphics g, RectangleF rect, float radius, Color color, float width = 3f)
        {
            if (color.A <= 1) return;
            var outer = RectangleF.Inflate(rect, 1, 1);
            using (var path = RoundRect(outer, radius + 1))
            using (var pen = new Pen(color, width))
            {
                g.DrawPath(pen, path);
            }
        }

        /// <summary>
        /// 文字抗锯齿级别。GDI 的 TextRenderer 画到双缓冲位图上是"关掉平滑"的效果（边缘是硬台阶），
        /// 所以文字统一改走 GDI+ 的 DrawString，由这里指定抗锯齿方式。
        /// MCDL_TEXT_HINT=gray 可切灰度抗锯齿，排查彩色边缘问题时用。
        /// </summary>
        private static readonly System.Drawing.Text.TextRenderingHint HintMode = ResolveHint();

        private static System.Drawing.Text.TextRenderingHint ResolveHint()
        {
            var mode = Environment.GetEnvironmentVariable("MCDL_TEXT_HINT");
            if (string.Equals(mode, "gray", StringComparison.OrdinalIgnoreCase))
            {
                return System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            }

            if (string.Equals(mode, "none", StringComparison.OrdinalIgnoreCase))
            {
                return System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
            }

            return System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        }

        /// <summary>竖向渐变：用来给背景垫一点“有厚度的玻璃”观感，比纯色更耐看。</summary>
        public static void VerticalGradient(Graphics g, Rectangle bounds, Color top, Color bottom)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            if (top == bottom)
            {
                using (var brush = new SolidBrush(top)) g.FillRectangle(brush, bounds);
                return;
            }

            using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(bounds, top, bottom, 90f))
            {
                g.FillRectangle(brush, bounds);
            }
        }

        /// <summary>卡片顶部一道 1px 高光，模拟玻璃边缘的反射。</summary>
        public static void GlassHighlight(Graphics g, RectangleF rect, float radius, Color color)
        {
            if (color.A == 0) return;
            var line = new RectangleF(rect.X + radius * 0.6f, rect.Y + 1, rect.Width - radius * 1.2f, 1f);
            using (var brush = new SolidBrush(color))
            {
                g.FillRectangle(brush, line);
            }
        }

        public static void SetQuality(Graphics g, bool dark)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            // 文字走 GDI+：TextRenderer 在双缓冲位图上半透明/关闭平滑，实测字缘是硬台阶。
            g.TextRenderingHint = HintMode;
        }

        /// <summary>量一段单行文字的实际尺寸（与 <see cref="Text"/> 使用同一套排版参数）。</summary>
        public static SizeF Measure(Graphics g, string text, Font font)
        {
            if (string.IsNullOrEmpty(text)) return SizeF.Empty;
            var hint = g.TextRenderingHint;
            g.TextRenderingHint = HintMode;
            try
            {
                var format = Typographic;
                return g.MeasureString(text, font, new SizeF(100000f, 100000f), format);
            }
            finally
            {
                g.TextRenderingHint = hint;
            }
        }

        /// <summary>逐字型排版（去掉 GDI+ 默认的行距留白），保证文字与设计稿的基线一致。</summary>
        private static StringFormat Typographic
        {
            get
            {
                var format = (StringFormat)StringFormat.GenericTypographic.Clone();
                format.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
                format.Trimming = StringTrimming.EllipsisCharacter;
                return format;
            }
        }

        public static void Text(Graphics g, string text, Font font, Color color, RectangleF rect,
            ContentAlignment align = ContentAlignment.MiddleLeft)
        {
            if (string.IsNullOrEmpty(text) || color.A == 0) return;

            var hint = g.TextRenderingHint;
            g.TextRenderingHint = HintMode;

            // 调用方给的矩形高度常常是 96 DPI 时代的常量（比如 18px 一行）。高 DPI 下字体变大了，
            // 矩形没跟着长高时 GDI+ 会把整行裁掉（表现为文字凭空消失），所以这里只纵向放宽到够放一行。
            if (align == ContentAlignment.TopLeft)
            {
                var need = font.GetHeight(g) * 1.35f;
                if (rect.Height < need) rect = new RectangleF(rect.X, rect.Y, rect.Width, need);
            }
            else
            {
                var need = font.GetHeight(g) * 1.35f;
                if (rect.Height < need)
                {
                    var extra = (need - rect.Height) / 2f;
                    rect = RectangleF.Inflate(rect, 0, extra);
                }
            }

            using (var format = Typographic)
            using (var brush = new SolidBrush(color))
            {
                switch (align)
                {
                    case ContentAlignment.MiddleRight:
                        format.Alignment = StringAlignment.Far;
                        format.LineAlignment = StringAlignment.Center;
                        break;
                    case ContentAlignment.MiddleCenter:
                        format.Alignment = StringAlignment.Center;
                        format.LineAlignment = StringAlignment.Center;
                        break;
                    case ContentAlignment.TopLeft:
                        format.Alignment = StringAlignment.Near;
                        format.LineAlignment = StringAlignment.Near;
                        break;
                    default:
                        format.Alignment = StringAlignment.Near;
                        format.LineAlignment = StringAlignment.Center;
                        break;
                }

                g.DrawString(text, font, brush, rect, format);
            }

            g.TextRenderingHint = hint;
        }
    }
}



