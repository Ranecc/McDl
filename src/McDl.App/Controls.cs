using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using McDl.Core;

namespace McDl.App
{
    /// <summary>White panel with a hairline border and an optional section title.</summary>
    internal sealed class Card : Panel
    {
        private readonly Font _titleFont = Theme.Ui(9f, FontStyle.Bold);

        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.Surface;
            Padding = new Padding(18, 42, 18, 14);
        }

        public string Title { get; set; }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            using (var pen = new Pen(Theme.Border))
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);

            if (!string.IsNullOrEmpty(Title))
            {
                TextRenderer.DrawText(e.Graphics, Title, _titleFont, new Point(18, 15), Theme.TextMuted,
                    TextFormatFlags.NoPrefix);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _titleFont.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>Two-line owner-drawn list of Minecraft versions.</summary>
    internal sealed class VersionList : ListBox
    {
        private readonly Font _titleFont = Theme.Ui(10f, FontStyle.Bold);
        private readonly Font _metaFont = Theme.Ui(8.25f);

        public VersionList()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BorderStyle = BorderStyle.None;
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = 46;
            IntegralHeight = false;
            BackColor = Theme.Surface;
            ForeColor = Theme.Text;
            Font = _titleFont;
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count) return;

            var entry = Items[e.Index] as VersionEntry;
            if (entry == null) return;

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            var bounds = e.Bounds;

            using (var fill = new SolidBrush(selected ? Theme.AccentSoft : Theme.Surface))
                g.FillRectangle(fill, bounds);

            if (selected)
            {
                using (var marker = new SolidBrush(Theme.Accent))
                    g.FillRectangle(marker, new Rectangle(bounds.X + 2, bounds.Y + 10, 3, bounds.Height - 20));
            }

            var textLeft = bounds.X + 14;
            var textWidth = bounds.Width - 20;
            if (textWidth < 10) textWidth = 10;

            var titleBounds = new Rectangle(textLeft, bounds.Y + 6, textWidth, 20);
            TextRenderer.DrawText(g, entry.Id, _titleFont, titleBounds,
                selected ? Theme.AccentPressed : Theme.Text,
                TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

            var meta = Theme.VersionType(entry.Type) + "   " + Theme.Date(entry.ReleaseTime);
            var metaBounds = new Rectangle(textLeft, bounds.Y + 26, textWidth, 18);
            TextRenderer.DrawText(g, meta, _metaFont, metaBounds, Theme.TextMuted,
                TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _titleFont.Dispose();
                _metaFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal static class Buttons
    {
        public static Button Primary(string text)
        {
            var button = Secondary(text);
            button.BackColor = Theme.Accent;
            button.ForeColor = Color.White;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = Theme.AccentHover;
            button.FlatAppearance.MouseDownBackColor = Theme.AccentPressed;
            return button;
        }

        public static Button Secondary(string text)
        {
            var button = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.Surface,
                ForeColor = Theme.Text,
                Font = Theme.Ui(9f),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false,
                Size = new Size(108, 38)
            };
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Theme.Border;
            button.FlatAppearance.MouseOverBackColor = Theme.SurfaceAlt;
            button.FlatAppearance.MouseDownBackColor = Theme.Border;
            return button;
        }
    }

    internal static class TextBoxes
    {
        public static TextBox Single(string text)
        {
            return new TextBox
            {
                Text = text ?? string.Empty,
                BorderStyle = BorderStyle.FixedSingle,
                Font = Theme.Ui(9.25f),
                BackColor = Theme.Surface,
                ForeColor = Theme.Text
            };
        }

        /// <summary>Shows greyed placeholder text inside an empty text box (EM_SETCUEBANNER).</summary>
        public static void Banner(TextBox box, string text)
        {
            Action apply = () =>
            {
                const int EmSetCueBanner = 0x1501;
                NativeMethods.SendMessage(box.Handle, EmSetCueBanner, new IntPtr(1), text);
            };

            if (box.IsHandleCreated) apply();
            else box.HandleCreated += (sender, e) => apply();
        }

        private static class NativeMethods
        {
            [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
            public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
        }
    }
}
