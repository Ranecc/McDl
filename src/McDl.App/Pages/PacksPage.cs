using System;
using System.Drawing;
using System.Windows.Forms;
using McDl.App.Design;
using McDl.App.Shell;
using McDl.Core;

namespace McDl.App.Pages
{
    /// <summary>整合包页：选一个整合包文件，装好本体与加载器。</summary>
    public sealed class PacksPage : UserControl
    {
        private const int HeaderHeight = 76;
        private const int SidebarWidth = 320;

        private readonly Settings _settings;
        private readonly UiCard _side;
        private readonly UiButton _choose;
        private readonly UiButton _clear;
        private readonly Label _hint;
        private readonly InstallPanel _install;

        private PackManifest _pack;
        private string _packPath;

        public PacksPage(Settings settings)
        {
            _settings = settings;
            BackColor = Color.Transparent;

            _side = new UiCard { Padding = new Padding(0) };

            _choose = new UiButton { Text = "选择整合包…", Style = ButtonStyle.Primary };
            _choose.Click += (s, e) => ChoosePack();

            _clear = new UiButton { Text = "清除", Style = ButtonStyle.Ghost, Enabled = false };
            _clear.Click += (s, e) => ClearPack();

            _hint = new Label
            {
                AutoSize = false,
                Font = Metrics.Small,
                BackColor = Color.Transparent,
                ForeColor = ThemeManager.Palette.TextMuted,
                Text = string.Empty
            };
            ThemeManager.Changed += (s, e) =>
            {
                if (IsDisposed) return;
                _hint.ForeColor = ThemeManager.Palette.TextMuted;
            };

            _side.Controls.Add(_choose);
            _side.Controls.Add(_clear);
            _side.Controls.Add(_hint);

            _install = new InstallPanel(settings);
            _install.RequestFactory = BuildRequest;
            _install.SetHeader(string.Empty, string.Empty, string.Empty);
            _install.AppendLog("支持 Modrinth(.mrpack)、CurseForge/HMCL 导出(manifest.json)、MCBBS 包、HMCL 原生包。");

            Controls.Add(_side);
            Controls.Add(_install);
        }

        public event Action<string> Footer;

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            _install.Footer += text =>
            {
                var handler = Footer;
                if (handler != null) handler(text);
            };
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutChildren();
        }

        private void LayoutChildren()
        {
            if (_side == null || _install == null || _choose == null) return;
            var header = HeaderHeight;
            var sideWidth = Math.Min(SidebarWidth, Math.Max(240, Width / 3));
            var height = Math.Max(120, Height - header);

            var pagePad = Metrics.PagePad;
            var contentHeight = Math.Max(120, height - pagePad);
            _side.SetBounds(pagePad, header, sideWidth, contentHeight);
            _install.SetBounds(pagePad + sideWidth + Metrics.Gap, header,
                Math.Max(200, Width - pagePad * 2 - sideWidth - Metrics.Gap), contentHeight);

            var pad = 18;
            var inner = sideWidth - pad * 2;

            _choose.SetBounds(pad, pad, inner, Metrics.ButtonHeight);
            _clear.SetBounds(pad, pad + Metrics.ButtonHeight + 10, inner, Metrics.CompactButtonHeight);
            _hint.SetBounds(pad, pad + Metrics.ButtonHeight + 10 + Metrics.CompactButtonHeight + 16, inner, Math.Max(80, height - (pad * 2 + Metrics.ButtonHeight + 60)));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var palette = ThemeManager.Palette;
            var g = e.Graphics;
            Surface.SetQuality(g, palette.IsDark);

            Surface.Text(g, "整合包", Metrics.Hero, palette.Text, new RectangleF(Metrics.PagePad, 6, Width, 36));
            Surface.Text(g, "导入整合包，自动装好游戏本体与加载器", Metrics.Small, palette.TextMuted,
                new RectangleF(Metrics.PagePad, 44, Width, 20));
        }

        private void ChoosePack()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "选择整合包";
                dialog.Filter = "整合包 (*.mrpack;*.zip;*.hmcl)|*.mrpack;*.zip;*.hmcl|所有文件 (*.*)|*.*";
                if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
                TryLoadPack(dialog.FileName);
            }
        }

        /// <summary>拖放/命令行/对话框三条入口共用；返回是否接受。</summary>
        public bool TryLoadPack(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            PackManifest pack;
            try
            {
                pack = PackFormats.Read(path);
            }
            catch (Exception ex)
            {
                // 日志会被后续操作刷走，提示区才是用户真正盯着的位置，两处都写。
                _hint.Text = "打不开这个整合包：" + ex.Message + Environment.NewLine + Environment.NewLine +
                             "支持的格式：Modrinth (.mrpack)、CurseForge / HMCL 导出 (.zip)、MCBBS (.zip)。";
                _install.AppendLog("读不懂这个包：" + ex.Message);
                return false;
            }

            if (string.IsNullOrEmpty(pack.MinecraftVersion))
            {
                _hint.Text = "这个整合包没有写明 Minecraft 版本，McDl 不知道该装哪个本体。";
                _install.AppendLog("包里没有写明 Minecraft 版本，跳过。");
                return false;
            }

            if (pack.Loader != null && !pack.Loader.IsSupported)
            {
                _hint.Text = pack.Loader.UnsupportedMessage ?? "这个加载器不在支持范围内。";
                _install.AppendLog(_hint.Text);
                return false;
            }

            _pack = pack;
            _packPath = path;

            _clear.Enabled = true;
            _hint.Text = DescribePack(pack);

            _install.SetHeader(pack.DisplayName, DescribeLoader(pack), DescribeScope(pack));
            _install.ResetProgress();
            _install.AppendLog("已选择整合包：" + System.IO.Path.GetFileName(path) + "（" + pack.FormatId + "）");

            var handler = Footer;
            if (handler != null) handler("已选择整合包 · " + pack.DisplayName);
            return true;
        }

        private void ClearPack()
        {
            _pack = null;
            _packPath = null;
            _clear.Enabled = false;
            _hint.Text = string.Empty;
            _install.SetHeader(string.Empty, string.Empty, string.Empty);
            _install.ResetProgress();
            Invalidate();
        }

        private static string DescribePack(PackManifest pack)
        {
            var loader = pack.Loader == null || pack.Loader.Kind == LoaderKind.None
                ? "纯原版"
                : pack.Loader.DisplayName + " " + pack.Loader.Version;

            return "文件：" + System.IO.Path.GetFileName(pack.SourcePath) + Environment.NewLine +
                   "格式：" + pack.FormatId + Environment.NewLine +
                   "游戏版本：" + pack.MinecraftVersion + Environment.NewLine +
                   "加载器：" + loader + Environment.NewLine +
                   "包内模组：" + (pack.Files == null ? 0 : pack.Files.Count) + " 个（不在本页下载）";
        }

        private static string DescribeLoader(PackManifest pack)
        {
            var loader = pack.Loader == null || pack.Loader.Kind == LoaderKind.None
                ? "纯原版"
                : pack.Loader.DisplayName + " " + pack.Loader.Version;

            return "整合包 · Minecraft " + pack.MinecraftVersion + " · " + loader;
        }

        private static string DescribeScope(PackManifest pack)
        {
            var count = pack.Files == null ? 0 : pack.Files.Count;
            return "先装游戏本体与加载器；包内 " + count + " 个模组/配置文件不在此处下载";
        }

        private InstallRequest BuildRequest()
        {
            if (_pack == null || string.IsNullOrEmpty(_packPath)) return null;

            return new InstallRequest
            {
                Kind = InstallKind.Modpack,
                ModpackPath = _packPath,
                Root = _settings.Root,
                IncludeAssets = _settings.WithAssets
            };
        }
    }
}



