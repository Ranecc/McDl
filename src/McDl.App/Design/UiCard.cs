using System;
using System.Drawing;
using System.Windows.Forms;

namespace McDl.App.Design
{
    /// <summary>
    /// 内容分组用的圆角面板。只负责"一块底 + 一圈边 + 一点投影"，
    /// 里面放什么由页面决定。
    /// </summary>
    public class UiCard : Panel
    {
        public UiCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            Padding = new Padding(Metrics.PadCard);
        }

        public int Radius { get; set; }

        public bool ShowShadow { get; set; } = true;

        public bool ShowBorder { get; set; } = true;

        /// <summary>用次级底色（日志区、内嵌面板这类凹进去的块）。</summary>
        public bool Alternate { get; set; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var palette = ThemeManager.Palette;
            var g = e.Graphics;
            Surface.SetQuality(g, palette.IsDark);

            var radius = Radius > 0 ? Radius : Metrics.RadiusCard;
            var body = new RectangleF(1, 1, Width - 3, Height - 3);

            if (ShowShadow)
            {
                Surface.Shadow(g, body, radius, palette.Shadow, palette.IsDark ? 2 : 3);
            }

            Surface.FillRound(g, Alternate ? palette.SurfaceAlt : palette.Surface, body, radius);
            // 顶部一道极淡的高光，卡片看起来像有厚度的玻璃而不是纯色块。
            Surface.GlassHighlight(g, body, radius,
                palette.IsDark ? Color.FromArgb(14, 255, 255, 255) : Color.FromArgb(150, 255, 255, 255));

            if (ShowBorder)
            {
                Surface.StrokeRound(g, palette.Border, body, radius);
            }
        }
    }

    /// <summary>细进度条。宽度自适应，取值为 0..1。</summary>
    public sealed class UiProgress : Control
    {
        private double _value;

        public UiProgress()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
            Height = 8;
            BackColor = Color.Transparent;
        }

        public double Value
        {
            get { return _value; }
            set
            {
                var clamped = value < 0 ? 0 : (value > 1 ? 1 : value);
                if (Math.Abs(clamped - _value) < 0.0001) return;
                _value = clamped;
                Invalidate();
            }
        }

        public bool Indeterminate { get; set; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var palette = ThemeManager.Palette;
            var g = e.Graphics;
            Surface.SetQuality(g, palette.IsDark);

            var radius = Height / 2f;
            var track = new RectangleF(0, 0, Width - 1, Height - 1);
            Surface.FillRound(g, palette.Track, track, radius);

            if (_value <= 0 && !Indeterminate) return;

            var fillWidth = Math.Max(Height, (float)((Width - 1) * _value));
            var fill = new RectangleF(0, 0, fillWidth, Height - 1);
            Surface.FillRound(g, palette.Accent, fill, radius);
        }
    }
}

