using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using McDl.App.Design;
using McDl.App.Shell;
using McDl.Core;

namespace McDl.App.Pages
{
    /// <summary>原版下载页：左边挑版本，右边装。</summary>
    public sealed class VanillaPage : UserControl
    {
        private const int HeaderHeight = 76;
        private const int SidebarWidth = 320;

        private readonly Settings _settings;
        private readonly UiCard _side;
        private readonly UiField _search;
        private readonly UiSegmented _filter;
        private readonly UiButton _refresh;
        private readonly UiList _list;
        private readonly InstallPanel _install;
        private readonly Label _loading;

        private bool _busy;

        private List<VersionEntry> _all = new List<VersionEntry>();
        private bool _loaded;

        public VanillaPage(Settings settings)
        {
            _settings = settings;
            BackColor = Color.Transparent;

            _side = new UiCard { Padding = new Padding(0) };
            _search = new UiField { Placeholder = "搜索版本，如 1.21" };
            _search.Text = string.Empty;
            _search.Inner.TextChanged += (s, e) => ApplyFilter();

            _filter = new UiSegmented();
            _filter.SetItems("全部", "正式版", "快照");
            _filter.SelectedChanged += (s, e) => ApplyFilter();

            _refresh = new UiButton { Text = "刷新", Style = ButtonStyle.Secondary };
            _refresh.Click += async (s, e) => await LoadVersionsAsync(true);

            _list = new UiList();
            _list.SelectionChanged += OnVersionSelected;

            _loading = new Label
            {
                Text = "正在获取版本列表…",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = Metrics.Small,
                BackColor = Color.Transparent
            };

            _side.Controls.Add(_search);
            _side.Controls.Add(_filter);
            _side.Controls.Add(_refresh);
            _side.Controls.Add(_list);
            _side.Controls.Add(_loading);

            _loading.ForeColor = Design.ThemeManager.Palette.TextMuted;
            ThemeManager.Changed += (s, e) =>
            {
                if (IsDisposed) return;
                _loading.ForeColor = ThemeManager.Palette.TextMuted;
            };

            _install = new InstallPanel(settings);
            _install.RequestFactory = BuildRequest;

            Controls.Add(_side);
            Controls.Add(_install);
        }

        public event Action<string> Footer;

        public InstallPanel Install
        {
            get { return _install; }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            _install.Footer += text => RaiseFooter(text);
            if (!_loaded) Task.Run(() => LoadVersionsAsync(false));
        }

        private void RaiseFooter(string text)
        {
            var handler = Footer;
            if (handler != null) handler(text);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutChildren();
        }

        private void LayoutChildren()
        {
            if (_side == null || _install == null || _list == null) return;
            var header = HeaderHeight;
            var sideWidth = Math.Min(SidebarWidth, Math.Max(240, Width / 3));
            var height = Math.Max(120, Height - header);

            var pagePad = Metrics.PagePad;
            var contentHeight = Math.Max(120, height - pagePad);
            _side.SetBounds(pagePad, header, sideWidth, contentHeight);
            _install.SetBounds(pagePad + sideWidth + Metrics.Gap, header,
                Math.Max(200, Width - pagePad * 2 - sideWidth - Metrics.Gap), contentHeight);

            var pad = Metrics.PadCard;
            var inner = sideWidth - pad * 2;
            var y = pad;

            _search.SetBounds(pad, y, inner, Metrics.InputHeight);
            y += Metrics.InputHeight + 12;

            _refresh.SetBounds(pad + inner - Metrics.CompactButtonWidth, y, Metrics.CompactButtonWidth, Metrics.CompactButtonHeight);
            _filter.SetBounds(pad, y, Math.Max(100, inner - Metrics.CompactButtonWidth - 10), Metrics.CompactButtonHeight);
            y += Metrics.CompactButtonHeight + 12;

            _loading.SetBounds(pad, y - 2, inner, 20);
            _list.SetBounds(pad, y, inner, Math.Max(60, contentHeight - pad - y));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var palette = ThemeManager.Palette;
            var g = e.Graphics;
            Surface.SetQuality(g, palette.IsDark);

            Surface.Text(g, "原版下载", Metrics.Hero, palette.Text, new RectangleF(Metrics.PagePad, 6, Width, 36));
            Surface.Text(g, "从 Mojang 官方清单下载任意版本", Metrics.Small, palette.TextMuted,
                new RectangleF(Metrics.PagePad, 44, Width, 20));


        }

        private async Task LoadVersionsAsync(bool force)
        {
            if (_loaded && !force) return;

            // 连点「刷新」时后面的请求直接丢掉，避免多份清单互相覆盖、也避免同时占两批连接。
            if (_busy) return;
            _busy = true;
            _refresh.Enabled = false;

            try
            {
                using (var installer = new Installer(MirrorRouter.CreateDefault(), 8))
                {
                    var manifest = await installer.ListVersionsAsync(CancellationToken.None);
                    _all = manifest.Versions ?? new List<VersionEntry>();
                    _loaded = true;

                    Post(() =>
                    {
                        ApplyFilter();
                        if (_list.SelectedIndex < 0 && _list.Items.Count > 0)
                        {
                            var latest = manifest.Latest == null ? null : manifest.Latest.Release;
                            SelectVersion(latest);
                            if (_list.SelectedIndex < 0) _list.SelectedIndex = 0;
                        }
                        _loading.Visible = false;
                        RaiseFooter("就绪 · " + _all.Count + " 个版本可用");
                    });
                }
            }
            catch (Exception ex)
            {
                Post(() =>
                {
                    _loading.Text = "版本列表获取失败，点「刷新」重试。";
                    _loading.Visible = true;
                    _install.AppendLog("获取版本列表失败：" + ex.Message);
                });
            }
            finally
            {
                _busy = false;
                Post(() => _refresh.Enabled = true);
            }
        }

        private void SelectVersion(string id)
        {
            if (string.IsNullOrEmpty(id)) return;

            for (var i = 0; i < _list.Items.Count; i++)
            {
                var entry = _list.Items[i].Tag as VersionEntry;
                if (entry == null || entry.Id != id) continue;
                _list.SelectedIndex = i;
                return;
            }
        }

        private void ApplyFilter()
        {
            if (!_loaded) return;

            var keyword = (_search.Text ?? string.Empty).Trim();
            var onlyType = _filter.SelectedIndex == 1 ? "release" : (_filter.SelectedIndex == 2 ? "snapshot" : null);

            var items = new List<UiList.Item>();
            foreach (var entry in _all)
            {
                if (entry == null) continue;
                if (onlyType != null && !string.Equals(entry.Type, onlyType, StringComparison.OrdinalIgnoreCase)) continue;
                if (keyword.Length > 0 && (entry.Id ?? string.Empty).IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0) continue;

                items.Add(new UiList.Item
                {
                    Title = entry.Id,
                    Subtitle = Format.VersionType(entry.Type) + " · " + Format.Date(entry.ReleaseTime),
                    Tag = entry
                });
            }

            var previous = _list.SelectedItem == null ? null : (_list.SelectedItem.Tag as VersionEntry);
            _list.SetItems(items, previous == null ? -1 : 0);

            if (previous != null) SelectVersion(previous.Id);
        }

        private void OnVersionSelected(object sender, EventArgs e)
        {
            var entry = _list.SelectedItem == null ? null : _list.SelectedItem.Tag as VersionEntry;
            if (entry == null)
            {
                _install.SetHeader(string.Empty, string.Empty, string.Empty);
                return;
            }

            _install.SetHeader(entry.Id,
                Format.VersionType(entry.Type) + " · 发布于 " + Format.Date(entry.ReleaseTime),
                "点「开始安装」后解析文件数与体积");
            _install.ResetProgress();
        }

        private InstallRequest BuildRequest()
        {
            var entry = _list.SelectedItem == null ? null : _list.SelectedItem.Tag as VersionEntry;
            if (entry == null) return null;

            return new InstallRequest
            {
                Kind = InstallKind.Vanilla,
                Version = entry.Id,
                Root = _settings.Root,
                IncludeAssets = _settings.WithAssets
            };
        }

        private void Post(Action action)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
            }
        }
    }
}






