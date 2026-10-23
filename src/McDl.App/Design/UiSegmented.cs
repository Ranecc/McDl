using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace McDl.App.Design
{
    /// <summary>分段选择器：版本筛选、主题模式这类"几选一"的地方用它。</summary>
    public sealed class UiSegmented : Control
    {
        private readonly List<string> _items = new List<string>();
        private int _selected;
        private int _hover = -1;

        public UiSegmented()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Height = 32;
            BackColor = Color.Transparent;
            TabStop = true;
            Cursor = Cursors.Hand;
        }

        public event EventHandler SelectedChanged;

        public int SelectedIndex
        {
            get { return _selected; }
            set
            {
                if (_items.Count == 0) return;
                var next = Math.Max(0, Math.Min(_items.Count - 1, value));
                if (next == _selected) return;
                _selected = next;
                Invalidate();
                var handler = SelectedChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        public string SelectedText
        {
            get { return _selected >= 0 && _selected < _items.Count ? _items[_selected] : string.Empty; }
        }

        public void SetItems(params string[] items)
        {
            _items.Clear();
            if (items != null) _items.AddRange(items);
            _selected = 0;
            Invalidate();
        }

        private float SegmentWidth
        {
            get { return _items.Count == 0 ? Width : (float)Width / _items.Count; }
        }

        private int HitTest(Point point)
        {
            if (_items.Count == 0) return -1;
            var index = (int)(point.X / SegmentWidth);
            return index < 0 || index >= _items.Count ? -1 : index;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var hit = HitTest(e.Location);
            if (hit == _hover) return;
            _hover = hit;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = -1;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            var hit = HitTest(e.Location);
            if (hit >= 0) SelectedIndex = hit;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Right) { SelectedIndex = _selected + 1; e.Handled = true; }
            else if (e.KeyCode == Keys.Left) { SelectedIndex = _selected - 1; e.Handled = true; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var palette = ThemeManager.Palette;
            var g = e.Graphics;
            Surface.SetQuality(g, palette.IsDark);

            if (_items.Count == 0) return;

            for (var i = 0; i < _items.Count; i++)
            {
                var rect = new RectangleF(i * SegmentWidth, 0, SegmentWidth - 2, Height);
                var active = i == _selected;

                if (active)
                {
                    Surface.FillRound(g, palette.AccentSoft, rect, Metrics.RadiusSmall);
                }
                else if (i == _hover)
                {
                    Surface.FillRound(g, Color.FromArgb(palette.IsDark ? 20 : 10, palette.Text), rect, Metrics.RadiusSmall);
                }

                Surface.Text(g, _items[i], Metrics.Small,
                    active ? (palette.IsDark ? palette.Text : palette.AccentPressed) : (Enabled ? palette.TextMuted : Surface.Blend(palette.TextMuted, palette.SurfaceAlt, 0.5f)), rect, ContentAlignment.MiddleCenter);
            }
        }
    }
}


