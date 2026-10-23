using System;
using Microsoft.Win32;

namespace McDl.App.Design
{
    /// <summary>
    /// 主题状态的唯一来源：解析浅色/深色/跟随系统，并把"系统到底偏哪边"这件事
    /// 收在一个地方。读不到系统设置时退回浅色，不抛异常、不阻塞启动。
    /// </summary>
    public static class ThemeManager
    {
        private static ThemeMode _mode = ThemeMode.System;
        private static bool _systemPrefersDark;
        private static Palette _palette = Palette.Light;
        private static bool _hooked;

        /// <summary>配色变化（切换模式或系统设置变化）。在 UI 线程触发。</summary>
        public static event EventHandler Changed;

        public static ThemeMode Mode
        {
            get { return _mode; }
        }

        public static Palette Palette
        {
            get { return _palette; }
        }

        /// <summary>系统应用主题是否偏深色；读不到时返回 false。</summary>
        public static bool SystemPrefersDark
        {
            get { return _systemPrefersDark; }
        }

        public static void Initialize(ThemeMode mode)
        {
            _systemPrefersDark = DetectSystemPrefersDark();
            _mode = mode;
            _palette = Palette.For(_mode, _systemPrefersDark);

            if (_hooked) return;
            _hooked = true;
            try
            {
                SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            }
            catch
            {
                // 极少数受限账户下挂不上系统事件：跟随系统就固定为启动时的判断。
            }
        }

        public static void Apply(ThemeMode mode)
        {
            _mode = mode;
            _systemPrefersDark = DetectSystemPrefersDark();
            _palette = Palette.For(_mode, _systemPrefersDark);
            RaiseChanged();
        }

        private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General && e.Category != UserPreferenceCategory.VisualStyle) return;

            var prefersDark = DetectSystemPrefersDark();
            if (prefersDark == _systemPrefersDark) return;

            _systemPrefersDark = prefersDark;
            _palette = Palette.For(_mode, _systemPrefersDark);
            RaiseChanged();
        }

        private static void RaiseChanged()
        {
            var handler = Changed;
            if (handler == null) return;

            // 逐个订阅者调用：某个控件重绘失败不能连累后面的控件停在旧配色。
            var callbacks = handler.GetInvocationList();
            foreach (var callback in callbacks)
            {
                try
                {
                    callback.DynamicInvoke(null, EventArgs.Empty);
                }
                catch
                {
                    // 主题切换不应把界面带回崩溃对话框。
                }
            }
        }

        /// <summary>读注册表；任何异常都当作"偏浅色"。</summary>
        public static bool DetectSystemPrefersDark()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key == null) return false;
                    var value = key.GetValue("AppsUseLightTheme");
                    if (value is int) return (int)value == 0;
                }
            }
            catch
            {
                // 无权限或旧系统：保守用浅色。
            }
            return false;
        }
    }
}

