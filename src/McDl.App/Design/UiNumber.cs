using System;
using System.Drawing;
using System.Windows.Forms;

namespace McDl.App.Design
{
    /// <summary>
    /// 小整数步进框：左右各一个热区，滚轮与上下键也能改。
    /// 只用于"并发线程"这类 1..64 的窄范围，不做文本编辑。
    /// </summary>
    public sealed class UiNumber : Control
    {
        private int _value = 8;
        private int _min = 1;
        private int _max = 64;
        private int _hotZone = -1;

        public UiNumber()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Height = Metrics.InputHeight;
            Width = 96;
            BackColor = Color.Transparent;
            TabStop = true;
            Cursor = Cursors.Hand;
        }

        public int Value
        {
            get { return _value; }
            set
            {
                var clamped = Math.Max(_min, Math.Min(_max, value));
                if (clamped == _value) return;
                _value = clamped;
                Invalidate();
                var handler = ValueChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        public int Minimum
        {
            get { return _min; }
            set { _min = value; Value = _value; }
        }

        public int Maximum
        {
            get { return _max; }
            set { _max = value; Value = _value; }
        }

        public event EventHandler ValueChanged;

        private RectangleF MinusZone
        {
            get { return new RectangleF(0, 0, 28, Height); }
        }

        private RectangleF PlusZone
        {
            get { return new RectangleF(Width - 28, 0, 28, Height); }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var zone = MinusZone.Contains(e.Location) ? 0 : (PlusZone.Contains(e.Location) ? 1 : -1);
            if (zone == _hotZone) return;
            _hotZone = zone;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hotZone = -1;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (MinusZone.Contains(e.Location)) Value = _value - 1;
            else if (PlusZone.Contains(e.Location)) Value = _value + 1;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!Focused) return;
            Value = _value + Math.Sign(e.Delta);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Right) { Value = _value + 1; e.Handled = true; }
            else if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Left) { Value = _value - 1; e.Handled = true; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var palette = ThemeManager.Palette;
            var g = e.Graphics;
            Surface.SetQuality(g, palette.IsDark);

            var body = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
            Surface.FillRound(g, palette.SurfaceAlt, body, Metrics.RadiusControl);
            Surface.StrokeRound(g, Focused ? palette.Accent : palette.Border, body, Metrics.RadiusControl, Focused ? 1.5f : 1f);

            var glyphColor = palette.TextMuted;
            if (_hotZone == 0 && Enabled) glyphColor = palette.Accent;
            DrawGlyph(g, glyphColor, new PointF(14, Height / 2f), false);

            glyphColor = palette.TextMuted;
            if (_hotZone == 1 && Enabled) glyphColor = palette.Accent;
            DrawGlyph(g, glyphColor, new PointF(Width - 14, Height / 2f), true);

            Surface.Text(g, _value.ToString(), Metrics.Body,
                Enabled ? palette.Text : palette.TextMuted,
                new RectangleF(28, 0, Width - 56, Height), ContentAlignment.MiddleCenter);
        }

        private static void DrawGlyph(Graphics g, Color color, PointF center, bool plus)
        {
            using (var pen = new Pen(color, 1.6f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round })
            {
                g.DrawLine(pen, center.X - 5, center.Y, center.X + 5, center.Y);
                if (plus) g.DrawLine(pen, center.X, center.Y - 5, center.X, center.Y + 5);
            }
        }
    }
}


