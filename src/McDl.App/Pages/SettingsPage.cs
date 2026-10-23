using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using McDl.App.Design;

namespace McDl.App.Pages
{
    /// <summary>设置页：配色模式、默认安装目录、并发与是否下载资源文件。</summary>
    public sealed class SettingsPage : UserControl
    {
        private const int HeaderHeight = 76;

        private readonly Settings _settings;
        private readonly UiCard _card;
        private readonly UiSegmented _theme;
        private readonly UiField _root;
        private readonly UiButton _browse;
        private readonly UiNumber _threads;
        private readonly UiCheck _assets;
        private readonly UiButton _save;
        private readonly UiButton _reset;

        public SettingsPage(Settings settings)
        {
            _settings = settings;
            BackColor = Color.Transparent;

            _card = new UiCard { Padding = new Padding(0) };

            _theme = new UiSegmented();
            _theme.SetItems("跟随系统", "浅色", "深色");
            _theme.SelectedIndex = settings.ThemeMode == ThemeMode.Dark ? 2 : (settings.ThemeMode == ThemeMode.Light ? 1 : 0);
            _theme.SelectedChanged += (s, e) => ApplyTheme();

            _root = new UiField { Text = settings.Root, Placeholder = "安装目录" };
            _browse = new UiButton { Text = "浏览…", Style = ButtonStyle.Secondary };
            _browse.Click += OnBrowse;

            _threads = new UiNumber { Value = settings.Threads, Minimum = 1, Maximum = 32 };
            _assets = new UiCheck { Text = "默认下载资源文件（assets）", Checked = settings.WithAssets };
            _save = new UiButton { Text = "保存设置", Style = ButtonStyle.Primary };
            _save.Click += OnSave;
        _reset = new UiButton { Text = "恢复默认", Style = ButtonStyle.Secondary };
        _reset.Click += OnReset;

            _card.Controls.Add(_theme);
            _card.Controls.Add(_root);
            _card.Controls.Add(_browse);
            _card.Controls.Add(_threads);
            _card.Controls.Add(_assets);
            _card.Controls.Add(_save);
        _card.Controls.Add(_reset);

            Controls.Add(_card);
        }

        public event Action<string> Footer;

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutChildren();
        }

        private void LayoutChildren()
        {
            if (_card == null || _theme == null || _root == null) return;
            var width = Math.Min(620, Math.Max(320, Width - Metrics.PagePad * 2));
            var x = Math.Max(Metrics.PagePad, (Width - width) / 2);
            var cardHeight = Math.Max(360, Height - HeaderHeight - 16);
            _card.SetBounds(x, HeaderHeight, width, cardHeight);

            var pad = 24;
            var inner = width - pad * 2;
            var y = pad + 26 + 34;

            _theme.SetBounds(pad, y, Math.Min(300, inner), 32);
            y += 32 + 30;

            _root.SetBounds(pad, y, Math.Max(80, inner - 96 - 8), Metrics.InputHeight);
            _browse.SetBounds(pad + inner - 96, y, 96, Metrics.InputHeight);
            y += Metrics.InputHeight + 28;

            _threads.SetBounds(pad, y, 96, Metrics.InputHeight);
            y += Metrics.InputHeight + 24;

            _assets.SetBounds(pad, y, inner, 22);
            y += 22 + 30;

            _save.SetBounds(pad, y, 132, Metrics.ButtonHeight);
            _reset.SetBounds(pad + 132 + 12, y, 132, Metrics.ButtonHeight);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var palette = ThemeManager.Palette;
            var g = e.Graphics;
            Surface.SetQuality(g, palette.IsDark);

            Surface.Text(g, "设置", Metrics.Hero, palette.Text, new RectangleF(Metrics.PagePad, 6, Width, 36));
            Surface.Text(g, "配色、默认安装目录与下载参数", Metrics.Small, palette.TextMuted,
                new RectangleF(Metrics.PagePad, 44, Width, 20));

            var pad = 24;
            var inner = _card.Width - pad * 2;
            var y = pad;
            var top = _card.Top;

            Surface.Text(g, "配色模式", Metrics.Small, palette.TextMuted, new RectangleF(_card.Left + pad, top + y, inner, 20));
            y += 26 + 34;

            Surface.Text(g, "默认安装目录", Metrics.Small, palette.TextMuted, new RectangleF(_card.Left + pad, top + y, inner, 20));
            y += 26 + Metrics.InputHeight + 28;

            Surface.Text(g, "默认并发线程", Metrics.Small, palette.TextMuted, new RectangleF(_card.Left + pad, top + y, inner, 20));
            y += 26 + Metrics.InputHeight + 24;

            var aboutY = Math.Max(_save.Bottom - _card.Top + 24, _card.Height - pad - 96);
            using (var pen = new Pen(palette.Border))
            {
                g.DrawLine(pen, _card.Left + pad, top + aboutY - 16, _card.Left + _card.Width - pad, top + aboutY - 16);
            }

            Surface.Text(g, "McDl " + Program.Version + " · net48 · Windows", Metrics.Small, palette.Text, new RectangleF(_card.Left + pad, top + aboutY, inner, 20));
            Surface.Text(g, "镜像：Mojang 官方 → BMCLAPI 自动回退", Metrics.Small, palette.TextMuted, new RectangleF(_card.Left + pad, top + aboutY + 22, inner, 20));
            Surface.Text(g, "许可证 MIT · 不接触任何账号凭据", Metrics.Small, palette.TextMuted, new RectangleF(_card.Left + pad, top + aboutY + 44, inner, 20));
        }

        private void OnBrowse(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择默认安装目录（.minecraft）";
                dialog.SelectedPath = Directory.Exists(_root.Text) ? _root.Text : _settings.Root;
                if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
                _root.Text = dialog.SelectedPath;
            }
        }

        private void ApplyTheme()
        {
            var mode = _theme.SelectedIndex == 1 ? ThemeMode.Light : (_theme.SelectedIndex == 2 ? ThemeMode.Dark : ThemeMode.System);
            _settings.ThemeMode = mode;
            _settings.Save();
            ThemeManager.Apply(mode);

            var handler = Footer;
            if (handler != null) handler("配色：" + _theme.SelectedText);
        }

        private void OnReset(object sender, EventArgs e)
        {
            var defaults = Settings.Defaults();
            _settings.Root = defaults.Root;
            _settings.Threads = defaults.Threads;
            _settings.WithAssets = defaults.WithAssets;
            _settings.ThemeMode = defaults.ThemeMode;
            _settings.Save();

            _root.Text = _settings.Root;
            _threads.Value = _settings.Threads;
            _assets.Checked = _settings.WithAssets;
            _theme.SelectedIndex = _settings.ThemeMode == ThemeMode.Light ? 1 : (_settings.ThemeMode == ThemeMode.Dark ? 2 : 0);
            ThemeManager.Apply(_settings.ThemeMode);

            var handler = Footer;
            if (handler != null) handler("已恢复默认设置");
        }

        private void OnSave(object sender, EventArgs e)
        {
            _settings.Root = _root.Text.Trim();
            _settings.Threads = _threads.Value;
            _settings.WithAssets = _assets.Checked;
            _settings.Save();

            var handler = Footer;
            if (handler != null) handler("设置已保存");
        }
    }
}



