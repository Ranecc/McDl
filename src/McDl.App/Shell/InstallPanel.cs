using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using McDl.App.Design;
using McDl.Core;

namespace McDl.App.Shell
{
    /// <summary>
    /// 下载页共用的右侧面板：版本/整合包信息、安装参数、进度、日志、四个操作按钮。
    /// 原版页和整合包页各自只负责"当前选中了什么"，其余行为都在这里。
    /// </summary>
    public sealed class InstallPanel : UiCard
    {
        private static int Pad { get { return Metrics.Px(24); } }

        private readonly Settings _settings;

        private string _title = string.Empty;
        private string _meta = string.Empty;
        private string _stats = string.Empty;
        private string _fileLine = string.Empty;
        private string _percentText = "0%";
        private bool _busy;

        private readonly UiField _root;
        private readonly UiButton _browse;
        private readonly UiNumber _threads;
        private readonly UiCheck _assets;
        private readonly UiProgress _bar;
        private readonly UiButton _start;
        private readonly UiButton _cancel;
        private readonly UiButton _verify;
        private readonly UiButton _open;
        private readonly UiCard _logCard;
        private readonly TextBox _log;
        private readonly Label _logHint;

        // 布局结果：OnPaint 只负责按这些坐标画文字，不做计算。
        private Rectangle _rootRect;
        private Rectangle _browseRect;
        private Rectangle _threadsRect;
        private Rectangle _assetsRect;
        private Rectangle _barRect;
        private Rectangle _percentRect;
        private int _dividerY;
        private int _labelRootY;
        private int _labelThreadsY;
        private int _buttonsY;
        private int _logHeaderY;

        private CancellationTokenSource _cts;

        private static readonly List<InstallPanel> Live = new List<InstallPanel>();

        /// <summary>窗口关闭时把仍在跑的安装/校验停下来。</summary>
        public static void CancelAll()
        {
            InstallPanel[] snapshot;
            lock (Live) snapshot = Live.ToArray();
            foreach (var panel in snapshot) panel.CancelWork();
        }
        private InstallPlan _plan;

        public InstallPanel(Settings settings)
        {
            lock (Live) Live.Add(this);

            _settings = settings;
            Padding = new Padding(0);

            _root = new UiField { Text = settings.Root, Placeholder = "安装目录" };
            _browse = new UiButton { Text = "浏览…", Style = ButtonStyle.Secondary };
            _browse.Click += OnBrowse;

            _threads = new UiNumber { Value = settings.Threads, Minimum = 1, Maximum = 32 };
            _assets = new UiCheck { Text = "下载资源文件（assets）", Checked = settings.WithAssets };

            _bar = new UiProgress();

            _start = new UiButton { Text = "开始安装", Style = ButtonStyle.Primary };
            _start.Click += OnStart;
            _cancel = new UiButton { Text = "取消", Style = ButtonStyle.Secondary, Enabled = false };
            _cancel.Click += OnCancel;
            _verify = new UiButton { Text = "校验文件", Style = ButtonStyle.Secondary, Enabled = false };
            _verify.Click += OnVerify;
            _open = new UiButton { Text = "打开目录", Style = ButtonStyle.Secondary };
            _open.Click += OnOpenFolder;

            _logCard = new UiCard
            {
                Alternate = true,
                ShowShadow = false,
                Radius = Metrics.RadiusControl,
                Padding = new Padding(0)
            };

            // 原生滚动条在自定义配色里很扎眼；隐藏后用滚轮翻，日志本身不多。
            _log = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                ScrollBars = ScrollBars.None,
                Font = Metrics.MonoSmall,
                WordWrap = false,
                TabStop = false
            };

            _logHint = new Label
            {
                Text = "还没有日志。安装和校验的进展会显示在这里。",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = Metrics.Small,
                BackColor = Color.Transparent
            };

            _logCard.Controls.Add(_logHint);
            _logCard.Controls.Add(_log);

            Controls.Add(_root);
            Controls.Add(_browse);
            Controls.Add(_threads);
            Controls.Add(_assets);
            Controls.Add(_bar);
            Controls.Add(_start);
            Controls.Add(_cancel);
            Controls.Add(_verify);
            Controls.Add(_open);
            Controls.Add(_logCard);

            ApplyTheme();
            ThemeManager.Changed += OnThemeChanged;
        }

        /// <summary>由页面提供：当前选中项对应的安装请求；没有选中时返回 null。</summary>
        public Func<InstallRequest> RequestFactory { get; set; }

        /// <summary>页脚状态文字（由外壳展示）。</summary>
        public event Action<string> Footer;

        public bool IsBusy
        {
            get { return _busy; }
        }

        public void SetHeader(string title, string meta, string stats)
        {
            _title = title ?? string.Empty;
            _meta = meta ?? string.Empty;
            _stats = stats ?? string.Empty;
            PerformLayout();
            Invalidate();
        }

        public void ResetProgress()
        {
            _bar.Value = 0;
            _percentText = "0%";
            _fileLine = string.Empty;
            Invalidate();
        }

        public void AppendLog(string message)
        {
            if (string.IsNullOrEmpty(message)) return;

            var line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message + Environment.NewLine;
            _log.AppendText(line);

            if (_log.TextLength > 200000)
            {
                _log.Text = _log.Text.Substring(_log.TextLength - 120000);
            }

            _log.SelectionStart = _log.TextLength;
            _log.ScrollToCaret();
            _logHint.Visible = _log.TextLength == 0;
        }

        private void RaiseFooter(string text)
        {
            var handler = Footer;
            if (handler != null) handler(text);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (_root == null || _logCard == null) return;

            var inner = Math.Max(120, Width - Pad * 2);
            var y = Pad;

            if (_title.Length > 0) y += Metrics.Px(32);
            if (_meta.Length > 0) y += Metrics.Px(21);
            if (_stats.Length > 0) y += Metrics.Px(24);
            else if (_title.Length == 0) y += Metrics.Px(24);

            _dividerY = y + Metrics.Px(4);
            y = _dividerY + Metrics.Px(16);

            _labelRootY = y;
            y += Metrics.Px(20);

            _rootRect = new Rectangle(Pad, y, Math.Max(80, inner - Metrics.Px(96) - Metrics.Px(8)), Metrics.InputHeight);
            _browseRect = new Rectangle(Pad + inner - Metrics.Px(96), y, Metrics.Px(96), Metrics.InputHeight);
            y += Metrics.InputHeight + Metrics.Px(16);

            _labelThreadsY = y + Metrics.Px(9);
            _threadsRect = new Rectangle(Pad + Metrics.Px(84), y, Metrics.Px(96), Metrics.InputHeight);
            _assetsRect = new Rectangle(Pad + Metrics.Px(84) + Metrics.Px(96) + Metrics.Px(18), y + Metrics.Px(7), Math.Max(120, inner - Metrics.Px(84) - Metrics.Px(96) - Metrics.Px(18)), Metrics.Px(22));
            y += Metrics.InputHeight + Metrics.Px(16);

            _barRect = new Rectangle(Pad, y, inner, Metrics.Px(8));
            y += Metrics.Px(18);
            _percentRect = new Rectangle(Pad, y, inner, Metrics.Px(22));
            y += Metrics.Px(26);

            _buttonsY = y;
            _start.SetBounds(Pad, _buttonsY, Metrics.Px(150), Metrics.ButtonHeight);
            _cancel.SetBounds(Pad + Metrics.Px(166), _buttonsY, Metrics.Px(108), Metrics.ButtonHeight);
            _open.SetBounds(Width - Pad - Metrics.Px(152), _buttonsY, Metrics.Px(152), Metrics.ButtonHeight);
            var verifyRight = Math.Min(_open.Left - Metrics.Px(16), Width - Pad);
            var verifyLeft = Math.Max(_cancel.Right + Metrics.Px(16), verifyRight - Metrics.Px(118));
            _verify.SetBounds(verifyLeft, _buttonsY, Math.Max(0, verifyRight - verifyLeft), Metrics.ButtonHeight);
            y += Metrics.ButtonHeight + Metrics.Px(18);

            _logHeaderY = y;
            y += Metrics.Px(26);

            var logHeight = Math.Max(90, Height - Pad - y);
            _logCard.SetBounds(Pad, y, inner, logHeight);
            var inset = Metrics.Px(12);
            _log.SetBounds(inset, inset, Math.Max(20, inner - inset * 2 - 2), Math.Max(20, logHeight - inset * 2));
            _logHint.SetBounds(inset + Metrics.Px(2), inset, Math.Max(20, inner - inset * 2 - Metrics.Px(8)), Metrics.Px(22));

            _root.SetBounds(_rootRect.X, _rootRect.Y, _rootRect.Width, _rootRect.Height);
            _browse.SetBounds(_browseRect.X, _browseRect.Y, _browseRect.Width, _browseRect.Height);
            _threads.SetBounds(_threadsRect.X, _threadsRect.Y, _threadsRect.Width, _threadsRect.Height);
            _assets.SetBounds(_assetsRect.X, _assetsRect.Y, _assetsRect.Width, _assetsRect.Height);
            _bar.SetBounds(_barRect.X, _barRect.Y, _barRect.Width, _barRect.Height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var palette = ThemeManager.Palette;
            var g = e.Graphics;
            Surface.SetQuality(g, palette.IsDark);

            var inner = Math.Max(120, Width - Pad * 2);
            var y = Pad;

            if (_title.Length > 0)
            {
                Surface.Text(g, _title, Metrics.Hero, palette.Text, new RectangleF(Pad, y, inner, Metrics.Px(30)));
                y += Metrics.Px(32);
            }

            if (_meta.Length > 0)
            {
                Surface.Text(g, _meta, Metrics.Body, palette.TextMuted, new RectangleF(Pad, y, inner, Metrics.Px(20)));
                y += Metrics.Px(21);
            }

            if (_stats.Length > 0)
            {
                Surface.Text(g, _stats, Metrics.Body, palette.Text, new RectangleF(Pad, y, inner, Metrics.Px(20)));
            }
            else if (_title.Length == 0)
            {
                Surface.Text(g, "从左侧选择版本或整合包，然后点「开始安装」。", Metrics.Body, palette.TextMuted,
                    new RectangleF(Pad, y, inner, Metrics.Px(20)));
            }

            using (var pen = new Pen(palette.Border))
            {
                g.DrawLine(pen, Pad, _dividerY, Width - Pad, _dividerY);
            }

            Surface.Text(g, "安装目录", Metrics.Small, palette.TextMuted, new RectangleF(Pad, _labelRootY, inner, Metrics.Px(18)));
            Surface.Text(g, "并发线程", Metrics.Small, palette.TextMuted, new RectangleF(Pad, _labelThreadsY, Metrics.Px(80), Metrics.Px(18)));

            if (_title.Length > 0)
            {
                Surface.Text(g, _percentText, Metrics.Section, palette.Text, _percentRect);

                if (_fileLine.Length > 0)
                {
                    Surface.Text(g, _fileLine, Metrics.MonoSmall, palette.TextMuted, _percentRect,
                        ContentAlignment.MiddleRight);
                }
            }

            Surface.Text(g, "日志", Metrics.Section, palette.Text, new RectangleF(Pad, _logHeaderY, inner - Metrics.Px(240), Metrics.Px(22)));
            Surface.Text(g, "安装结束后自动复核一遍落盘文件", Metrics.Tiny, palette.TextMuted,
                new RectangleF(Width - Pad - Metrics.Px(240), _logHeaderY + Metrics.Px(2), Metrics.Px(240), Metrics.Px(20)), ContentAlignment.MiddleRight);
        }

        private void ApplyTheme()
        {
            var palette = ThemeManager.Palette;
            _log.BackColor = palette.SurfaceAlt;
            _log.ForeColor = palette.IsDark ? palette.Text : Color.FromArgb(48, 56, 66);
            _logHint.ForeColor = palette.TextMuted;
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            if (IsDisposed) return;

            var palette = ThemeManager.Palette;
            _log.BackColor = palette.SurfaceAlt;
            _log.ForeColor = palette.IsDark ? palette.Text : Color.FromArgb(48, 56, 66);
            _logHint.ForeColor = palette.TextMuted;
            Invalidate(true);
        }

        /// <summary>窗口关闭时调用：把正在跑的安装/校验停下来。</summary>
        public void CancelWork()
        {
            var cts = _cts;
            if (cts == null) return;
            try { cts.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                CancelWork();
                lock (Live) Live.Remove(this);
            }
            if (disposing) ThemeManager.Changed -= OnThemeChanged;
            base.Dispose(disposing);
        }

        private void OnBrowse(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择安装目录（.minecraft）";
                dialog.SelectedPath = Directory.Exists(_root.Text) ? _root.Text : _settings.Root;
                if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;

                _root.Text = dialog.SelectedPath;
                _settings.Root = dialog.SelectedPath;
                _settings.Save();
                RaiseFooter("已切换安装目录");
            }
        }

        private async void OnStart(object sender, EventArgs e)
        {
            if (_busy) return;

            var factory = RequestFactory;
            var request = factory == null ? null : factory();
            if (request == null)
            {
                AppendLog("还没有选中版本或整合包。");
                return;
            }

            var target = _root.Text.Trim();
            if (target.Length == 0)
            {
                AppendLog("请先填写安装目录。");
                return;
            }

            request.Root = target;
            request.IncludeAssets = _assets.Checked;

            _settings.Root = target;
            _settings.Threads = _threads.Value;
            _settings.WithAssets = _assets.Checked;
            _settings.Save();

            SetBusy(true);
            ResetProgress();
            _plan = null;

            using (var cts = new CancellationTokenSource())
            {
                _cts = cts;
                var progress = new Progress<TransferStats>(OnProgress);

                try
                {
                    using (var installer = new Installer(MirrorRouter.CreateDefault(), _threads.Value))
                    {
                        installer.Log = line => Post(() => AppendLog(line));

                        var summary = await installer.InstallAsync(request, progress, cts.Token, plan => Post(() =>
                        {
                            _plan = plan;
                            _verify.Enabled = !_busy;
                            _stats = DescribePlan(plan);
                            PerformLayout();
                            Invalidate();
                        }));

                        HandleSummary(summary);
                    }
                }
                catch (OperationCanceledException)
                {
                    AppendLog("已取消安装。");
                    RaiseFooter("已取消");
                }
                catch (Exception ex)
                {
                    AppendLog("安装失败：" + ex.Message);
                    RaiseFooter("安装失败");
                    if (ex is UnauthorizedAccessException)
                    {
                        AppendLog("提示：当前账号无法写入该目录，请换一个目录或检查权限。");
                    }
                }
                finally
                {
                    _cts = null;
                    SetBusy(false);
                }
            }
        }

        private void HandleSummary(InstallSummary summary)
        {
            if (summary == null) return;

            if (summary.Error != null)
            {
                AppendLog("安装失败：" + summary.Error);
                RaiseFooter("安装失败");
                return;
            }

            var stats = summary.Stats;
            if (stats != null)
            {
                AppendLog(string.Format("安装结束：下载 {0}，已存在 {1}，失败 {2}。",
                    stats.DoneFiles, stats.SkippedFiles, stats.FailedFiles));
            }

            if (summary.Failures != null && summary.Failures.Count > 0)
            {
                foreach (var failure in summary.Failures) AppendLog("失败：" + failure);
            }

            ShowVerification(summary.Verification, "安装完成");
        }

        private static string DescribePlan(InstallPlan plan)
        {
            if (plan == null) return string.Empty;

            var parts = plan.Files.Count + " 个文件 · " + Format.Bytes(plan.TotalBytes);
            if (!string.IsNullOrEmpty(plan.AssetIndexId)) parts += " · 资源索引 " + plan.AssetIndexId;
            if (plan.Natives.Count > 0) parts += " · 解压 " + plan.Natives.Count + " 个 natives";
            return parts;
        }

        private void OnProgress(TransferStats stats)
        {
            if (stats == null) return;

            _bar.Value = stats.Percent / 100.0;
            _percentText = ((int)Math.Round(stats.Percent)).ToString() + "%";
            _fileLine = stats.CurrentFile ?? string.Empty;
            Invalidate();
        }

        private void OnCancel(object sender, EventArgs e)
        {
            var cts = _cts;
            if (cts == null) return;
            AppendLog("正在取消…");
            try
            {
                cts.Cancel();
            }
            catch
            {
                // 已经释放：忽略。
            }
        }

        private void OnOpenFolder(object sender, EventArgs e)
        {
            var target = _root.Text.Trim();
            try
            {
                if (Directory.Exists(target)) Process.Start("explorer.exe", "\"" + target + "\"");
                else AppendLog("目录还不存在：" + target);
            }
            catch (Exception ex)
            {
                AppendLog("打开目录失败：" + ex.Message);
            }
        }

        private async void OnVerify(object sender, EventArgs e)
        {
            if (_busy) return;

            var plan = _plan;
            if (plan == null)
            {
                AppendLog("先安装一次再校验。");
                return;
            }

            var cts = new CancellationTokenSource();
            _cts = cts;
            SetBusy(true);
            AppendLog("正在校验已安装的文件…");

            try
            {
                var progress = new Progress<int>(done =>
                {
                    _fileLine = "已校验 " + done + " 个文件";
                    if (_bar != null && plan.Files != null && plan.Files.Count > 0)
                    {
                        _bar.Value = Math.Min(1d, (double)done / Math.Max(1, plan.Files.Count));
                    }
                    Invalidate();
                });
                var report = await Task.Run(() => InstallVerifier.VerifyAsync(plan.Files, progress, cts.Token));
                ShowVerification(report, "校验完成");
            }
            catch (OperationCanceledException)
            {
                AppendLog("已取消校验。");
                RaiseFooter("已取消校验");
            }
            catch (Exception ex)
            {
                AppendLog("校验失败：" + ex.Message);
            }
            finally
            {
                _cts = null;
                cts.Dispose();
                SetBusy(false);
                Invalidate();
            }
        }

        private void ShowVerification(VerificationReport report, string cleanFooter)
        {
            if (report == null) return;

            if (report.Clean)
            {
                AppendLog("校验通过：" + report.Describe());
                RaiseFooter(cleanFooter);
                _fileLine = "全部文件与官方哈希一致";
                Invalidate();
                return;
            }

            AppendLog("校验失败：" + report.Describe() + "（文件未被删除，建议重新下载或检查磁盘/杀毒软件）");

            var shown = 0;
            foreach (var problem in report.Problems)
            {
                if (shown++ >= 5) break;
                var kind = problem.Status == FileCheckStatus.Missing ? "缺失" : "哈希不符";
                AppendLog(kind + "：" + problem.Label);
            }

            if (report.Problems.Count > 5) AppendLog("…还有 " + (report.Problems.Count - 5) + " 个");

            _fileLine = "有文件校验失败，文件保留未删除";
            Invalidate();
            RaiseFooter("校验未通过");

            MessageBox.Show(FindForm(),
                report.Describe() + "\r\n\r\n文件没有被删除，可以重新点「开始安装」补齐或覆盖。\r\n明细见日志。",
                "文件校验未通过", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _start.Enabled = !busy;
            _cancel.Enabled = busy;
            _browse.Enabled = !busy;
            _threads.Enabled = !busy;
            _assets.Enabled = !busy;
            _root.Enabled = !busy;
            _verify.Enabled = !busy && _plan != null;
            _start.Text = busy ? "安装中…" : "开始安装";
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
                // 窗口正在销毁：丢弃这条 UI 更新。
            }
        }
    }
}









