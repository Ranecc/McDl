# 界面

界面代码在 `src\McDl.App`，全是 WinForms 自绘。这篇讲窗口怎么搭、配色和尺寸从哪里改、高 DPI 与主题切换怎么走，最后是几个容易踩的坑。分层和安装流程看 [architecture.md](architecture.md)。

## 一个窗口，三个页面

`ShellWindow` 是无边框窗口，`FormBorderStyle.None`，自己画标题栏、左侧导航和底部状态栏。窗口尺寸 1180×740（96 DPI 下），最小 900×620，按 `Metrics.Scale` 换算成像素。

| 导航项 | 实现 | 职责 |
| --- | --- | --- |
| 原版下载 | `Pages\VanillaPage.cs` | 版本列表、搜索与正式版/快照过滤；右栏安装面板 |
| 整合包 | `Pages\PacksPage.cs` | 选择或拖入整合包，显示识别出的格式、版本、加载器；右栏安装面板 |
| 设置 | `Pages\SettingsPage.cs` | 配色、默认安装目录、并发线程、assets；单张居中卡片 |

三个页面都是 `UserControl`，`Dock = Fill`，由 `ShellWindow.ShowPage` 切换显示。两个下载页各自持有一个 `InstallPanel` 放在右栏，构造时把自己的 `BuildRequest` 挂到面板的 `RequestFactory` 上——面板只认「给我一个安装请求」，不关心请求来自哪一页。

加第四个页面：新建 `UserControl`，在构造里设 `SetStyle(... | ControlStyles.SupportsTransparentBackColor, true)` 和 `BackColor = Color.Transparent`，塞进 `_pages` 数组，再给 `_nav` 加一项、在 `ShowPage` 的 switch 里接上。标题和卡片自己画；只有必须用原生控件的地方（比如「正在获取版本列表…」）才用 `Label`，并记得在主题变化时改它的 `ForeColor`。

## 页面怎么排

`VanillaPage` 和 `PacksPage` 是同一套骨架：左边一张卡（默认 320 宽，取 `min(320, max(240, 宽 / 3))`），右边 `InstallPanel`，外边距 `Metrics.PagePad`，两栏间隔 `Metrics.Gap`。

- 页标题：`Hero` 字号，左上角 `(PagePad, 6)`，高约 36。
- 副标题：`Small` + `TextMuted`，`(PagePad, 44)`，一句话，例如「从 Mojang 官方清单下载任意版本」。不要写第二行。
- 卡片内部留白 `Metrics.PadCard`。

`SettingsPage` 只有一张卡，宽 `min(620, max(320, 宽 - 2 * PagePad))`，水平居中，高 `max(360, 高 - 76 - 16)`。卡片底部是「关于」区，放版本号、镜像顺序和许可说明——空间不够时靠 `aboutY = max(保存按钮下沿 + 24, 卡高 - 24 - 96)` 让两者不重叠。

## 设计 token

只改 `Design\Tokens.cs`，不要在控件里散落色值和尺寸。

**Palette** 是角色名，不是颜色名。浅色与深色各一套，控件永远取角色：

| 角色 | 用在哪 |
| --- | --- |
| `Backdrop` | 窗口底色（`ShellWindow` 客户区） |
| `Surface` / `SurfaceAlt` | 卡片面 / 次级面（输入框、日志区） |
| `Border` / `BorderStrong` | 分隔线、卡片描边 / 控件 hover 描边 |
| `Text` / `TextMuted` | 正文 / 标签、副标题、日志提示 |
| `Accent` / `AccentHover` / `AccentPressed` | 主按钮三态、选中标记 |
| `AccentSoft` | 选中行、分段控件的底 |
| `OnAccent` | 压在 Accent 实心块上的文字与图标（浅色主题是白，深色主题是近黑） |
| `Track` | 进度条底槽 |
| `Danger` | 失败、关闭按钮 hover |
| `Shadow` / `Glow` | 投影 / 悬停发光 |

需要两色之间过渡时用 `Surface.Blend(a, b, amount)`，不要手写插值。

**Metrics** 是尺寸和字号的唯一入口：

| 项 | 值（96 DPI） |
| --- | --- |
| `RadiusCard` / `RadiusControl` / `RadiusSmall` | 10 / 8 / 6 |
| `PagePad` / `Gap` / `PadCard` / `PadControl` | 24 / 16 / 24 / 14 |
| `ButtonHeight` / `CompactButtonHeight` / `CompactButtonWidth` | 40 / 32 / 84 |
| `InputHeight` / `RowHeight` | 36 / 56 |
| `TitleBarHeight` / `NavWidth` | 48 / 176 |

字号：`Hero` 26、`Title` 21、`Section` 15、`Body` 13、`BodyStrong` 13、`Small` 12、`Tiny` 11、`Button` 14、`MonoSmall`（Consolas）13，单位是像素。字体是 `Metrics.Ui(像素, 样式)` 按 `Scale` 乘出来的，已经含缩放。

## 主题切换

启动时 `Program.Main` 先 `ApplyUiScale()`、`Settings.Load()`，再 `ThemeManager.Initialize(settings.ThemeMode)`，最后才 `Application.Run(new ShellWindow(...))`。运行中在设置页切换：

```
SettingsPage.ApplyTheme → settings.ThemeMode 落盘
        → ThemeManager.Apply(mode) → 重读系统偏好 → Changed
        → ShellWindow 换窗口底色、重设 DWM 背景、Invalidate(true)
        → 自绘控件下一次 OnPaint 从 ThemeManager.Palette 取新色
```

规则：

- 自绘控件**不缓存**颜色，`OnPaint` 里现取 `ThemeManager.Palette`。
- 原生控件（日志框等）不会自己变，必须显式改 `BackColor` / `ForeColor`，参考 `InstallPanel.ApplyTheme`，并且在 `Dispose` 时退订 `Changed`，否则窗口关了还被回调。
- 「跟随系统」读 `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme`。读不到、注册表被锁、非 Windows 10/11，一律按浅色处理；`SystemEvents` 挂不上就固定启动时的判断。
- 注册表变化只在 `General` / `VisualStyle` 两个类别下处理，并且偏好没真的翻转就不重绘。

## 高 DPI

`app.manifest` 里声明了 `dpiAware=true`（系统 DPI 感知），窗口不再被系统当位图拉伸。`Program.ApplyUiScale` 取 `Graphics.FromHwnd(IntPtr.Zero).DpiX / 96`，写进 `Metrics.Scale`。

- 新控件里所有几何都走 `Metrics.Px()`（取整）或 `Pxf()`（保留两位），不要写裸数字。现存代码里还有几处没清干净（`PacksPage` 的卡片内边距是裸 18），别照抄。
- 字体已经乘过 `Scale`，不要再乘第二次。
- 调试时可以用 `MCDL_UI_SCALE=1.5` 强制缩放，只在启动读取一次。
- 壳的 `TitleBarHeight` / `NavWidth` / `FooterHeight` / `ResizeEdge` 现在也是 `Metrics.Px(...)` 出来的实例字段（原先是 const），改标题栏高度时直接改那一行即可。

## 背景与圆角

`Surface.Backdrop.Attach` 在窗口句柄创建后调用，按能力逐项加：

1. 圆角：`DWMWA_WINDOW_CORNER_PREFERENCE = DWMWCP_ROUND`。系统不支持时不设 `Region`——设了会把阴影切掉；只在连 DWM 圆角都没有的机器上用 12px 圆角 `Region` 兜底。
2. 深色标题栏：先试属性 20，失败退 19。
3. 背景材质：Win11 用 `DWMWA_SYSTEMBACKDROP_TYPE = DWMSBT_MAINWINDOW`（云母）。

亚克力是**故意关掉**的：在 Win11 26200 上 `ACCENT_ENABLE_ACRYLICBLURBEHIND` 会把着色层盖到客户区，浅色主题下文字几乎看不见。代码里留着 `TrySetAcrylic`，但不要随手打开。

调试时 `MCDL_NO_BACKDROP=1` 可关掉所有 DWM 效果，退成纯色窗口。

## 自绘的基础件

`Design\Surface.cs` 提供画图原语：`RoundRect` / `FillRound` / `StrokeRound`（真圆弧，不是切角）、`Shadow`（三层叠加，越外层越淡）、`Glow`（描边外扩 1px）、`Text`（`TextRenderer` 封装，对齐只支持 `MiddleLeft` / `MiddleCenter` / `MiddleRight` / `TopLeft`）。

拟物只落在按钮和卡片上：圆角 + 投影 + hover 发光 + 按下 1px 位移。输入框、列表、分段控件保持扁平，不给纹理和渐变。

`Surface.SetQuality` 固定 `AntiAlias` + `TextRenderingHint.AntiAlias`。这里**不用 ClearType**：在半透明、分层的窗口上，ClearType 会把字形边缘算成半透明像素，浅色主题下文字会发虚甚至消失。

## 几个坑

- **`UiField.OnLayout` 必须判空。** 构造期设 `Size`、挂父容器都会提前触发布局，这时 `_box` 可能还没赋值，不判就是 `NullReferenceException`。
- **字体是共享缓存，会被回收。** `Metrics.Scale` 变化时会 `DisposeFonts()`，所以不要把 `Metrics.Body` 存进字段长期持有，`OnPaint` 里现取；也不要 `Dispose` 这些字体。
- **`TextRenderer` 不吃 `Graphics` 变换。** `g.ScaleTransform` 对 `TextRenderer.DrawText` 无效，文字会按未缩放的位置画。要缩放就把字号做进字体（`Metrics.Ui(像素)`），或者改用 `g.DrawString` 并自己承担渲染质量差异。
- **自绘控件必须开双缓冲。** `SetStyle(AllPaintingInWmPaint | OptimizedDoubleBuffer | UserPaint | ResizeRedraw, true)`，漏一个就会闪。透明背景还要加 `SupportsTransparentBackColor`，并把 `BackColor` 设成 `Color.Transparent`，由父容器画底。
- **`Paint` 里 new 出来的 `Pen` / `Font` / `GraphicsPath` 要 `using`。** 每秒重绘几十次，泄漏会很快显形。
- **不要在构造函数里取 `CreateGraphics` 量文字。** 句柄还没建，拿到的度量不可靠；用 `TextRenderer.MeasureText`。
- **`InstallPanel` 的控件是在代码里建的**，`OnLayout` 里判断 `_root == null || _logCard == null` 提前返回，别假设字段一定就绪。
