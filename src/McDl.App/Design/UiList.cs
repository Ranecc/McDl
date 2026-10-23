using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace McDl.App.Design
{
    /// <summary>
    /// 版本/条目列表。自己画行、自己管滚动，避免原生 ListBox 在深色主题下
    /// 留一条浅色滚动条。
    /// </summary>
    public sealed class UiList : Control
    {
        public sealed class Item
        {
            public string Title = string.Empty;
            public string Subtitle = string.Empty;
            public object Tag;

            public override string ToString()
            {
                return Title;
            }
        }

        private readonly List<Item> _items = new List<Item>();
        private int _selected = -1;
        private int _top;
        private int _hoverRow = -1;

        /// <summary>列表为空时的占位文字。</summary>
        public string EmptyText = "没有匹配的版本";

        public UiList()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Color.Transparent;
            TabStop = true;
            AccessibleRole = AccessibleRole.List;
        }

        public event EventHandler SelectionChanged;

        public IReadOnlyList<Item> Items
        {
            get { return _items; }
        }

        public Item SelectedItem
        {
            get { return _selected >= 0 && _selected < _items.Count ? _items[_selected] : null; }
        }

        public int SelectedIndex
        {
            get { return _selected; }
            set
            {
                var next = value < 0 || value >= _items.Count ? -1 : value;
                if (next == _selected) return;
                _selected = next;
                EnsureVisible(next);
                Invalidate();
                var handler = SelectionChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        public void SetItems(IEnumerable<Item> items, int keepIndex = 0)
        {
            _items.Clear();
            if (items != null) _items.AddRange(items);
            _selected = -1;
            _top = 0;
            if (_items.Count > 0 && keepIndex >= 0) SelectedIndex = Math.Min(keepIndex, _items.Count - 1);
            Invalidate();
        }

        public void ClearSelection()
        {
            if (_selected < 0) return;
            _selected = -1;
            Invalidate();
        }

        private int RowHeight
        {
            get { return Metrics.RowHeight; }
        }

        private int VisibleRows
        {
            get { return Math.Max(1, Height / RowHeight); }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var row = e.Y / RowHeight + _top;
            var hover = row >= 0 && row < _items.Count ? row : -1;
            if (hover == _hoverRow) return;
            _hoverRow = hover;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hoverRow = -1;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            var row = e.Y / RowHeight + _top;
            if (row < 0 || row >= _items.Count) return;
            SelectedIndex = row;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            ScrollBy(-Math.Sign(e.Delta) * 2);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Down) { SelectedIndex = Math.Min(_items.Count - 1, _selected + 1); e.Handled = true; }
            else if (e.KeyCode == Keys.Up) { SelectedIndex = Math.Max(0, _selected - 1); e.Handled = true; }
            else if (e.KeyCode == Keys.PageDown) { SelectedIndex = Math.Min(_items.Count - 1, _selected + VisibleRows); e.Handled = true; }
            else if (e.KeyCode == Keys.PageUp) { SelectedIndex = Math.Max(0, _selected - VisibleRows); e.Handled = true; }
        }

        private void ScrollBy(int rows)
        {
            var max = Math.Max(0, _items.Count - VisibleRows);
            var next = Math.Max(0, Math.Min(max, _top + rows));
            if (next == _top) return;
            _top = next;
            Invalidate();
        }

        private void EnsureVisible(int index)
        {
            if (index < 0) return;
            if (index < _top) _top = index;
            else if (index >= _top + VisibleRows) _top = index - VisibleRows + 1;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            var max = Math.Max(0, _items.Count - VisibleRows);
            if (_top > max) _top = max;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var palette = ThemeManager.Palette;
            var g = e.Graphics;
            Surface.SetQuality(g, palette.IsDark);

            var rowHeight = RowHeight;
            var first = _top;
            var last = Math.Min(_items.Count, _top + VisibleRows + 1);

            for (var i = first; i < last; i++)
            {
                var y = (i - _top) * rowHeight;
                var rowRect = new RectangleF(0, y, Width, rowHeight - 2);

                if (i == _selected)
                {
                    Surface.FillRound(g, palette.AccentSoft, rowRect, Metrics.RadiusControl);
                    Surface.FillRound(g, palette.Accent, new RectangleF(0, y + 10, 3, rowHeight - 22), 1.5f);
                }
                else if (i == _hoverRow)
                {
                    Surface.FillRound(g, Color.FromArgb(palette.IsDark ? 22 : 12, palette.Text), rowRect, Metrics.RadiusControl);
                }

                var titleRect = new RectangleF(14, y + 9, Width - 24, 20);
                var subRect = new RectangleF(14, y + 29, Width - 24, 18);

                Surface.Text(g, _items[i].Title, Metrics.BodyStrong,
                    i == _selected ? palette.Text : palette.Text, titleRect);

                if (!string.IsNullOrEmpty(_items[i].Subtitle))
                {
                    Surface.Text(g, _items[i].Subtitle, Metrics.Small, palette.TextMuted, subRect);
                }
            }

            if (_items.Count == 0)
            {
                Surface.Text(g, EmptyText, Metrics.Body, palette.TextMuted,
                    new RectangleF(0, 0, Width, Height), ContentAlignment.MiddleCenter);
            }
        }
    }
}





