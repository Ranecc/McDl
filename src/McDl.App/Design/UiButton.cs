using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace McDl.App.Design
{
    public enum ButtonStyle
    {
        Primary,
        Secondary,
        Ghost
    }

    /// <summary>
    /// 自绘按钮。拟物只落在这一层：投影、悬停发光、按下位移；
    /// 形状和留白保持扁平，不给纹理和渐变。
    /// </summary>
    public sealed class UiButton : Control
    {
        private readonly Timer _anim;
        private ButtonStyle _style = ButtonStyle.Secondary;
        private bool _hover;
        private bool _pressed;

        // 0..1 的动画进度，分别驱动发光强度和按下位移。
        private float _glow;
        private float _press;

        public UiButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            _anim = new Timer { Interval = 15 };
            _anim.Tick += OnAnimTick;
            Cursor = Cursors.Hand;
            TabStop = true;
            Height = Metrics.ButtonHeight;
            AccessibleRole = AccessibleRole.PushButton;
        }

        public ButtonStyle Style
        {
            get { return _style; }
            set
            {
                _style = value;
                Invalidate();
            }
        }

        public bool IsPrimary
        {
            get { return _style == ButtonStyle.Primary; }
            set { Style = value ? ButtonStyle.Primary : ButtonStyle.Secondary; }
        }

        private Palette Palette
        {
            get { return ThemeManager.Palette; }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            StartAnim();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            _pressed = false;
            StartAnim();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            _pressed = true;
            Focus();
            StartAnim();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            _pressed = false;
            StartAnim();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
            {
                e.Handled = true;
                _pressed = true;
                StartAnim();
            }
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.KeyCode != Keys.Enter && e.KeyCode != Keys.Space) return;
            e.Handled = true;
            _pressed = false;
            StartAnim();
            if (_hover || Focused) OnClick(EventArgs.Empty);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            if (!Enabled) _pressed = false;
            Invalidate();
        }

        private void StartAnim()
        {
            if (!_anim.Enabled) _anim.Start();
        }

        private void OnAnimTick(object sender, EventArgs e)
        {
            var glowTarget = _hover && Enabled ? 1f : 0f;
            var pressTarget = _pressed && Enabled ? 1f : 0f;

            _glow = Approach(_glow, glowTarget, 0.25f);
            _press = Approach(_press, pressTarget, 0.35f);

            var settled = Math.Abs(_glow - glowTarget) < 0.02f && Math.Abs(_press - pressTarget) < 0.02f;
            if (settled)
            {
                _glow = glowTarget;
                _press = pressTarget;
                _anim.Stop();
            }

            Invalidate();
        }

        private static float Approach(float current, float target, float step)
        {
            if (current < target) return Math.Min(target, current + step);
            if (current > target) return Math.Max(target, current - step);
            return current;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Surface.SetQuality(g, Palette.IsDark);

            var inset = _press > 0.01f ? 1f : 0f;
            var body = new RectangleF(inset, inset + _press, Width - 1f - inset * 2, Height - 1f - inset * 2);
            var radius = Metrics.RadiusControl;

            if (!Enabled)
            {
                PaintDisabled(g, body, radius);
                return;
            }

            switch (_style)
            {
                case ButtonStyle.Primary:
                    PaintPrimary(g, body, radius);
                    break;
                case ButtonStyle.Ghost:
                    PaintGhost(g, body, radius);
                    break;
                default:
                    PaintSecondary(g, body, radius);
                    break;
            }

            var textColor = _style == ButtonStyle.Primary ? Palette.OnAccent : Palette.Text;

            if (Focused && ShowFocusCues)
            {
                Surface.StrokeRound(g, Color.FromArgb(120, Palette.Accent), RectangleF.Inflate(body, -3, -3), radius - 2, 1.5f);
            }

            Surface.Text(g, Text, Metrics.Button, textColor, body, ContentAlignment.MiddleCenter);
        }

        private void PaintDisabled(Graphics g, RectangleF body, float radius)
        {
            var fill = Palette.IsDark ? Palette.SurfaceAlt : Color.FromArgb(240, 242, 245);
            Surface.FillRound(g, fill, body, radius);
            Surface.StrokeRound(g, Palette.Border, body, radius);
            Surface.Text(g, Text, Metrics.Button, Color.FromArgb(Palette.IsDark ? 110 : 150, Palette.TextMuted),
                body, ContentAlignment.MiddleCenter);
        }

        private void PaintPrimary(Graphics g, RectangleF body, float radius)
        {
            var fill = _press > 0.5f ? Palette.AccentPressed : (_hover ? Palette.AccentHover : Palette.Accent);

            if (_glow > 0.01f)
            {
                var glow = Palette.Glow;
                Surface.Glow(g, body, radius, Color.FromArgb((int)(glow.A * _glow), glow.R, glow.G, glow.B), 4f);
            }

            Surface.FillRound(g, fill, body, radius);
            Surface.StrokeRound(g, Color.FromArgb(40, 0, 0, 0), body, radius);
        }

        private void PaintSecondary(Graphics g, RectangleF body, float radius)
        {
            var fill = _press > 0.5f
                ? (_paletteLite(Palette.SurfaceAlt, Palette.Border))
                : (_hover ? Palette.SurfaceAlt : Palette.Surface);

            if (_glow > 0.01f)
            {
                var glow = Palette.Glow;
                Surface.Glow(g, body, radius, Color.FromArgb((int)(glow.A * 0.6f * _glow), glow.R, glow.G, glow.B), 3f);
            }

            Surface.FillRound(g, fill, body, radius);
            Surface.StrokeRound(g, _hover ? Palette.BorderStrong : Palette.Border, body, radius);
        }

        private void PaintGhost(Graphics g, RectangleF body, float radius)
        {
            if (_hover)
            {
                var glow = Palette.Glow;
                Surface.Glow(g, body, radius, Color.FromArgb((int)(glow.A * 0.5f * _glow), glow.R, glow.G, glow.B), 3f);
                Surface.FillRound(g, Color.FromArgb(Palette.IsDark ? 28 : 18, Palette.Accent), body, radius);
            }
        }

        private static Color _paletteLite(Color a, Color b)
        {
            return Color.FromArgb((a.R + b.R) / 2, (a.G + b.G) / 2, (a.B + b.B) / 2);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _anim.Tick -= OnAnimTick;
                _anim.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}



