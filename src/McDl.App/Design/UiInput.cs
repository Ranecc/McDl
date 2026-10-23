using System;
using System.Drawing;
using System.Windows.Forms;

namespace McDl.App.Design
{
    /// <summary>圆角输入框，内部包一个无边框 TextBox。</summary>
    public sealed class UiField : Control
    {
        private readonly TextBox _box;
        private bool _hot;
        private string _placeholder = string.Empty;

        public UiField()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
            Height = Metrics.InputHeight;
            BackColor = Color.Transparent;

            _box = new TextBox
            {
                BorderStyle = BorderStyle.None,
                Font = Metrics.Body
            };
            _box.GotFocus += (s, e) => { _hot = true; Invalidate(); };
            _box.LostFocus += (s, e) => { _hot = false; Invalidate(); };
            _box.TextChanged += (s, e) => OnTextChanged(EventArgs.Empty);
            _box.HandleCreated += (s, e) => ApplyPlaceholder();
            _box.KeyDown += (s, e) => OnKeyDown(e);
            Controls.Add(_box);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        private void ApplyPlaceholder()
        {
            if (!_box.IsHandleCreated) return;
            const int EM_SETCUEBANNER = 0x1501;
            SendMessage(_box.Handle, EM_SETCUEBANNER, (IntPtr)1, _placeholder);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyPlaceholder();
        }

        public TextBox Inner
        {
            get { return _box; }
        }

        public override string Text
        {
            get { return _box.Text; }
            set { _box.Text = value; }
        }

        public string Placeholder
        {
            get { return _placeholder; }
            set
            {
                _placeholder = value ?? string.Empty;
                ApplyPlaceholder();
                Invalidate();
            }
        }

        public bool ReadOnly
        {
            get { return _box.ReadOnly; }
            set { _box.ReadOnly = value; }
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (_box == null) return;
            var top = Math.Max(1, (Height - _box.PreferredHeight) / 2);
            _box.SetBounds(Metrics.PadControl, top, Math.Max(10, Width - Metrics.PadControl * 2), _box.PreferredHeight);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            _box.Focus();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var palette = ThemeManager.Palette;
            var g = e.Graphics;
            Surface.SetQuality(g, palette.IsDark);

            _box.BackColor = palette.SurfaceAlt;
            _box.ForeColor = palette.Text;

            var body = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
            Surface.FillRound(g, Enabled ? palette.SurfaceAlt : Surface.Blend(palette.SurfaceAlt, palette.Backdrop, 0.4f), body, Metrics.RadiusControl);

            if (!Enabled)
            {
                Surface.StrokeRound(g, palette.Border, body, Metrics.RadiusControl);
                return;
            }

            if (Focused)
            {
                Surface.StrokeRound(g, palette.Accent, body, Metrics.RadiusControl, 1.5f);
            }
            else if (_hot)
            {
                var glow = palette.Glow;
                Surface.Glow(g, body, Metrics.RadiusControl, Color.FromArgb((int)(glow.A * 0.5f), glow.R, glow.G, glow.B), 3f);
                Surface.StrokeRound(g, palette.Accent, body, Metrics.RadiusControl, 1.5f);
            }
            else
            {
                Surface.StrokeRound(g, palette.Border, body, Metrics.RadiusControl);
            }

            if (_box.TextLength == 0 && !_hot && _placeholder.Length > 0)
            {
                Surface.Text(g, _placeholder, Metrics.Body, palette.TextMuted,
                    new RectangleF(Metrics.PadControl, 0, Width - Metrics.PadControl * 2, Height));
            }
        }
    }

    /// <summary>自绘复选框：16px 圆角方框 + 勾。</summary>
    public sealed class UiCheck : Control
    {
        private bool _checked;
        private bool _hover;

        public UiCheck()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Height = 22;
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleRole = AccessibleRole.CheckButton;
        }

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                Invalidate();
                OnCheckedChanged(EventArgs.Empty);
            }
        }

        public event EventHandler CheckedChanged;

        private void OnCheckedChanged(EventArgs e)
        {
            var handler = CheckedChanged;
            if (handler != null) handler(this, e);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) Focus();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left) Checked = !Checked;
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.KeyCode != Keys.Space && e.KeyCode != Keys.Enter) return;
            e.Handled = true;
            Checked = !Checked;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var palette = ThemeManager.Palette;
            var g = e.Graphics;
            Surface.SetQuality(g, palette.IsDark);

            var box = new RectangleF(1, (Height - 17) / 2f, 16, 16);

            if (!Enabled)
            {
                Surface.FillRound(g, palette.SurfaceAlt, box, Metrics.RadiusSmall - 2);
                Surface.StrokeRound(g, palette.Border, box, Metrics.RadiusSmall - 2);
                var disabledText = new RectangleF(26, 0, Math.Max(0, Width - 26), Height);
                Surface.Text(g, Text, Metrics.Body, Surface.Blend(palette.Text, palette.SurfaceAlt, 0.55f), disabledText);
                return;
            }

            if (_checked)
            {
                Surface.FillRound(g, palette.Accent, box, Metrics.RadiusSmall - 2);
                using (var pen = new Pen(palette.OnAccent, 1.8f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round })
                {
                    var pts = new[]
                    {
                        new PointF(box.X + 3.6f, box.Y + 8.2f),
                        new PointF(box.X + 6.8f, box.Y + 11.4f),
                        new PointF(box.X + 12.2f, box.Y + 5.2f)
                    };
                    g.DrawLines(pen, pts);
                }
            }
            else
            {
                Surface.FillRound(g, palette.SurfaceAlt, box, Metrics.RadiusSmall - 2);
                Surface.StrokeRound(g, _hover ? palette.BorderStrong : palette.Border, box, Metrics.RadiusSmall - 2);
            }

            if (Focused)
            {
                Surface.StrokeRound(g, palette.Accent, new RectangleF(box.X - 2, box.Y - 2, box.Width + 4, box.Height + 4), Metrics.RadiusSmall, 1.5f);
            }

            var textRect = new RectangleF(26, 0, Math.Max(0, Width - 26), Height);
            Surface.Text(g, Text, Metrics.Body, palette.Text, textRect);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            var width = 26;
            if (!string.IsNullOrEmpty(Text))
            {
                using (var g = CreateGraphics())
                {
                    width += (int)Math.Ceiling(Surface.Measure(g, Text, Metrics.Body).Width);
                }
            }
            return new Size(width + 4, Math.Max(22, Height));
        }
    }
}





