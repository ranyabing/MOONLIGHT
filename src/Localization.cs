using System;
using System.Collections.Generic;
using System.Globalization;

namespace Moonlight
{
    public sealed class Localizer
    {
        private static readonly Dictionary<string, string[]> entries = new Dictionary<string, string[]>
        {
            { "app.name", new[] { "MOONLIGHT", "MOONLIGHT" } },
            { "schedule.enable", new[] { "Enable scheduled switching", "启用定时切换" } },
            { "time.light", new[] { "Light mode starts", "浅色开始时间" } },
            { "time.dark", new[] { "Dark mode starts", "深色开始时间" } },
            { "theme.windows", new[] { "Switch Windows theme", "切换 Windows 主题" } },
            { "theme.hint", new[] { "Apply the theme to Windows and apps that follow the system theme.", "同时切换 Windows 和跟随系统的应用主题。" } },
            { "startup", new[] { "Start with Windows", "开机启动" } },
            { "switch.light", new[] { "Switch to light", "切换浅色" } },
            { "switch.dark", new[] { "Switch to dark", "切换深色" } },
            { "schedule.resume", new[] { "Resume schedule", "恢复自动计划" } },
            { "save", new[] { "Save and apply", "保存并应用" } },
            { "hide", new[] { "Hide to tray", "收起到托盘" } },
            { "language", new[] { "Language", "语言" } },
            { "tray.toggle", new[] { "Toggle theme", "切换深浅色" } },
            { "tray.settings", new[] { "Settings", "设置" } },
            { "tray.exit", new[] { "Exit", "退出" } },
            { "mode.light", new[] { "Light", "浅色" } },
            { "mode.dark", new[] { "Dark", "深色" } },
            { "status.schedule", new[] { "Schedule enabled", "自动计划运行中" } },
            { "status.paused", new[] { "Schedule paused", "自动计划已暂停" } },
            { "status.override", new[] { "Manual theme until the next scheduled change", "手动模式保持到下一个切换点" } },
            { "status.disabled", new[] { "Windows theme switching is off", "Windows 主题切换已关闭" } },
            { "status.contrast", new[] { "Switching paused for high contrast", "高对比度模式下已暂停切换" } },
            { "status.next", new[] { "Next: {0} {1} · {2}", "下次：{0} {1} · {2}" } },
            { "today", new[] { "Today", "今天" } },
            { "tomorrow", new[] { "Tomorrow", "明天" } },
            { "saved", new[] { "Saved", "已保存" } },
            { "language.saved", new[] { "Language updated", "语言已切换" } },
            { "schedule.resumed", new[] { "Schedule resumed", "已恢复自动计划" } },
            { "switched", new[] { "Switched to {0}", "已切换为{0}" } },
            { "notice.manual", new[] { "The schedule resumes at the next change. Right-click the tray icon for settings.", "下个切换点恢复自动计划。右键托盘图标可打开设置。" } },
            { "notice.paused", new[] { "The schedule is paused. Double-click MOONLIGHT again to switch back.", "自动计划已暂停。再次双击 MOONLIGHT 即可切换回来。" } },
            { "notice.disabled", new[] { "Enable Switch Windows theme in Settings to change colors.", "请在设置中启用切换 Windows 主题。" } },
            { "notice.contrast", new[] { "Turn off the Windows high contrast theme to resume switching.", "关闭 Windows 高对比度主题后即可恢复切换。" } },
            { "error.action", new[] { "The action could not be completed. See the local log for details.", "操作未完成，详情请查看本地日志。" } },
            { "error.start", new[] { "MOONLIGHT could not start. Check that the app and its settings folder are writable.", "MOONLIGHT 无法启动，请检查程序和配置目录是否可写。" } },
            { "error.starting", new[] { "MOONLIGHT is starting. Try again in a moment.", "MOONLIGHT 正在启动，请稍后再试。" } },
            { "error.argument", new[] { "The command-line arguments are invalid.", "命令行参数无效。" } },
            { "validation.version", new[] { "This settings version is not supported.", "不支持这个配置文件版本。" } },
            { "validation.time", new[] { "Choose a time between 00:00 and 23:59.", "切换时间必须在 00:00 到 23:59 之间。" } },
            { "validation.equalTimes", new[] { "Light and dark start times must be different.", "浅色和深色的开始时间不能相同。" } },
            { "validation.language", new[] { "Choose English or Simplified Chinese.", "请选择英文或简体中文。" } },
            { "validation.override", new[] { "The saved manual theme is invalid.", "保存的手动模式无效。" } },
            { "validation.expiry", new[] { "The saved manual theme end time is invalid.", "保存的手动模式结束时间无效。" } },
            { "validation.empty", new[] { "The settings file is empty.", "配置文件为空。" } },
            { "validation.scope", new[] { "Select a theme to switch.", "请选择要切换的主题。" } },
            { "validation.path", new[] { "The app path is invalid.", "程序路径无效。" } },
            { "validation.longPath", new[] { "Move MOONLIGHT to a shorter path before enabling startup.", "请先把 MOONLIGHT 放到较短的路径，再启用开机启动。" } },
            { "warning.missing", new[] { "The settings file was missing and has been restored from backup.", "配置文件缺失，已从备份恢复。" } },
            { "warning.backup", new[] { "The damaged settings file was kept and the backup was restored.", "已保留损坏的配置文件，并从备份恢复。" } },
            { "warning.defaults", new[] { "The damaged settings file was kept and defaults were restored.", "已保留损坏的配置文件，并恢复默认设置。" } }
        };

        public string Language { get; private set; }
        public Localizer(string language) { SetLanguage(language); }
        public void SetLanguage(string language)
        {
            if (language != "en" && language != "zh-CN") throw new ArgumentException("validation.language");
            Language = language;
        }
        public string this[string key]
        {
            get
            {
                string[] pair;
                if (!entries.TryGetValue(key, out pair)) throw new ArgumentException("Unknown text key: " + key);
                return pair[Language == "zh-CN" ? 1 : 0];
            }
        }
        public string Format(string key, params object[] args)
        {
            return string.Format(CultureInfo.InvariantCulture, this[key], args);
        }
        public string Mode(ThemeMode mode) { return this[mode == ThemeMode.Light ? "mode.light" : "mode.dark"]; }
        public static string ErrorKey(Exception error) { return entries.ContainsKey(error.Message) ? error.Message : "error.action"; }
        public string Error(Exception error) { return this[ErrorKey(error)]; }
        public static IEnumerable<string> Keys { get { return entries.Keys; } }
    }
}
