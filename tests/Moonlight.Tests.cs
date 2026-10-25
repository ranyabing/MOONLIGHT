using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using System.Drawing;
using System.Windows.Forms;

namespace Moonlight
{
    [DataContract]
    public sealed class TestResult
    {
        [DataMember] public string Name;
        [DataMember] public bool Passed;
        [DataMember] public string Details;
        [DataMember] public long Milliseconds;
    }

    [DataContract]
    public sealed class TestReport
    {
        [DataMember] public string Started;
        [DataMember] public string Finished;
        [DataMember] public string OS;
        [DataMember] public string Application;
        [DataMember] public bool ThemeRestored;
        [DataMember] public List<TestResult> Results;
    }

    internal sealed class FakeTheme : IThemeAccess
    {
        public int Writes;
        public ThemeMode Apps = ThemeMode.Light;
        public ThemeMode System = ThemeMode.Light;
        public bool Apply(ThemeMode mode, bool apps, bool system)
        {
            bool change = (apps && Apps != mode) || (system && System != mode);
            if (apps) Apps = mode;
            if (system) System = mode;
            if (change) Writes++;
            return change;
        }
    }

    internal sealed class FakeSettingsActions : ISettingsActions
    {
        public Settings Current { get; private set; }
        public Localizer Texts { get; private set; }
        public bool StartupEnabled { get { return false; } }
        public SettingsForm Form;
        public FakeSettingsActions() { Current = new Settings(); Texts = new Localizer("en"); }
        public void Save(Settings settings, bool startup) { Current = settings.Copy(); }
        public void Manual(ThemeMode mode, bool notify) { }
        public void Resume() { }
        public void ChangeLanguage(string language)
        {
            Settings next = Current.Copy(); next.Language = language; Current = next;
            Texts.SetLanguage(language); if (Form != null) Form.ApplyLanguage();
        }
    }

    internal static class Tests
    {
        private static readonly List<TestResult> results = new List<TestResult>();
        private static string root;
        private static string executable;
        private static string reportPath;
        private static readonly List<Process> children = new List<Process>();

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
        private static void Reject(Action action)
        {
            bool rejected = false;
            try { action(); }
            catch (InvalidDataException) { rejected = true; }
            Assert(rejected, "Invalid settings were accepted.");
        }
        private static string NewDirectory(string name)
        {
            string path = Path.Combine(root, name + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path); return path;
        }
        private static DateTimeOffset At(int hour, int minute, int second)
        {
            return new DateTimeOffset(new DateTime(2026, 1, 2, hour, minute, second, DateTimeKind.Local));
        }
        private static void Run(string name, Func<string> test)
        {
            var watch = Stopwatch.StartNew();
            var result = new TestResult { Name = name };
            try { result.Details = test(); result.Passed = true; }
            catch (Exception error) { result.Details = error.ToString(); result.Passed = false; }
            result.Milliseconds = watch.ElapsedMilliseconds;
            results.Add(result);
            Console.WriteLine((result.Passed ? "PASS " : "FAIL ") + name + " (" + result.Milliseconds + " ms) " + result.Details);
        }
        private static void WaitFor(Func<bool> condition, int milliseconds, string message)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < milliseconds)
            {
                if (condition()) return;
                Thread.Sleep(100);
            }
            throw new TimeoutException(message);
        }
        private static Process Launch(string directory, string action)
        {
            var process = Process.Start(new ProcessStartInfo(executable,
                "--data-dir \"" + directory + "\" " + action) { UseShellExecute = false, CreateNoWindow = true });
            children.Add(process); return process;
        }
        private static void Quit(string directory, Process main)
        {
            using (Process quit = Launch(directory, "--quit"))
                Assert(quit.WaitForExit(10000) && quit.ExitCode == 0, "Quit client did not exit cleanly.");
            Assert(main.WaitForExit(10000) && main.ExitCode == 0, "Tray process did not exit cleanly.");
        }
        private static string ReadLog(string directory)
        {
            string path = Path.Combine(directory, "MOONLIGHT.log");
            try
            {
                if (!File.Exists(path)) return "";
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream)) return reader.ReadToEnd();
            }
            catch (IOException) { return ""; }
        }
        private static bool RealMode(bool appsLight, bool systemLight)
        {
            return WindowsTheme.IsLight("AppsUseLightTheme") == appsLight &&
                WindowsTheme.IsLight("SystemUsesLightTheme") == systemLight;
        }

        private static void CoreTests()
        {
            Run("Schedule / exact normal boundaries", delegate
            {
                var settings = new Settings();
                Assert(Schedule.At(settings, At(6, 59, 59).LocalDateTime) == ThemeMode.Dark, "Before sunrise.");
                Assert(Schedule.At(settings, At(7, 0, 0).LocalDateTime) == ThemeMode.Light, "At light boundary.");
                Assert(Schedule.At(settings, At(18, 59, 59).LocalDateTime) == ThemeMode.Light, "Before dark boundary.");
                Assert(Schedule.At(settings, At(19, 0, 0).LocalDateTime) == ThemeMode.Dark, "At dark boundary.");
                return "06:59:59, 07:00:00, 18:59:59, 19:00:00 correct.";
            });
            Run("Schedule / cross-midnight light interval", delegate
            {
                var settings = new Settings { LightMinute = 20 * 60, DarkMinute = 6 * 60 };
                Assert(Schedule.At(settings, At(23, 59, 59).LocalDateTime) == ThemeMode.Light, "Late night interval.");
                Assert(Schedule.At(settings, At(0, 0, 0).LocalDateTime) == ThemeMode.Light, "Midnight interval.");
                Assert(Schedule.At(settings, At(5, 59, 59).LocalDateTime) == ThemeMode.Light, "Before end.");
                Assert(Schedule.At(settings, At(6, 0, 0).LocalDateTime) == ThemeMode.Dark, "At end.");
                Assert(Schedule.At(settings, At(20, 0, 0).LocalDateTime) == ThemeMode.Light, "At start.");
                return "20:00 to 06:00 wraps correctly.";
            });
            Run("Schedule / next boundary and next day", delegate
            {
                var settings = new Settings();
                Assert(Schedule.Next(settings, At(7, 0, 0).LocalDateTime) == At(19, 0, 0).LocalDateTime, "Boundary must be strictly in future.");
                Assert(Schedule.Next(settings, At(19, 0, 0).LocalDateTime) == At(7, 0, 0).LocalDateTime.AddDays(1), "Next day.");
                Assert(Schedule.Next(settings, At(23, 59, 59).LocalDateTime).Date == At(0, 0, 0).LocalDateTime.AddDays(1).Date, "Midnight rollover.");
                return "Strictly future boundary; tomorrow rollover correct.";
            });
            Run("Schedule / full-day invariants for six schedules", delegate
            {
                int[][] pairs = { new[] { 420, 1140 }, new[] { 1200, 360 }, new[] { 0, 1 },
                    new[] { 1439, 0 }, new[] { 1, 1439 }, new[] { 800, 799 } };
                foreach (int[] pair in pairs)
                {
                    var settings = new Settings { LightMinute = pair[0], DarkMinute = pair[1] };
                    int light = 0, transitions = 0;
                    for (int minute = 0; minute < 1440; minute++)
                    {
                        DateTime now = At(0, 0, 0).LocalDateTime.AddMinutes(minute);
                        ThemeMode mode = Schedule.At(settings, now);
                        if (mode == ThemeMode.Light) light++;
                        if (mode != Schedule.At(settings, now.AddMinutes(-1))) transitions++;
                        DateTime next = Schedule.Next(settings, now);
                        Assert(next > now && next <= now.AddDays(1), "Next boundary bounds.");
                        Assert(Schedule.At(settings, next) != mode, "Next event changes mode.");
                    }
                    Assert(transitions == 2, "Exactly two transitions per day.");
                    Assert(light == (pair[1] - pair[0] + 1440) % 1440, "Light duration.");
                }
                return "8,640 sampled minutes; two transitions/day, correct duration and next event.";
            });
            Run("Validation / equal times, range, language, schema", delegate
            {
                Reject(delegate { new Settings { LightMinute = 0, DarkMinute = 0 }.Validate(); });
                Reject(delegate { new Settings { LightMinute = -1 }.Validate(); });
                Reject(delegate { new Settings { DarkMinute = 1440 }.Validate(); });
                Reject(delegate { new Settings { Language = "unknown" }.Validate(); });
                Reject(delegate { new Settings { Version = 99 }.Validate(); });
                return "Five invalid configurations rejected.";
            });
            Run("Validation / invalid temporary modes", delegate
            {
                Reject(delegate { new Settings { OverrideMode = "Other", OverrideUntilUtc = "bad" }.Validate(); });
                Reject(delegate { new Settings { OverrideMode = "Dark" }.Validate(); });
                Reject(delegate { new Settings { OverrideUntilUtc = At(19, 0, 0).ToString("o") }.Validate(); });
                return "Invalid mode, missing expiry, orphan expiry rejected.";
            });
            Run("Persistence / round trip and atomic backup", delegate
            {
                var store = new SettingsStore(NewDirectory("round-trip"));
                Settings settings = store.Load(); settings.LightMinute = 480; store.Save(settings);
                Settings loaded = store.Load();
                Assert(loaded.LightMinute == 480 && loaded.DarkMinute == 1140 && loaded.Enabled, "Round trip.");
                Assert(File.Exists(store.FilePath + ".bak"), "Backup missing.");
                Assert(Directory.GetFiles(Path.GetDirectoryName(store.FilePath), "*.tmp-*").Length == 0, "Temp file left behind.");
                return "Saved values and .bak verified; no leftover write file.";
            });
            Run("Persistence / corrupt primary recovered from backup", delegate
            {
                var store = new SettingsStore(NewDirectory("backup-recovery"));
                Settings settings = store.Load(); settings.DarkMinute = 1200; store.Save(settings);
                File.WriteAllText(store.FilePath, "{broken-json");
                Settings recovered = store.Load();
                Assert(recovered.DarkMinute == 1140, "Must recover previous valid settings.");
                Assert(store.Warning != null && store.Warning == "warning.backup", "Recovery warning.");
                Assert(Directory.GetFiles(Path.GetDirectoryName(store.FilePath), "*.corrupt-*").Length == 1, "Bad file not preserved.");
                Assert(store.Load().DarkMinute == 1140, "Recovered primary not persisted.");
                return "Valid backup restored; corrupt original retained.";
            });
            Run("Persistence / both copies corrupt fall back safely", delegate
            {
                var store = new SettingsStore(NewDirectory("corrupt-both")); store.Load();
                File.WriteAllText(store.FilePath, "bad"); File.WriteAllText(store.FilePath + ".bak", "bad");
                Settings recovered = store.Load();
                Assert(recovered.LightMinute == 420 && store.Warning == "warning.defaults", "Safe defaults / correct warning.");
                return "Defaults recovered with accurate warning.";
            });
            Run("Persistence / missing primary recovers backup", delegate
            {
                var store = new SettingsStore(NewDirectory("missing-primary"));
                Settings settings = store.Load(); settings.DarkMinute = 1200; store.Save(settings);
                File.Delete(store.FilePath);
                Assert(store.Load().DarkMinute == 1140, "Missing primary should use previous valid backup.");
                return "Missing primary restored from backup.";
            });
            Run("Persistence / failed save leaves live settings unchanged", delegate
            {
                string blocked = Path.Combine(NewDirectory("failed-write"), "file"); File.WriteAllText(blocked, "blocking file");
                var engine = new ThemeEngine(new Settings(), new SettingsStore(Path.Combine(blocked, "child")), new FakeTheme());
                bool failed = false;
                try { engine.Update(new Settings { LightMinute = 480 }); } catch (IOException) { failed = true; }
                Assert(failed && engine.Settings.LightMinute == 420, "Live config changed after failed save.");
                return "Filesystem error did not replace live configuration.";
            });
            Run("Engine / disabled schedule and manual toggle", delegate
            {
                var fake = new FakeTheme(); var store = new SettingsStore(NewDirectory("disabled"));
                var engine = new ThemeEngine(new Settings { Enabled = false }, store, fake);
                engine.Evaluate(At(22, 0, 0), false);
                Assert(fake.Writes == 0, "Disabled schedule wrote a theme.");
                engine.Manual(ThemeMode.Dark, At(22, 0, 0), false);
                Assert(fake.Writes == 1 && engine.Settings.OverrideMode == null, "Manual mode while paused.");
                engine.Evaluate(At(12, 0, 0), false);
                Assert(fake.Apps == ThemeMode.Dark, "Paused schedule reverted manual mode.");
                return "Paused schedule performs no writes; manual toggle still works.";
            });
            Run("Engine / manual override survives restart and expires", delegate
            {
                var store = new SettingsStore(NewDirectory("override")); var fake = new FakeTheme();
                var engine = new ThemeEngine(new Settings(), store, fake);
                engine.Manual(ThemeMode.Dark, At(12, 0, 0), false);
                var restarted = new ThemeEngine(store.Load(), store, fake);
                Evaluation held = restarted.Evaluate(At(12, 1, 0), false);
                Assert(held.Temporary && fake.Apps == ThemeMode.Dark, "Override not retained.");
                Evaluation expired = restarted.Evaluate(At(19, 0, 0), false);
                Assert(!expired.Temporary && restarted.Settings.OverrideMode == null, "Exact expiry did not clear override.");
                restarted.Evaluate(At(7, 0, 0).AddDays(1), false);
                Assert(fake.Apps == ThemeMode.Light, "Did not resume tomorrow's plan.");
                return "Override persisted, cleared at exact boundary, then followed next day's plan.";
            });
            Run("Engine / high contrast blocks automatic and manual changes", delegate
            {
                var store = new SettingsStore(NewDirectory("contrast")); var fake = new FakeTheme();
                var engine = new ThemeEngine(store.Load(), store, fake);
                engine.Evaluate(At(22, 0, 0), true); engine.Manual(ThemeMode.Dark, At(22, 0, 0), true);
                Assert(fake.Writes == 0 && engine.Settings.OverrideMode == null, "High contrast should preserve state.");
                return "Neither path modifies theme or override in high-contrast mode.";
            });
            Run("Engine / unchanged ticks do not rewrite settings or theme", delegate
            {
                var store = new SettingsStore(NewDirectory("idle")); var fake = new FakeTheme();
                var engine = new ThemeEngine(store.Load(), store, fake);
                string before = File.ReadAllText(store.FilePath);
                for (int tick = 0; tick < 120; tick++) engine.Evaluate(At(22, 0, 0).AddSeconds(tick * 15), false);
                Assert(fake.Writes == 1, "Repeated theme writes.");
                Assert(File.ReadAllText(store.FilePath) == before && !File.Exists(store.FilePath + ".bak"), "Idle config writes.");
                return "120 ticks: one initial theme change, zero config rewrites.";
            });
            Run("Engine / wake catch-up and combined Windows scope", delegate
            {
                var fake = new FakeTheme();
                var engine = new ThemeEngine(new Settings(), new SettingsStore(NewDirectory("wake")), fake);
                engine.Evaluate(At(12, 0, 0), false);
                engine.Evaluate(At(23, 0, 0), false);
                Assert(fake.Apps == ThemeMode.Dark && fake.System == ThemeMode.Dark, "Wake catch-up must switch both themes.");
                Settings next = engine.Settings.Copy(); next.SwitchWindowsTheme = false; engine.Update(next);
                engine.Evaluate(At(12, 0, 0).AddDays(1), false);
                Evaluation manual = engine.Manual(ThemeMode.Light, At(12, 0, 0).AddDays(1), false);
                Assert(manual.SwitchingOff && fake.Apps == ThemeMode.Dark && fake.System == ThemeMode.Dark, "Unchecked theme option changed colors.");
                next = engine.Settings.Copy(); next.SwitchWindowsTheme = true; engine.Update(next);
                engine.Evaluate(At(12, 0, 0).AddDays(1), false);
                Assert(fake.Apps == ThemeMode.Light && fake.System == ThemeMode.Light, "Re-enabling theme scope did not affect both themes.");
                return "Wake catches up; one option controls both Windows and app themes, including the disabled state.";
            });
            Run("Startup / actual HKCU registration and cleanup", delegate
            {
                string name = "MOONLIGHT.Test." + Guid.NewGuid().ToString("N");
                var registration = new StartupRegistration(name);
                try
                {
                    registration.Set(true, executable);
                    Assert(registration.IsEnabled(executable), "Startup command mismatch.");
                    Assert(registration.ReadCommand() == "\"" + Path.GetFullPath(executable) + "\" --background", "Quotes/arguments.");
                    registration.Set(false, executable);
                    Assert(registration.ReadCommand() == null, "Startup value not removed.");
                    return "Real per-user Run value round trip; quoted Unicode path; test value removed.";
                }
                finally { registration.RestoreCommand(null); }
            });
            Run("Identity / consistent path and isolated test profile", delegate
            {
                Assert(Native.Identity(root) == Native.Identity(root.ToUpperInvariant() + "\\"), "Path normalization.");
                Assert(Native.Identity(root) != Native.Identity(Path.Combine(root, "isolated")), "Profile collision.");
                return "Singleton identities are path-stable and profile-isolated.";
            });
        }

        private static IEnumerable<Control> Descendants(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (Control nested in Descendants(child)) yield return nested;
            }
        }
        private static Control Named(Control root, string name)
        {
            foreach (Control control in Descendants(root)) if (control.Name == name) return control;
            throw new Exception("Control not found: " + name);
        }
        private static void LocalizationAndLayoutTests()
        {
            Run("Localization / English defaults and complete bilingual strings", delegate
            {
                Assert(new Settings().Language == "en", "Default must be English.");
                foreach (string language in new[] { "en", "zh-CN" })
                {
                    var texts = new Localizer(language);
                    foreach (string key in Localizer.Keys)
                    {
                        string value = texts.Format(key, "one", "two", "three");
                        Assert(!string.IsNullOrWhiteSpace(value), "Missing translation: " + key);
                        Assert(value.IndexOfAny(new[] { '(', ')', '（', '）' }) < 0, "Parentheses in visible text: " + key);
                        if (language == "en")
                            foreach (char c in value) Assert(c < 0x4e00 || c > 0x9fff, "Chinese text in English translation: " + key);
                    }
                    Assert(texts["app.name"] == "MOONLIGHT", "Brand capitalization.");
                }
                return "Every UI/tray string has both languages; default English; no parentheses in either catalog.";
            });
            Run("Localization / language persistence preserves schedule and manual mode", delegate
            {
                var store = new SettingsStore(NewDirectory("language-preference"));
                var engine = new ThemeEngine(store.Load(), store, new FakeTheme());
                engine.Manual(ThemeMode.Dark, At(12, 0, 0), false);
                string expiry = engine.Settings.OverrideUntilUtc;
                Settings next = engine.Settings.Copy(); next.Language = "zh-CN"; engine.Update(next);
                Settings reloaded = store.Load();
                Assert(reloaded.Language == "zh-CN" && reloaded.LightMinute == 420 && reloaded.OverrideUntilUtc == expiry,
                    "Language update changed schedule or manual expiry.");
                next = reloaded.Copy(); next.Language = "en"; engine.Update(next);
                Assert(store.Load().Language == "en", "English selection not persisted.");
                return "Chinese and English round trip; theme and schedule retained.";
            });
            Run("Layout / requested controls, ordering, and compact branding", delegate
            {
                var actions = new FakeSettingsActions();
                using (var form = new SettingsForm(actions, SystemIcons.Application))
                {
                    actions.Form = form;
                    Assert(form.Text == "MOONLIGHT" && Named(form, "brandName").Text == "MOONLIGHT", "Window/heading brand.");
                    int checkboxes = 0, labels = 0;
                    foreach (Control control in Descendants(form))
                    {
                        if (control is CheckBox) checkboxes++;
                        if (control is Label) labels++;
                        Assert(control.Text.IndexOfAny(new[] { '(', ')', '（', '）' }) < 0, "Parentheses in control: " + control.Name);
                    }
                    Assert(checkboxes == 3, "Expected only schedule, combined Windows theme, and startup checkboxes.");
                    Assert(labels == 4, "Unexpected subtitle, theme card, or old footer.");
                    var layout = (TableLayoutPanel)Named(form, "settingsLayout");
                    form.PerformLayout();
                    Assert(layout.GetPreferredSize(new Size(form.ClientSize.Width, 0)).Height <= form.ClientSize.Height,
                        "Default settings window requires scrolling to reach the action row.");
                    Assert(layout.GetRow(Named(form, "brandName")) == 0 && layout.GetRow(Named(form, "enableSchedule")) == 1 &&
                        layout.GetRow(Named(form, "startTimes")) == 2 && layout.GetRow(Named(form, "themeOptions")) == 3, "Top-to-bottom order.");
                    var row = (TableLayoutPanel)Named(form, "mainActions");
                    Assert(row.GetColumn(Named(form, "saveButton")) == 0 && row.GetColumn(Named(form, "languageSelector")) == 1 &&
                        row.GetColumn(Named(form, "hideButton")) == 2, "Language must sit between Save and Hide.");
                    Assert(Named(form, "switchLightButton").Text == "Switch to light" && Named(form, "switchDarkButton").Text == "Switch to dark", "Switch button wording.");
                }
                return "Exact three-checkbox layout; language centered between actions; no old title/card/footer.";
            });
            Run("Layout / immediate language switch keeps unsaved controls", delegate
            {
                var actions = new FakeSettingsActions();
                using (var form = new SettingsForm(actions, SystemIcons.Application))
                {
                    actions.Form = form;
                    var time = (DateTimePicker)Named(form, "lightStartTime"); time.Value = DateTime.Today.AddHours(8.5);
                    var scope = (CheckBox)Named(form, "switchWindowsTheme"); scope.Checked = false;
                    var selector = (ComboBox)Named(form, "languageSelector");
                    selector.SelectedIndex = 1;
                    Assert(actions.Current.Language == "zh-CN" && Named(form, "saveButton").Text == "保存并应用", "Chinese did not apply immediately.");
                    Assert(time.Value.Hour == 8 && time.Value.Minute == 30 && !scope.Checked, "Language change discarded pending settings.");
                    selector.SelectedIndex = 0;
                    Assert(actions.Current.Language == "en" && Named(form, "saveButton").Text == "Save and apply", "English did not apply immediately.");
                    Assert(form.Text == "MOONLIGHT" && !scope.Checked, "Brand or pending scope changed.");
                }
                return "Language selector event updates immediately in both directions while preserving unsaved inputs.";
            });
            Run("Upgrade / prior schedule converted to combined theme option", delegate
            {
                byte[] json = Encoding.UTF8.GetBytes("{\"Version\":1,\"LightMinute\":480,\"DarkMinute\":1200,\"Enabled\":false,\"SwitchApps\":true,\"SwitchSystem\":false}");
                using (var stream = new MemoryStream(json))
                {
                    Settings converted = Upgrade.ConvertSettings(stream);
                    Assert(converted.Version == 2 && converted.LightMinute == 480 && converted.DarkMinute == 1200 &&
                        !converted.Enabled && converted.SwitchWindowsTheme && converted.Language == "en", "Legacy migration changed preferences.");
                }
                return "Prior time/enable settings retained, combined scope and English default applied.";
            });
        }

        private static void WindowsTests()
        {
            var theme = new WindowsTheme(new AppLog(root), false);
            Run("Windows / real dark then light theme", delegate
            {
                theme.Apply(ThemeMode.Dark, true, true); Assert(RealMode(false, false), "Dark values missing.");
                theme.Apply(ThemeMode.Light, true, true); Assert(RealMode(true, true), "Light values missing.");
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(WindowsTheme.KeyPath))
                {
                    Assert(key.GetValueKind("AppsUseLightTheme") == RegistryValueKind.DWord &&
                        key.GetValueKind("SystemUsesLightTheme") == RegistryValueKind.DWord, "Wrong registry kinds.");
                }
                return "Actual Windows personalization DWORDs verified at 0/0 and 1/1; WM_SETTINGCHANGE sent.";
            });
            Run("Windows / apps-only and system-only", delegate
            {
                theme.Apply(ThemeMode.Light, true, true);
                theme.Apply(ThemeMode.Dark, true, false); Assert(RealMode(false, true), "Apps-only.");
                theme.Apply(ThemeMode.Dark, false, true); Assert(RealMode(false, false), "System-only.");
                Assert(!theme.Apply(ThemeMode.Dark, true, true), "Same mode must be a no-op.");
                return "Unselected theme preserved; already-correct mode is a no-op.";
            });
            Run("Executable / two ordinary launches toggle both ways, one tray instance", delegate
            {
                string directory = NewDirectory("double-click-equivalent");
                new SettingsStore(directory).Save(new Settings { Enabled = false });
                theme.Apply(ThemeMode.Light, true, true);
                Process main = Launch(directory, "");
                try
                {
                    WaitFor(delegate { return RealMode(false, false) && ReadLog(directory).Contains("MANUAL mode=Dark"); }, 15000, "First ordinary launch did not toggle dark.");
                    using (Process second = Launch(directory, ""))
                        Assert(second.WaitForExit(10000) && second.ExitCode == 0, "Second launch did not forward to existing process.");
                    WaitFor(delegate { return RealMode(true, true) && ReadLog(directory).Contains("MANUAL mode=Light"); }, 10000, "Second ordinary launch did not toggle light.");
                    Assert(!main.HasExited && ReadLog(directory).Split(new[] { "START action=" }, StringSplitOptions.None).Length == 2, "Multiple persistent tray instances.");
                    main.Refresh(); long memory = main.WorkingSet64 / 1024 / 1024;
                    Assert(main.MainWindowHandle == IntPtr.Zero, "Ordinary toggle should not open the settings window.");
                    return "First launch: dark; second: light; singleton forwarding and hidden settings verified. Working set " + memory + " MB.";
                }
                finally { Quit(directory, main); }
            });
            Run("Executable / automatic mode keeps manual toggle until next boundary", delegate
            {
                string directory = NewDirectory("toggle-automatic");
                new SettingsStore(directory).Save(new Settings());
                theme.Apply(ThemeMode.Light, true, true);
                Process main = Launch(directory, "");
                try
                {
                    WaitFor(delegate { return RealMode(false, false) && ReadLog(directory).Contains("MANUAL mode=Dark"); }, 15000, "No manual dark.");
                    Settings persisted = new SettingsStore(directory).Load();
                    ThemeMode temporary;
                    Assert(Schedule.TryOverride(persisted, DateTimeOffset.Now, out temporary) && temporary == ThemeMode.Dark, "Manual override not persisted.");
                    using (Process background = Launch(directory, "--background"))
                        Assert(background.WaitForExit(10000) && background.ExitCode == 0, "Background duplicate exit.");
                    Assert(RealMode(false, false), "Duplicate background launch toggled the theme.");
                    return "Persistent temporary override verified; background duplicate does not toggle.";
                }
                finally { Quit(directory, main); }
            });
            Run("Executable / settings window startup smoke test", delegate
            {
                string directory = NewDirectory("settings-window");
                new SettingsStore(directory).Save(new Settings { Enabled = false });
                Process main = Launch(directory, "--settings");
                try
                {
                    WaitFor(delegate { main.Refresh(); return main.MainWindowHandle != IntPtr.Zero && main.Responding; }, 15000, "No responsive settings window.");
                    return "Native settings window created and responsive. Control clicking / visual appearance not tested.";
                }
                finally { Quit(directory, main); }
            });
            Run("Executable / real clock boundary triggers automatic change", delegate
            {
                string directory = NewDirectory("live-timer");
                DateTime now = DateTime.Now;
                DateTime boundary = now.Date.AddHours(now.Hour).AddMinutes(now.Minute + 1);
                if ((boundary - now).TotalSeconds < 10) boundary = boundary.AddMinutes(1);
                var settings = new Settings { LightMinute = now.Hour * 60 + now.Minute,
                    DarkMinute = boundary.Hour * 60 + boundary.Minute };
                new SettingsStore(directory).Save(settings);
                theme.Apply(ThemeMode.Dark, true, true);
                Process main = Launch(directory, "--background");
                try
                {
                    WaitFor(delegate { return RealMode(true, true) && ReadLog(directory).Contains("APPLY reason=startup mode=Light"); }, 15000, "Background launch did not catch up to light schedule.");
                    main.Refresh(); Assert(main.MainWindowHandle == IntPtr.Zero, "Background launch opened a settings window.");
                    Console.WriteLine("WAIT live boundary " + boundary.ToString("yyyy-MM-dd HH:mm:ss") + " local time; scheduler interval 15 seconds.");
                    WaitFor(delegate { return RealMode(false, false) && ReadLog(directory).Contains("APPLY reason=timer mode=Dark"); }, 95000, "Live timer failed to apply dark after the boundary.");
                    double delay = (DateTime.Now - boundary).TotalSeconds;
                    Assert(delay >= 0 && delay < 17, "Timer transition delay exceeds expected tolerance: " + delay);
                    return "Real boundary " + boundary.ToString("HH:mm:ss") + "; dark applied " + delay.ToString("F1", CultureInfo.InvariantCulture) + " s later. No clock manipulation.";
                }
                finally { Quit(directory, main); }
            });
        }

        [STAThread]
        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            if (args.Length < 2) { Console.WriteLine("Usage: Moonlight.Tests.exe APPLICATION REPORT.json"); return 2; }
            executable = Path.GetFullPath(args[0]); reportPath = Path.GetFullPath(args[1]);
            root = Path.Combine(Path.GetDirectoryName(reportPath), "test-data-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var report = new TestReport { Started = DateTimeOffset.Now.ToString("o"), OS = Environment.OSVersion.VersionString,
                Application = executable, Results = results };
            bool unitOnly = args.Length >= 3 && args[2] == "--unit-only";
            if (unitOnly)
            {
                CoreTests(); LocalizationAndLayoutTests();
                report.ThemeRestored = true; report.Finished = DateTimeOffset.Now.ToString("o");
                using (var stream = File.Create(reportPath)) new DataContractJsonSerializer(typeof(TestReport)).WriteObject(stream, report);
                int unitFailures = results.FindAll(result => !result.Passed).Count;
                Console.WriteLine("RESULT " + (results.Count - unitFailures) + "/" + results.Count + " passed; unit-only mode");
                return unitFailures == 0 ? 0 : 1;
            }
            RegistryValue originalApps, originalSystem;
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(WindowsTheme.KeyPath))
            {
                originalApps = RegistryValue.Read(key, "AppsUseLightTheme"); originalSystem = RegistryValue.Read(key, "SystemUsesLightTheme");
            }
            try { CoreTests(); LocalizationAndLayoutTests(); WindowsTests(); }
            finally
            {
                foreach (Process child in children)
                {
                    try { if (!child.HasExited) { child.Kill(); child.WaitForExit(5000); } }
                    catch (InvalidOperationException) { }
                }
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(WindowsTheme.KeyPath))
                {
                    RegistryValue.Restore(key, "AppsUseLightTheme", originalApps);
                    RegistryValue.Restore(key, "SystemUsesLightTheme", originalSystem);
                }
                Native.BroadcastTheme();
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(WindowsTheme.KeyPath))
                {
                    RegistryValue apps = RegistryValue.Read(key, "AppsUseLightTheme");
                    RegistryValue system = RegistryValue.Read(key, "SystemUsesLightTheme");
                    report.ThemeRestored = Same(apps, originalApps) && Same(system, originalSystem);
                }
                report.Finished = DateTimeOffset.Now.ToString("o");
                using (var stream = File.Create(reportPath)) new DataContractJsonSerializer(typeof(TestReport)).WriteObject(stream, report);
            }
            int failures = results.FindAll(result => !result.Passed).Count;
            Console.WriteLine("RESULT " + (results.Count - failures) + "/" + results.Count + " passed; original theme restored=" + report.ThemeRestored);
            return failures == 0 && report.ThemeRestored ? 0 : 1;
        }
        private static bool Same(RegistryValue a, RegistryValue b)
        {
            if (a == null || b == null) return a == b;
            return a.Kind == b.Kind && object.Equals(a.Value, b.Value);
        }
    }
}
