using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using McDl.App.Design;
using McDl.App.Pages;
using McDl.Core;

namespace McDl.App.Shell
{
    /// <summary>
    /// 主窗口：无边框自绘外壳（标题栏、左侧导航、页脚），内容交给三个页面。
    /// 圆角与亚克力优先交给 DWM；不支持时退回圆角 Region + 纯色底。
    /// </summary>
    public sealed class ShellWindow : Form
    {
        // 壳的尺寸也跟着 DPI 走：这些值原先写死 96 DPI 像素，125%/150% 下标题栏和侧栏会明显偏小。
        private readonly int TitleBarHeight = Metrics.Px(48);
        private readonly int NavWidth = Metrics.Px(176);
        private readonly int FooterHeight = Metrics.Px(28);
        private readonly int ResizeEdge = Metrics.Px(6);

        private readonly Settings _settings;
        private readonly NavRail _nav;
        private readonly Panel _host;
        private readonly WindowButton _minimize;
        private readonly WindowButton _maximize;
        private readonly WindowButton _close;
        private readonly VanillaPage _vanilla;
        private readonly PacksPage _packs;
        private readonly SettingsPage _settingsPage;

        private UserControl _current;
        private string _status = string.Empty;
        private string _pendingPack;

        public ShellWindow(Settings settings, string packArgument)
        {
            _settings = settings;
            _pendingPack = packArgument;

            Text = "McDl · Minecraft 下载器";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Metrics.Px(1180), Metrics.Px(740));
            MinimumSize = new Size(Metrics.Px(900), Metrics.Px(620));
            DoubleBuffered = true;
            KeyPreview = true;
            Font = Metrics.Body;
            AllowDrop = true;

            _nav = new NavRail();
            _nav.Items.AddRange(new[] { "原版下载", "整合包", "设置" });
            _nav.SelectedIndex = 0;
            _nav.SelectedChanged += (s, e) => ShowPage(_nav.SelectedIndex);

            _host = new Panel { BackColor = ThemeManager.Palette.Backdrop };

            _vanilla = new VanillaPage(settings);
            _packs = new PacksPage(settings);
            _settingsPage = new SettingsPage(settings);

            _vanilla.Footer += SetStatus;
            _packs.Footer += SetStatus;
            _settingsPage.Footer += SetStatus;

            foreach (var page in new UserControl[] { _vanilla, _packs, _settingsPage })
            {
                page.Dock = DockStyle.Fill;
                page.Visible = false;
                _host.Controls.Add(page);
            }

            _minimize = new WindowButton(WindowGlyph.Minimize);
            _minimize.Click += (s, e) => WindowState = FormWindowState.Minimized;

            _maximize = new WindowButton(WindowGlyph.Maximize);
            _maximize.Click += (s, e) => ToggleMaximize();

            _close = new WindowButton(WindowGlyph.Close);
            _close.Click += (s, e) => Close();

            Controls.Add(_nav);
            Controls.Add(_host);
            Controls.Add(_minimize);
            Controls.Add(_maximize);
            Controls.Add(_close);

            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;

            ThemeManager.Changed += OnThemeChanged;
            ShowPage(0);
            SetStatus("就绪");
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ApplyBackdrop();

            if (!string.IsNullOrEmpty(_pendingPack))
            {
                var pack = _pendingPack;
                _pendingPack = null;
                BeginInvoke((Action)(() => OpenPack(pack)));
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyBackdrop();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            InstallPanel.CancelAll();
            base.OnFormClosing(e);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            SyncMaximizeGlyph();
            LayoutShell();
            ApplyRegionFallback();
        }

        private void LayoutShell()
        {
            if (_nav == null || _host == null || _close == null) return;
            var width = ClientSize.Width;
            var height = ClientSize.Height;

            _nav.SetBounds(0, TitleBarHeight, NavWidth, Math.Max(60, height - TitleBarHeight - FooterHeight));
            _host.SetBounds(NavWidth, TitleBarHeight, Math.Max(120, width - NavWidth), Math.Max(80, height - TitleBarHeight - FooterHeight));

            var buttonY = (TitleBarHeight - 26) / 2;
            _close.SetBounds(width - 14 - 34, buttonY, 34, 26);
            _maximize.SetBounds(width - 14 - 34 - 6 - 34, buttonY, 34, 26);
            _minimize.SetBounds(width - 14 - 34 - 6 - 34 - 6 - 34, buttonY, 34, 26);
        }

        private void ShowPage(int index)
        {
            UserControl next;
            switch (index)
            {
                case 1: next = _packs; break;
                case 2: next = _settingsPage; break;
                default: next = _vanilla; break;
            }

            if (_current == next) return;

            foreach (var page in new UserControl[] { _vanilla, _packs, _settingsPage })
            {
                page.Visible = ReferenceEquals(page, next);
            }

            _current = next;
            next.BringToFront();
        }

        private void ApplyBackdrop()
        {
            if (!IsHandleCreated) return;

            var palette = ThemeManager.Palette;
            var tint = palette.IsDark ? Color.FromArgb(150, 18, 22, 26) : Color.FromArgb(150, 250, 251, 252);
            if (Environment.GetEnvironmentVariable("MCDL_NO_BACKDROP") != "1")
            {
                Backdrop.Attach(Handle, palette.IsDark, tint);
            }
            BackColor = palette.Backdrop;
            ApplyRegionFallback();
            Invalidate(true);
        }

        private void ApplyRegionFallback()
        {
            if (!IsHandleCreated) return;

            // DWM 能自己圆角时不裁 Region：裁了反而把阴影和亚克力边缘切掉。
            if (Backdrop.RoundedCornersSupported)
            {
                if (Region != null)
                {
                    Region.Dispose();
                    Region = null;
                }
                return;
            }

            var radius = 12;
            var rect = new Rectangle(0, 0, Width, Height);
            using (var path = Surface.RoundRect(rect, radius))
            {
                var old = Region;
                Region = new Region(path);
                if (old != null) old.Dispose();
            }
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            var palette = ThemeManager.Palette;
            _host.BackColor = palette.Backdrop;
            ApplyBackdrop();
            Invalidate(true);
        }

        private void SetStatus(string text)
        {
            _status = text ?? string.Empty;
            Invalidate(new Rectangle(0, ClientSize.Height - FooterHeight, ClientSize.Width, FooterHeight));
        }

        public bool OpenPack(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            _nav.SelectedIndex = 1;
            ShowPage(1);
            return _packs.TryLoadPack(path);
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            var paths = e.Data == null ? null : e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths == null || paths.Length == 0) return;

            foreach (var path in paths)
            {
                if (!System.IO.File.Exists(path)) continue;
                if (OpenPack(path))
                {
                    e.Effect = DragDropEffects.Copy;
                    return;
                }
            }
        }

        private void ToggleMaximize()
        {
            WindowState = WindowState == FormWindowState.Maximized
                ? FormWindowState.Normal
                : FormWindowState.Maximized;
            SyncMaximizeGlyph();
        }

        private void SyncMaximizeGlyph()
        {
            if (_maximize == null) return;
            _maximize.Glyph = WindowState == FormWindowState.Maximized
                ? WindowGlyph.Restore
                : WindowGlyph.Maximize;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var palette = ThemeManager.Palette;
            var g = e.Graphics;
            Surface.SetQuality(g, palette.IsDark);

            // 背景走一点竖向渐变：纯色在 40 万像素的窗口里显得平，渐变能读出层次。
            // 真正的 DWM 毛玻璃（云母）只在客户区不透明处无法透出，所以层次靠这套渐变 + 卡片高光表达。
            Surface.VerticalGradient(g, ClientRectangle, palette.BackdropTop, palette.BackdropBottom);

            // 标题栏：应用标记 + 名字 + 版本（偏移同样走 DPI 缩放，否则高 DPI 下会叠字）
            var markRect = new RectangleF(Metrics.Px(20), (TitleBarHeight - Metrics.Px(24)) / 2f, Metrics.Px(24), Metrics.Px(24));
            Surface.FillRound(g, palette.Accent, markRect, Metrics.Px(7));
            using (var pen = new Pen(Color.White, Metrics.Pxf(1.8f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                var cx = markRect.X + markRect.Width / 2;
                var cy = markRect.Y + markRect.Height / 2;
                g.DrawLine(pen, cx, cy - Metrics.Pxf(6), cx, cy + Metrics.Pxf(4));
                g.DrawLine(pen, cx, cy + Metrics.Pxf(5), cx - Metrics.Pxf(4.5f), cy);
                g.DrawLine(pen, cx, cy + Metrics.Pxf(5), cx + Metrics.Pxf(4.5f), cy);
            }

            var nameRect = new RectangleF(Metrics.Px(54), 0, Metrics.Px(70), TitleBarHeight);
            var nameSize = Surface.Measure(g, "McDl", Metrics.Section);
            Surface.Text(g, "McDl", Metrics.Section, palette.Text, nameRect);
            Surface.Text(g, Program.Version, Metrics.Tiny, palette.TextMuted,
                new RectangleF(nameRect.X + nameSize.Width + Metrics.Px(4), Metrics.Px(1), Metrics.Px(80), TitleBarHeight));

            // 页脚
            var footerY = ClientSize.Height - FooterHeight;
            using (var pen = new Pen(palette.Border))
            {
                g.DrawLine(pen, NavWidth, footerY, ClientSize.Width, footerY);
            }

            Surface.Text(g, _status, Metrics.Tiny, palette.TextMuted,
                new RectangleF(NavWidth + Metrics.Px(24), footerY, ClientSize.Width - NavWidth - Metrics.Px(220), FooterHeight));

            Surface.Text(g, "net48 · McDl 引擎", Metrics.Tiny, palette.TextMuted,
                new RectangleF(ClientSize.Width - Metrics.Px(200), footerY, Metrics.Px(180), FooterHeight), ContentAlignment.MiddleRight);
        }

        protected override void WndProc(ref Message m)
        {
            const int WmNcHitTest = 0x0084;
            const int WmGetMinMaxInfo = 0x0024;

            if (m.Msg == WmGetMinMaxInfo)
            {
                ClampMaximizedBounds(m);
            }

            if (m.Msg == WmNcHitTest)
            {
                // 用消息里的屏幕坐标，而不是 Cursor.Position：拖拽/多屏/DPI 缩放时两者会不同步。
                var screen = new Point((short)((long)m.LParam & 0xFFFF), (short)(((long)m.LParam >> 16) & 0xFFFF));
                var point = PointToClient(screen);
                var left = point.X <= ResizeEdge;
                var right = point.X >= ClientSize.Width - ResizeEdge;
                var top = point.Y <= ResizeEdge;
                var bottom = point.Y >= ClientSize.Height - ResizeEdge;

                var hit = -1;
                if (top && left) hit = 13;
                else if (top && right) hit = 14;
                else if (bottom && left) hit = 16;
                else if (bottom && right) hit = 17;
                else if (top) hit = 12;
                else if (bottom) hit = 15;
                else if (left) hit = 10;
                else if (right) hit = 11;
                else if (point.Y < TitleBarHeight && point.X < ClientSize.Width - 140) hit = 2;

                if (hit > 0)
                {
                    m.Result = (IntPtr)hit;
                    return;
                }
            }

            base.WndProc(ref m);
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct MinMaxInfo
        {
            public Point Reserved;
            public Point MaxSize;
            public Point MaxPosition;
            public Point MinTrackSize;
            public Point MaxTrackSize;
        }

        private void ClampMaximizedBounds(Message m)
        {
            try
            {
                var info = (MinMaxInfo)System.Runtime.InteropServices.Marshal.PtrToStructure(
                    m.LParam, typeof(MinMaxInfo));
                var screen = Screen.FromHandle(Handle);
                if (screen == null) return;

                var area = screen.WorkingArea;
                var bounds = screen.Bounds;
                info.MaxPosition = new Point(area.X - bounds.X, area.Y - bounds.Y);
                info.MaxSize = new Point(area.Width, area.Height);
                System.Runtime.InteropServices.Marshal.StructureToPtr(info, m.LParam, false);
                m.Result = IntPtr.Zero;
            }
            catch
            {
                // 拿不到工作区就交给系统默认处理。
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) ThemeManager.Changed -= OnThemeChanged;
            base.Dispose(disposing);
        }
    }

    /// <summary>左侧导航。图标用几根线画，避免带一堆位图资源。</summary>
    internal sealed class NavRail : Control
    {
        private readonly List<string> _items = new List<string>();
        private int _selected;
        private int _hover = -1;

        public NavRail()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Color.Transparent;
            TabStop = true;
        }

        public List<string> Items
        {
            get { return _items; }
        }

        public event EventHandler SelectedChanged;

        public int SelectedIndex
        {
            get { return _selected; }
            set
            {
                var next = Math.Max(0, Math.Min(_items.Count - 1, value));
                if (next == _selected) return;
                _selected = next;
                Invalidate();
                var handler = SelectedChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Down) { SelectedIndex = _selected + 1; e.Handled = true; }
            else if (e.KeyCode == Keys.Up) { SelectedIndex = _selected - 1; e.Handled = true; }
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

        private int ItemHeight
        {
            get { return Metrics.Px(44); }
        }

        private int FirstItemY
        {
            get { return 14; }
        }

        private int HitTest(Point point)
        {
            var relative = point.Y - FirstItemY;
            if (relative < 0) return -1;
            var index = relative / ItemHeight;
            return index < _items.Count ? index : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var hit = HitTest(e.Location);
            if (hit == _hover) return;
            _hover = hit;
            Cursor = hit >= 0 ? Cursors.Hand : Cursors.Default;
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
            var hit = HitTest(e.Location);
            if (hit >= 0) SelectedIndex = hit;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var palette = ThemeManager.Palette;
            var g = e.Graphics;
            Surface.SetQuality(g, palette.IsDark);

            for (var i = 0; i < _items.Count; i++)
            {
                var y = FirstItemY + i * ItemHeight;
                var rect = new RectangleF(12, y, Width - 24, ItemHeight - 6);
                var active = i == _selected;

                if (active)
                {
                    Surface.FillRound(g, palette.AccentSoft, rect, Metrics.RadiusControl);
                    Surface.FillRound(g, palette.Accent, new RectangleF(rect.X, rect.Y + 10, 3, rect.Height - 20), 1.5f);

                }
                else if (i == _hover)
                {
                    Surface.FillRound(g, Color.FromArgb(palette.IsDark ? 20 : 10, palette.Text), rect, Metrics.RadiusControl);
                }

                var glyphCenter = new PointF(rect.X + 20, rect.Y + rect.Height / 2f);
                DrawGlyph(g, i, glyphCenter, active ? palette.Accent : palette.TextMuted);

                Surface.Text(g, _items[i], Metrics.Body, active ? palette.Text : palette.TextMuted,
                    new RectangleF(rect.X + 38, rect.Y, rect.Width - 44, rect.Height));
            }
        }

        private static void DrawGlyph(Graphics g, int index, PointF center, Color color)
        {
            using (var pen = new Pen(color, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                if (index == 0)
                {
                    g.DrawLine(pen, center.X, center.Y - 7, center.X, center.Y + 3);
                    g.DrawLine(pen, center.X - 4, center.Y - 1, center.X, center.Y + 3);
                    g.DrawLine(pen, center.X + 4, center.Y - 1, center.X, center.Y + 3);
                    g.DrawLine(pen, center.X - 6, center.Y + 7, center.X + 6, center.Y + 7);
                }
                else if (index == 1)
                {
                    var box = new RectangleF(center.X - 6, center.Y - 5, 12, 10);
                    g.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);
                    g.DrawLine(pen, center.X - 6, center.Y - 2, center.X + 6, center.Y - 2);
                    g.DrawLine(pen, center.X - 2, center.Y - 5, center.X - 2, center.Y - 2);
                }
                else
                {
                    for (var i = -1; i <= 1; i++)
                    {
                        var y = center.Y + i * 5;
                        g.DrawLine(pen, center.X - 6, y, center.X + 6, y);
                        g.DrawLine(pen, center.X + (i == 0 ? 2 : -2), y - 3, center.X + (i == 0 ? 2 : -2), y + 3);
                    }
                }
            }
        }
    }

    internal enum WindowGlyph
    {
        Minimize,
        Maximize,
        Restore,
        Close
    }

    /// <summary>标题栏右上角的窗口按钮。</summary>
    internal sealed class WindowButton : Control
    {
        private WindowGlyph _glyph;

        public WindowGlyph Glyph
        {
            get { return _glyph; }
            set { _glyph = value; Invalidate(); }
        }
        private bool _hover;

        public WindowButton(WindowGlyph glyph)
        {
            _glyph = glyph;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
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

        protected override void OnPaint(PaintEventArgs e)
        {
            var palette = ThemeManager.Palette;
            var g = e.Graphics;
            Surface.SetQuality(g, palette.IsDark);

            if (_hover)
            {
                var fill = _glyph == WindowGlyph.Close
                    ? Color.FromArgb(palette.IsDark ? 90 : 40, palette.Danger)
                    : Color.FromArgb(palette.IsDark ? 26 : 14, palette.Text);
                Surface.FillRound(g, fill, new RectangleF(0, 0, Width, Height), Metrics.RadiusSmall);
            }

            var color = _glyph == WindowGlyph.Close && _hover ? Color.White : palette.TextMuted;
            using (var pen = new Pen(color, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                var cx = Width / 2f;
                var cy = Height / 2f;

                if (_glyph == WindowGlyph.Minimize)
                {
                    g.DrawLine(pen, cx - 5, cy + 3, cx + 5, cy + 3);
                }
                else if (_glyph == WindowGlyph.Maximize)
                {
                    g.DrawRectangle(pen, cx - 5, cy - 4, 10, 9);
                }
                else if (_glyph == WindowGlyph.Restore)
                {
                    g.DrawRectangle(pen, cx - 5, cy - 5, 8, 7);
                    g.DrawRectangle(pen, cx - 2, cy - 2, 8, 7);
                }
                else
                {
                    g.DrawLine(pen, cx - 4, cy - 4, cx + 4, cy + 4);
                    g.DrawLine(pen, cx + 4, cy - 4, cx - 4, cy + 4);
                }
            }
        }
    }
}
















