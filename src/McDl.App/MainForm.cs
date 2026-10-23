using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using McDl.Core;

namespace McDl.App
{
    public sealed class MainForm : Form
    {
        private readonly MirrorRouter _router = MirrorRouter.CreateDefault();
        private readonly Settings _settings = Settings.Load();

        private List<VersionEntry> _versions = new List<VersionEntry>();
        private InstallPlan _plan;
        private CancellationTokenSource _cts;
        private bool _busy;
        private bool _filling;

        // Non-null while the user picked a .mrpack: install then runs as InstallKind.Modpack.
        private string _modpackPath;

        private Panel _header;
        private Panel _footer;
        private Panel _content;
        private Card _leftCard;
        private Card _rightCard;
        private Card _logCard;
        private Panel _separator;

        private Label _headerTitle;
        private Label _headerSub;
        private Label _headerMirror;
        private Label _footerLabel;
        private Label _footerVersion;

        private ComboBox _typeFilter;
        private TextBox _search;
        private Button _refresh;
        private VersionList _list;

        private Label _vTitle;
        private Label _vMeta;
        private Label _vStats;
        private TextBox _root;
        private Button _browse;
        private NumericUpDown _threads;
        private CheckBox _withAssets;
        private Button _openPack;
        private Label _mirror;
        private ProgressBar _bar;
        private Label _pct;
        private Label _speed;
        private Label _file;
        private Button _start;
        private Button _cancel;
        private Button _open;
        private Button _verify;
        private TextBox _log;

        public MainForm()
            : this(null)
        {
        }

        public MainForm(string[] args)
        {
            Text = "McDl · Minecraft 下载器";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1000, 700);
            MinimumSize = new Size(940, 640);
            BackColor = Theme.Background;
            Font = Theme.Ui(9f);
            DoubleBuffered = true;

            BuildHeader();
            BuildContent();
            BuildFooter();

            Controls.Add(_content);
            Controls.Add(_footer);
            Controls.Add(_header);

            Shown += async (sender, e) => await LoadVersionsAsync();
            FormClosing += OnFormClosing;

            AllowDrop = true;
            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;

            var initial = FirstModpackArgument(args);
            if (initial != null) LoadModpack(initial);
        }

        private static readonly string[] PackExtensions = { ".mrpack", ".zip", ".hmcl" };

        private static string FirstModpackArgument(string[] args)
        {
            if (args == null) return null;
            foreach (var arg in args)
            {
                if (string.IsNullOrEmpty(arg) || !File.Exists(arg)) continue;
                if (LooksLikePack(arg)) return arg;
            }
            return null;
        }

        /// <summary>扩展名只是个提示，真正的判断交给 PackFormats。</summary>
        private static bool LooksLikePack(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var extension = Path.GetExtension(path);
            if (PackExtensions.Any(x => string.Equals(x, extension, StringComparison.OrdinalIgnoreCase))) return true;

            try
            {
                PackFormats.Read(path);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = DragDropPaths(e).Length > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            var paths = DragDropPaths(e);
            if (paths.Length > 0) LoadModpack(paths[0]);
        }

        private static string[] DragDropPaths(DragEventArgs e)
        {
            var files = e.Data == null ? null : e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null) return new string[0];
            return files.Where(f => File.Exists(f) && LooksLikePack(f)).ToArray();
        }

        private void BuildHeader()
        {
            _header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 74,
                BackColor = Theme.Surface,
                Padding = new Padding(0)
            };
            _header.Paint += (sender, e) =>
            {
                using (var pen = new Pen(Theme.Border)) e.Graphics.DrawLine(pen, 0, _header.Height - 1, _header.Width, _header.Height - 1);
                using (var accent = new SolidBrush(Theme.Accent)) e.Graphics.FillRectangle(accent, 0, 0, 4, _header.Height - 1);
            };

            _headerTitle = new Label
            {
                Text = "McDl",
                AutoSize = true,
                Font = Theme.Ui(17f, FontStyle.Bold),
                ForeColor = Theme.Text,
                Location = new Point(24, 12)
            };

            _headerSub = new Label
            {
                Text = "原版与整合包本体下载 · 自研引擎 · 断点续传",
                AutoSize = true,
                Font = Theme.Ui(9f),
                ForeColor = Theme.TextMuted,
                Location = new Point(26, 44)
            };

            _headerMirror = new Label
            {
                Text = "镜像：" + string.Join(" → ", _router.Profiles.Select(p => Theme.MirrorName(p.Name))) + "（自动回退）",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleRight,
                Font = Theme.Ui(8.75f),
                ForeColor = Theme.TextMuted,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };

            _header.Controls.Add(_headerTitle);
            _header.Controls.Add(_headerSub);
            _header.Controls.Add(_headerMirror);
            _header.Resize += (sender, e) => _headerMirror.Bounds = new Rectangle(_header.Width - 24 - 420, 26, 420, 22);
            _headerMirror.Bounds = new Rectangle(_header.Width - 24 - 420, 26, 420, 22);
        }

        private void BuildFooter()
        {
            _footer = new Panel { Dock = DockStyle.Bottom, Height = 28, BackColor = Theme.Surface };
            _footer.Paint += (sender, e) =>
            {
                using (var pen = new Pen(Theme.Border)) e.Graphics.DrawLine(pen, 0, 0, _footer.Width, 0);
            };

            _footerLabel = new Label
            {
                Text = "就绪",
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(24, 0, 0, 0),
                Font = Theme.Ui(8.5f),
                ForeColor = Theme.TextMuted
            };

            _footerVersion = new Label
            {
                Text = "v" + Theme.ShortVersion() + " · net48 · McDl 引擎",
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleRight,
                Font = Theme.Ui(8.25f),
                ForeColor = Theme.TextMuted,
                BackColor = Theme.Surface
            };

            _footer.Controls.Add(_footerVersion);
            _footer.Controls.Add(_footerLabel);
            _footer.Resize += (sender, e) =>
            {
                _footerVersion.Location = new Point(_footer.Width - _footerVersion.Width - 20,
                    (_footer.Height - _footerVersion.Height) / 2);
            };
        }

        private void BuildContent()
        {
            _content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16) };
            _content.Resize += (sender, e) => LayoutColumns();

            _leftCard = new Card { Padding = new Padding(14, 14, 14, 14) };
            _rightCard = new Card { Padding = new Padding(18, 16, 18, 16) };
            _logCard = new Card { Padding = new Padding(14, 14, 14, 12) };

            BuildLeftCard();
            BuildRightCard();
            BuildLogCard();

            _content.Controls.Add(_leftCard);
            _content.Controls.Add(_rightCard);
            _content.Controls.Add(_logCard);
        }

        private void BuildLeftCard()
        {
            var caption = new Label
            {
                Text = "版本列表",
                AutoSize = true,
                Font = Theme.Ui(9.5f, FontStyle.Bold),
                ForeColor = Theme.Text,
                Location = new Point(14, 15)
            };

            _refresh = Buttons.Secondary("刷新");
            _refresh.Size = new Size(58, 26);
            _refresh.Font = Theme.Ui(8.75f);
            _refresh.Click += async (sender, e) => await LoadVersionsAsync();
            _refresh.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            _typeFilter = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                Font = Theme.Ui(9f),
                Width = 104
            };
            _typeFilter.Items.AddRange(new object[] { "全部", "正式版", "快照", "远古版" });
            _typeFilter.SelectedIndex = 0;
            _typeFilter.SelectedIndexChanged += (sender, e) => ApplyFilter();

            _search = TextBoxes.Single(string.Empty);
            _search.Font = Theme.Ui(9f);
            _search.ForeColor = Theme.Text;
            _search.TextChanged += (sender, e) => ApplyFilter();
            TextBoxes.Banner(_search, "搜索版本，如 1.21");

            _list = new VersionList();
            _list.SelectedIndexChanged += (sender, e) => OnVersionSelected();

            _leftCard.Controls.Add(caption);
            _leftCard.Controls.Add(_refresh);
            _leftCard.Controls.Add(_typeFilter);
            _leftCard.Controls.Add(_search);
            _leftCard.Controls.Add(_list);

            _leftCard.Resize += (sender, e) => LayoutLeft();
        }

        private void LayoutLeft()
        {
            var width = _leftCard.ClientSize.Width - 28;
            _refresh.Location = new Point(_leftCard.ClientSize.Width - 14 - _refresh.Width, 13);
            _typeFilter.Bounds = new Rectangle(14, 46, 104, 26);
            _search.Bounds = new Rectangle(14 + 104 + 8, 46, Math.Max(60, width - 112), 26);
            _list.Bounds = new Rectangle(14, 82, Math.Max(80, width), Math.Max(60, _leftCard.ClientSize.Height - 96));
        }

        private void BuildRightCard()
        {
            _vTitle = new Label
            {
                Text = "请选择版本",
                AutoSize = false,
                Font = Theme.Ui(15f, FontStyle.Bold),
                ForeColor = Theme.Text
            };

            _vMeta = new Label { Text = string.Empty, AutoSize = false, Font = Theme.Ui(9f), ForeColor = Theme.TextMuted };
            _vStats = new Label { Text = string.Empty, AutoSize = false, Font = Theme.Ui(8.75f), ForeColor = Theme.TextMuted };

            _separator = new Panel { BackColor = Theme.Border, Height = 1 };

            _openPack = Buttons.Secondary("整合包…");
            _openPack.Size = new Size(92, 30);
            _openPack.Font = Theme.Ui(8.75f);
            _openPack.Click += OnOpenModpack;

            var rootCaption = new Label
            {
                Text = "安装目录",
                AutoSize = true,
                Font = Theme.Ui(9f),
                ForeColor = Theme.TextMuted,
                Location = new Point(18, 112)
            };
            _root = TextBoxes.Single(_settings.Root);
            _browse = Buttons.Secondary("浏览…");
            _browse.Size = new Size(92, 30);
            _browse.Font = Theme.Ui(8.75f);
            _browse.Click += OnBrowse;

            var threadsCaption = new Label
            {
                Text = "并发线程",
                AutoSize = true,
                Font = Theme.Ui(9f),
                ForeColor = Theme.TextMuted,
                Location = new Point(18, 176)
            };
            _threads = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 32,
                Value = Math.Max(1, Math.Min(32, _settings.Threads)),
                Font = Theme.Ui(9f),
                BorderStyle = BorderStyle.FixedSingle,
                TextAlign = HorizontalAlignment.Center
            };
            _withAssets = new CheckBox
            {
                Text = "下载资源文件 (assets)",
                Checked = _settings.WithAssets,
                AutoSize = true,
                Font = Theme.Ui(9f),
                ForeColor = Theme.Text
            };
            _mirror = new Label { Text = string.Empty, AutoSize = false, Font = Theme.Ui(8.5f), ForeColor = Theme.TextMuted };

            _bar = new ProgressBar { Minimum = 0, Maximum = 1000, Style = ProgressBarStyle.Continuous };
            _pct = new Label { Text = "0%", AutoSize = false, Font = Theme.Ui(9f, FontStyle.Bold), ForeColor = Theme.Text, TextAlign = ContentAlignment.MiddleLeft };
            _speed = new Label { Text = string.Empty, AutoSize = false, Font = Theme.Ui(8.75f), ForeColor = Theme.TextMuted, TextAlign = ContentAlignment.MiddleRight };
            _file = new Label { Text = string.Empty, AutoSize = false, Font = Theme.Ui(8.25f), ForeColor = Theme.TextMuted, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };

            _start = Buttons.Primary("开始安装");
            _start.Size = new Size(148, 40);
            _start.Font = Theme.Ui(9.5f, FontStyle.Bold);
            _start.Click += OnStart;
            _cancel = Buttons.Secondary("取消");
            _cancel.Size = new Size(92, 40);
            _cancel.Enabled = false;
            _cancel.Click += (sender, e) =>
            {
                if (_cts != null) _cts.Cancel();
            };
            _open = Buttons.Secondary("打开目录");
            _open.Size = new Size(110, 40);
            _open.Click += OnOpenFolder;

            _verify = Buttons.Secondary("校验文件");
            _verify.Size = new Size(96, 40);
            _verify.Enabled = false;
            _verify.Click += OnVerifyFiles;

            _rightCard.Controls.Add(_vTitle);
            _rightCard.Controls.Add(_vMeta);
            _rightCard.Controls.Add(_vStats);
            _rightCard.Controls.Add(_separator);
            _rightCard.Controls.Add(_openPack);
            _rightCard.Controls.Add(rootCaption);
            _rightCard.Controls.Add(_root);
            _rightCard.Controls.Add(_browse);
            _rightCard.Controls.Add(threadsCaption);
            _rightCard.Controls.Add(_threads);
            _rightCard.Controls.Add(_withAssets);
            _rightCard.Controls.Add(_mirror);
            _rightCard.Controls.Add(_bar);
            _rightCard.Controls.Add(_pct);
            _rightCard.Controls.Add(_speed);
            _rightCard.Controls.Add(_file);
            _rightCard.Controls.Add(_start);
            _rightCard.Controls.Add(_cancel);
            _rightCard.Controls.Add(_open);
            _rightCard.Controls.Add(_verify);

            _rightCard.Resize += (sender, e) => LayoutRight();
        }

        private void LayoutRight()
        {
            var height = _rightCard.ClientSize.Height;
            var width = _rightCard.ClientSize.Width;
            var inner = Math.Max(120, width - 36);

            _vTitle.Bounds = new Rectangle(18, 14, Math.Max(80, inner - 100), 34);
            _openPack.Location = new Point(18 + inner - 92, 16);
            _vMeta.Bounds = new Rectangle(18, 50, inner, 20);
            _vStats.Bounds = new Rectangle(18, 72, inner, 20);
            _separator.Bounds = new Rectangle(18, 98, inner, 1);

            _root.Bounds = new Rectangle(18, 138, Math.Max(80, inner - 100), 28);
            _browse.Location = new Point(18 + inner - 92, 136);

            _threads.Bounds = new Rectangle(92, 173, 64, 26);
            _withAssets.Location = new Point(174, 177);
            _mirror.Bounds = new Rectangle(18, 206, inner, 18);

            _bar.Bounds = new Rectangle(18, 232, inner, 16);
            _pct.Bounds = new Rectangle(18, 254, 120, 20);
            _speed.Bounds = new Rectangle(18 + inner - 260, 254, 260, 20);
            _file.Bounds = new Rectangle(18, 274, inner, 18);

            var buttonY = height - 16 - 40;
            _start.Location = new Point(18, buttonY);
            _cancel.Location = new Point(18 + 158, buttonY);
            _open.Location = new Point(18 + inner - 110, buttonY);
            _verify.Location = new Point(18 + inner - 110 - 8 - 96, buttonY);
        }

        private void BuildLogCard()
        {
            var caption = new Label
            {
                Text = "日志",
                AutoSize = true,
                Font = Theme.Ui(9.5f, FontStyle.Bold),
                ForeColor = Theme.Text,
                Location = new Point(14, 15)
            };

            _log = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Theme.Surface,
                ForeColor = Theme.TextMuted,
                Font = Theme.Mono(8.5f),
                WordWrap = false,
                ShortcutsEnabled = true
            };

            _logCard.Controls.Add(caption);
            _logCard.Controls.Add(_log);
            _logCard.Resize += (sender, e) =>
            {
                _log.Bounds = new Rectangle(14, 40, Math.Max(120, _logCard.ClientSize.Width - 28),
                    Math.Max(60, _logCard.ClientSize.Height - 52));
            };
        }

        private void LayoutColumns()
        {
            var width = _content.ClientSize.Width;
            var height = _content.ClientSize.Height;
            var leftWidth = 296;
            var gap = 14;
            var rightWidth = Math.Max(320, width - leftWidth - gap);

            // The install card keeps a fixed, comfortable height; the log takes whatever is left.
            var rightHeight = Math.Max(330, Math.Min(354, height - 150));

            _leftCard.Bounds = new Rectangle(0, 0, leftWidth, height);
            _rightCard.Bounds = new Rectangle(leftWidth + gap, 0, rightWidth, rightHeight);
            _logCard.Bounds = new Rectangle(leftWidth + gap, rightHeight + gap, rightWidth,
                Math.Max(90, height - rightHeight - gap));
        }

        // ---------------------------------------------------------------- data

        private async Task LoadVersionsAsync()
        {
            if (_busy) return;

            SetBusyUi(true, "正在获取版本列表…");
            AppendLog("正在获取版本列表…");

            try
            {
                var manifest = await Task.Run(async () =>
                {
                    using (var installer = new Installer(_router, 4))
                        return await installer.ListVersionsAsync(CancellationToken.None).ConfigureAwait(false);
                }).ConfigureAwait(true);

                if (manifest?.Versions == null || manifest.Versions.Count == 0)
                {
                    AppendLog("版本列表为空，请检查网络后重试。");
                    SetFooter("版本列表获取失败");
                    return;
                }

                _versions = manifest.Versions;
                ApplyFilter();

                // A .mrpack chosen before the list arrived (drag-drop or command line) owns the panel.
                if (_modpackPath == null) SelectDefault(manifest.Latest?.Release);

                AppendLog(string.Format("已加载 {0} 个版本（最新正式版 {1}，最新快照 {2}）。",
                    _versions.Count, manifest.Latest?.Release ?? "?", manifest.Latest?.Snapshot ?? "?"));
                SetFooter("就绪 · " + _versions.Count + " 个版本可用");
            }
            catch (Exception ex)
            {
                AppendLog("获取版本列表失败：" + ex.Message);
                SetFooter("版本列表获取失败");
            }
            finally
            {
                SetBusyUi(false, null);
            }
        }

        private void SelectDefault(string preferredId)
        {
            if (_list.Items.Count == 0) return;

            var index = 0;
            if (!string.IsNullOrEmpty(preferredId))
            {
                for (var i = 0; i < _list.Items.Count; i++)
                {
                    var entry = _list.Items[i] as VersionEntry;
                    if (entry != null && entry.Id == preferredId) { index = i; break; }
                }
            }

            _list.SelectedIndex = index;
            ScrollSelectionIntoView();
        }

        /// <summary>Keeps the selected version visible (the list is long; default selection sits far down).</summary>
        private void ScrollSelectionIntoView()
        {
            var index = _list.SelectedIndex;
            if (index < 0) return;

            var visible = Math.Max(1, _list.ClientSize.Height / Math.Max(1, _list.ItemHeight));
            var top = _list.TopIndex;
            if (index < top || index >= top + visible)
                _list.TopIndex = Math.Max(0, Math.Min(index, Math.Max(0, _list.Items.Count - visible)));
        }

        private void ApplyFilter()
        {
            var query = (_search.Text ?? string.Empty).Trim();
            var filter = _typeFilter.SelectedItem as string ?? "全部";

            var filtered = _versions.Where(v =>
            {
                if (v?.Id == null) return false;

                var type = (v.Type ?? string.Empty).ToLowerInvariant();
                switch (filter)
                {
                    case "正式版": if (type != "release") return false; break;
                    case "快照": if (type != "snapshot") return false; break;
                    case "远古版": if (type != "old_beta" && type != "old_alpha") return false; break;
                }

                return query.Length == 0 || v.Id.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
            }).ToList();

            _filling = true;
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                foreach (var entry in filtered) _list.Items.Add(entry);
            }
            finally
            {
                _list.EndUpdate();
                _filling = false;
            }

            // While a .mrpack is pending the list stays unselected: picking a row means "install that
            // version instead", so the automatic first-row selection must not clear the pack.
            if (_modpackPath == null && _list.Items.Count > 0 && _list.SelectedIndex < 0) _list.SelectedIndex = 0;
            if (_list.Items.Count == 0)
            {
                _vTitle.Text = "没有匹配的版本";
                _vMeta.Text = string.Empty;
                _vStats.Text = string.Empty;
            }
        }

        private void OnVersionSelected()
        {
            if (_filling) return;

            var entry = _list.SelectedItem as VersionEntry;
            if (entry == null) return;

            if (_modpackPath != null)
            {
                _modpackPath = null;
                AppendLog("已切回按版本安装。");
            }

            _vTitle.Text = entry.Id;
            _vMeta.Text = Theme.VersionType(entry.Type) + "   ·   发布于 " + Theme.Date(entry.ReleaseTime);

            if (_plan != null && _plan.VersionId == entry.Id)
            {
                _vStats.Text = PlanSummary(_plan);
            }
            else
            {
                _vStats.Text = "点击「开始安装」后将显示文件数与体积（约 300–600 MB）。";
            }
        }

        private static string PlanSummary(InstallPlan plan)
        {
            var natives = plan.Natives.Count;
            return string.Format("{0} 个文件 · {1} · 资源索引 {2}{3}{4}",
                plan.Files.Count,
                Theme.Bytes(plan.TotalBytes),
                plan.AssetIndexId,
                plan.VirtualAssets ? "（虚拟）" : string.Empty,
                natives > 0 ? " · 解压 " + natives + " 个 natives" : string.Empty);
        }

        // --------------------------------------------------------------- actions

        private void OnBrowse(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择 .minecraft 安装目录";
                dialog.ShowNewFolderButton = true;
                if (Directory.Exists(_root.Text)) dialog.SelectedPath = _root.Text;

                if (dialog.ShowDialog(this) == DialogResult.OK) _root.Text = dialog.SelectedPath;
            }
        }

        private void OnOpenModpack(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "选择整合包";
                dialog.Filter = "整合包 (*.mrpack;*.zip;*.hmcl)|*.mrpack;*.zip;*.hmcl|所有文件 (*.*)|*.*";
                dialog.CheckFileExists = true;

                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                LoadModpack(dialog.FileName);
            }
        }

        /// <summary>认下这个整合包：先装游戏本体，再装它声明的加载器。</summary>
        private void LoadModpack(string fileName)
        {
            PackManifest pack;
            try
            {
                pack = PackFormats.Read(fileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "读不了这个整合包：" + ex.Message, "McDl",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrEmpty(pack.MinecraftVersion))
            {
                MessageBox.Show(this, "整合包里没写 Minecraft 版本，没法确定装哪个版本。", "McDl",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!pack.Loader.IsSupported)
            {
                MessageBox.Show(this,
                    pack.Loader.UnsupportedMessage ?? ("这个包要 " + pack.Loader.DisplayName + "，McDl 暂时装不了。"),
                    "暂不支持", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _modpackPath = fileName;
            _plan = null;

            var loaderLine = pack.Loader.Kind == LoaderKind.None
                ? "纯原版"
                : pack.Loader.DisplayName + (pack.Loader.Version == null ? string.Empty : " " + pack.Loader.Version);

            _vTitle.Text = pack.DisplayName;
            _vMeta.Text = "整合包   ·   Minecraft " + pack.MinecraftVersion + "   ·   " + loaderLine;
            _vStats.Text = "先装原版游戏本体，再装加载器；包里的模组与配置文件不下载。";
            _bar.Value = 0;
            _pct.Text = "0%";
            _speed.Text = string.Empty;
            _file.Text = string.Empty;
            _list.ClearSelected();

            AppendLog(string.Format("已选择整合包：{0}（{1}，MC {2}，{3}）",
                Path.GetFileName(fileName), pack.FormatId, pack.MinecraftVersion, loaderLine));
        }

        private void OnOpenFolder(object sender, EventArgs e)
        {
            var path = (_root.Text ?? string.Empty).Trim();
            try
            {
                if (path.Length > 0 && !Directory.Exists(path)) Directory.CreateDirectory(path);
                if (path.Length > 0) Process.Start("explorer.exe", "\"" + path + "\"");
            }
            catch (Exception ex)
            {
                AppendLog("无法打开目录：" + ex.Message);
            }
        }

        private async void OnStart(object sender, EventArgs e)
        {
            if (_busy) return;

            var entry = _list.SelectedItem as VersionEntry;
            var modpackPath = _modpackPath;

            if (modpackPath == null && entry == null)
            {
                MessageBox.Show(this, "请先在左侧选择一个版本，或点击「整合包…」选择 .mrpack 文件。",
                    "McDl", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var root = (_root.Text ?? string.Empty).Trim();
            if (root.Length == 0)
            {
                MessageBox.Show(this, "请填写安装目录。", "McDl", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                root = Path.GetFullPath(root);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "安装目录无效：" + ex.Message, "McDl", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _root.Text = root;
            _settings.Root = root;
            _settings.Threads = (int)_threads.Value;
            _settings.WithAssets = _withAssets.Checked;
            _settings.Save();

            var versionId = modpackPath != null ? Path.GetFileNameWithoutExtension(modpackPath) : entry.Id;
            var threads = (int)_threads.Value;
            var includeAssets = _withAssets.Checked;
            var progressSink = new Progress<TransferStats>(OnStats);

            _plan = null;
            _bar.Value = 0;
            _pct.Text = "0%";
            _speed.Text = string.Empty;
            _file.Text = string.Empty;
            _vStats.Text = "正在解析版本信息…";

            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            SetBusyUi(true, modpackPath != null ? "正在安装整合包" : "正在安装 " + versionId);
            AppendLog(modpackPath != null
                ? string.Format("开始安装整合包 {0} → {1}（{2} 线程）", Path.GetFileName(modpackPath), root, threads)
                : string.Format("开始安装 {0} → {1}（{2} 线程）", versionId, root, threads));

            try
            {
                var request = modpackPath != null
                    ? new InstallRequest
                    {
                        Kind = InstallKind.Modpack,
                        Root = root,
                        ModpackPath = modpackPath,
                        IncludeAssets = includeAssets
                    }
                    : new InstallRequest
                    {
                        Kind = InstallKind.Vanilla,
                        Root = root,
                        Version = versionId,
                        IncludeAssets = includeAssets
                    };

                InstallSummary summary = null;

                await Task.Run(async () =>
                {
                    using (var installer = new Installer(_router, threads))
                    {
                        installer.Log = AppendLog;
                        summary = await installer.InstallAsync(request, progressSink, ct, plan => Ui(() =>
                        {
                            _plan = plan;
                            _vStats.Text = PlanSummary(plan);
                            AppendLog(string.Format("解析完成：{0} 个文件，共 {1}。",
                                plan.Files.Count, Theme.Bytes(plan.TotalBytes)));
                        })).ConfigureAwait(false);
                    }
                }, ct).ConfigureAwait(true);

                if (summary == null) throw new InvalidOperationException("安装没有返回结果。");

                if (summary.Error != null)
                {
                    AppendLog("安装失败：" + summary.Error);
                    SetFooter("安装失败");
                    _pct.Text = "失败";
                    MessageBox.Show(this, summary.Error, "安装失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                var stats = summary.Stats;
                var firstFailure = summary.Failures.Count > 0 ? summary.Failures[0] : null;
                var versionLabel = summary.Plan != null && !string.IsNullOrEmpty(summary.Plan.Label)
                    ? summary.Plan.Label
                    : (summary.Plan != null ? summary.Plan.VersionId : versionId);

                AppendLog(string.Format("安装结束：下载 {0}，已存在 {1}，失败 {2}。",
                    stats.DoneFiles, stats.SkippedFiles, stats.FailedFiles));
                if (firstFailure != null) AppendLog("首个失败：" + firstFailure);

                _bar.Value = stats.FailedFiles > 0 ? _bar.Value : 1000;
                _pct.Text = stats.FailedFiles > 0 ? "未完成" : "100%";
                _speed.Text = stats.FailedFiles > 0 ? stats.FailedFiles + " 个文件失败" : "已完成";
                _file.Text = stats.FailedFiles > 0
                    ? "部分文件失败，可再次点击「开始安装」重试（已完成的文件会自动跳过）"
                    : "全部文件已就绪";
                SetFooter(stats.FailedFiles > 0 ? "安装未完成" : "安装完成 · " + versionLabel);

                AppendLog("完成，可点击「打开目录」查看结果。");

                // 下载阶段已经逐个校验过 SHA1，这里复核的是“落盘之后”有没有被改写：
                // 杀软隔离、磁盘问题、别人手动替换。失败只提示，绝不删除文件。
                ShowVerification(summary.Verification, "安装完成");
            }
            catch (OperationCanceledException)
            {
                AppendLog("已取消安装。");
                SetFooter("已取消");
                _pct.Text = "已取消";
            }
            catch (Exception ex)
            {
                AppendLog("安装失败：" + ex.Message);
                if (ex is UnauthorizedAccessException)
                    AppendLog("提示：当前账号无法写入该安装目录，请换一个目录（例如换到其它盘的游戏目录）或检查目录权限。");
                SetFooter("安装失败");
                _pct.Text = "失败";
                MessageBox.Show(this, ex.Message, "安装失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (_cts != null) { _cts.Dispose(); _cts = null; }
                SetBusyUi(false, null);
            }
        }

        /// <summary>
        /// 把校验结果落到日志与弹窗：干净就一行通过，有问题就逐条列并提示，绝不删除文件。
        /// 安装结束与手动点「校验文件」共用这一段，避免两条路径给出不一致的说法。
        /// </summary>
        private void ShowVerification(VerificationReport verification, string cleanFooter)
        {
            if (verification == null) return;

            if (verification.Clean)
            {
                AppendLog("校验通过：" + verification.Describe());
                SetFooter(cleanFooter);
                _file.Text = "全部文件与官方哈希一致";
                return;
            }

            AppendLog("校验失败：" + verification.Describe() + "（文件未被删除，建议重新下载或检查磁盘/杀毒软件）。");
            for (var i = 0; i < verification.Problems.Count && i < 5; i++)
            {
                var problem = verification.Problems[i];
                AppendLog(string.Format("  {0}：{1}（{2}）",
                    problem.Status == FileCheckStatus.Missing ? "缺失" : "哈希不符",
                    problem.Label ?? problem.Path, problem.Path));
            }
            if (verification.Problems.Count > 5)
                AppendLog("  …还有 " + (verification.Problems.Count - 5) + " 个问题文件，详见本次日志。");

            SetFooter(cleanFooter + "，但有文件校验失败");
            _file.Text = "有文件校验失败，文件保留未删除";
            MessageBox.Show(this,
                verification.Describe() + Environment.NewLine + Environment.NewLine +
                "这些文件没有被删除或改写。可以重新点击「开始安装」补齐或重新下载，" +
                "若反复出现，请检查磁盘或杀毒软件是否在改动下载的文件。",
                "文件校验未通过", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private async void OnVerifyFiles(object sender, EventArgs e)
        {
            var plan = _plan;
            if (plan == null)
            {
                AppendLog("还没有可校验的文件清单：请先点「开始安装」解析一次。");
                return;
            }

            _verify.Enabled = false;
            _file.Text = "正在校验已下载的文件…";
            _pct.Text = "校验中";
            try
            {
                var count = plan.Files.Count;
                var progress = new Progress<int>(done =>
                {
                    if (done >= count) return;
                    _file.Text = "已校验 " + done + " / " + count;
                });

                var report = await Task.Run(() => InstallVerifier.VerifyAsync(plan.Files, progress, CancellationToken.None))
                    .ConfigureAwait(true);

                _file.Text = report.Clean ? "全部文件与官方哈希一致" : "有文件校验失败，文件保留未删除";
                if (report.Clean) _pct.Text = "校验通过"; else _pct.Text = "校验未通过";
                ShowVerification(report, "校验完成 · " + (plan.Label ?? plan.VersionId));
            }
            catch (Exception ex)
            {
                AppendLog("校验失败：" + ex.Message);
            }
            finally
            {
                _verify.Enabled = !_busy && _plan != null;
            }
        }

        private void OnStats(TransferStats stats)
        {
            if (stats == null) return;

            var clamped = (int)Math.Max(0, Math.Min(100, stats.Percent)) * 10;
            if (clamped >= _bar.Minimum && clamped <= _bar.Maximum) _bar.Value = clamped;

            _pct.Text = stats.Percent.ToString("0.0") + "%";
            _speed.Text = string.Format("{0} / {1}   ·   {2}/s",
                Theme.Bytes(stats.DoneBytes), Theme.Bytes(stats.TotalBytes),
                Theme.Bytes((long)stats.BytesPerSecond));

            _file.Text = stats.CurrentFile ?? string.Empty;
        }

        // ------------------------------------------------------------- plumbing

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_cts != null) _cts.Cancel();
            _settings.Save();
        }

        private void SetBusyUi(bool busy, string footer)
        {
            var topIndex = _list.TopIndex;
            _busy = busy;

            _start.Enabled = !busy;
            _refresh.Enabled = !busy;
            _list.Enabled = !busy;
            _typeFilter.Enabled = !busy;
            _search.Enabled = !busy;
            _threads.Enabled = !busy;
            _withAssets.Enabled = !busy;
            _browse.Enabled = !busy;
            _openPack.Enabled = !busy;
            _verify.Enabled = !busy && _plan != null;
            _root.Enabled = !busy;
            _cancel.Enabled = busy;
            _start.Text = busy ? "安装中…" : "开始安装";

            Cursor = busy ? Cursors.AppStarting : Cursors.Default;
            if (footer != null) SetFooter(footer);

            // Re-enabling a list box can reset its scroll offset; keep the selected row in view.
            if (!busy && topIndex >= 0 && _list.Items.Count > 0)
            {
                _list.TopIndex = Math.Min(topIndex, _list.Items.Count - 1);
                if (_list.SelectedIndex >= 0 && _list.SelectedIndex < _list.TopIndex)
                    _list.TopIndex = _list.SelectedIndex;
            }
        }

        private void SetFooter(string text)
        {
            _footerLabel.Text = text ?? string.Empty;
        }

        private void AppendLog(string message)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(AppendLog), message); return; }

            _log.AppendText(string.Format("[{0}] {1}{2}", DateTime.Now.ToString("HH:mm:ss"), message, Environment.NewLine));
            if (_log.TextLength > 200000) _log.Text = _log.Text.Substring(_log.TextLength - 120000);
            _log.SelectionStart = _log.TextLength;
            _log.ScrollToCaret();
        }

        private void Ui(Action action)
        {
            if (InvokeRequired) { BeginInvoke(action); return; }
            action();
        }
    }
}
