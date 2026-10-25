using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("MOONLIGHT")]
[assembly: AssemblyDescription("A local Windows theme switcher")]
[assembly: AssemblyProduct("MOONLIGHT")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]
[assembly: InternalsVisibleTo("Moonlight.Tests")]

namespace Moonlight
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MOONLIGHT");
            string action = "toggle";
            bool isolated = false;
            var fallback = new Localizer("en");
            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "--background") action = "background";
                    else if (args[i] == "--settings") action = "settings";
                    else if (args[i] == "--quit") action = "quit";
                    else if (args[i] == "--data-dir" && i + 1 < args.Length)
                    {
                        directory = Path.GetFullPath(args[++i]); isolated = true;
                    }
                    else throw new ArgumentException("error.argument");
                }
                string identity = "Local\\Moonlight." + Native.Identity(directory);
                if (action == "quit")
                {
                    try { using (var quit = EventWaitHandle.OpenExisting(identity + ".Quit")) quit.Set(); }
                    catch (WaitHandleCannotBeOpenedException) { }
                    return 0;
                }
                using (var mutex = new Mutex(false, identity + ".Mutex"))
                {
                    bool owns;
                    try { owns = mutex.WaitOne(0, false); }
                    catch (AbandonedMutexException) { owns = true; }
                    if (!owns)
                    {
                        if (action == "background") return 0;
                        for (int attempt = 0; attempt < 100; attempt++)
                        {
                            try
                            {
                                using (var signal = EventWaitHandle.OpenExisting(identity +
                                    (action == "settings" ? ".Show" : ".Toggle"))) signal.Set();
                                return 0;
                            }
                            catch (WaitHandleCannotBeOpenedException) { Thread.Sleep(50); }
                        }
                        throw new IOException("error.starting");
                    }
                    try
                    {
                        Directory.CreateDirectory(directory);
                        var log = new AppLog(directory);
                        Application.EnableVisualStyles();
                        Application.SetCompatibleTextRenderingDefault(false);
                        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                        TrayApplication active = null;
                        Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs error)
                        {
                            log.Write("ERROR " + error.Exception);
                            Localizer texts = active == null ? fallback : active.Texts;
                            MessageBox.Show(texts.Error(error.Exception), "MOONLIGHT", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        };
                        using (var context = new TrayApplication(directory, identity, action, isolated, log))
                        {
                            active = context; Application.Run(context);
                        }
                        return 0;
                    }
                    finally { mutex.ReleaseMutex(); }
                }
            }
            catch (Exception error)
            {
                string key = Localizer.ErrorKey(error);
                MessageBox.Show(fallback[key == "error.action" ? "error.start" : key], "MOONLIGHT", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }

    internal sealed class TrayApplication : ApplicationContext, ISettingsActions
    {
        private readonly AppLog log;
        private readonly SettingsStore store;
        private readonly ThemeEngine engine;
        private readonly StartupRegistration startup = new StartupRegistration();
        private readonly SettingsForm form;
        private readonly NotifyIcon tray;
        private readonly ContextMenuStrip menu;
        private readonly ToolStripMenuItem toggleItem, lightItem, darkItem, resumeItem, automaticItem, settingsItem, exitItem;
        private readonly System.Windows.Forms.Timer timer;
        private readonly EventWaitHandle showEvent, toggleEvent, quitEvent;
        private readonly RegisteredWaitHandle showWait, toggleWait, quitWait;
        private readonly Icon icon;
        private bool exiting;
        private string lastError;
        public Settings Current { get { return engine.Settings; } }
        public Localizer Texts { get; private set; }
        public bool StartupEnabled { get { return startup.IsEnabled(Application.ExecutablePath); } }

        public TrayApplication(string directory, string identity, string action, bool isolated, AppLog log)
        {
            this.log = log;
            store = new SettingsStore(directory);
            if (!isolated) Upgrade.ImportSettings(store, log);
            Settings settings = store.Load();
            Texts = new Localizer(settings.Language);
            engine = new ThemeEngine(settings, store, new WindowsTheme(log, true));
            if (!isolated) Upgrade.ImportStartup(Application.ExecutablePath, log);
            icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            form = new SettingsForm(this, icon);
            form.FormClosed += delegate { if (!exiting) ExitThread(); };
            IntPtr handle = form.Handle;
            menu = new ContextMenuStrip();
            toggleItem = Item("tray.toggle", delegate { Toggle(true); });
            menu.Items.Add(new ToolStripSeparator());
            lightItem = Item("switch.light", delegate { Manual(ThemeMode.Light, true); });
            darkItem = Item("switch.dark", delegate { Manual(ThemeMode.Dark, true); });
            resumeItem = Item("schedule.resume", delegate { Resume(); });
            automaticItem = Item("schedule.enable", delegate
            {
                RunSafe(delegate
                {
                    Settings next = Current.Copy(); next.Enabled = !next.Enabled; next.ClearOverride();
                    engine.Update(next); form.LoadFields(); EvaluateCore("auto-toggle");
                });
            });
            menu.Items.Add(new ToolStripSeparator());
            settingsItem = Item("tray.settings", delegate { ShowSettings(); });
            exitItem = Item("tray.exit", delegate { ExitThread(); });
            tray = new NotifyIcon { Icon = icon, Text = "MOONLIGHT", ContextMenuStrip = menu, Visible = true };
            tray.DoubleClick += delegate { ShowSettings(); };
            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, identity + ".Show");
            toggleEvent = new EventWaitHandle(false, EventResetMode.AutoReset, identity + ".Toggle");
            quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, identity + ".Quit");
            showWait = Register(showEvent, ShowSettings);
            toggleWait = Register(toggleEvent, delegate { Toggle(true); });
            quitWait = Register(quitEvent, delegate { ExitThread(); });
            timer = new System.Windows.Forms.Timer { Interval = 15000 };
            timer.Tick += delegate { RunSafe(delegate { EvaluateCore("timer"); }); };
            timer.Start();
            SystemEvents.PowerModeChanged += OnPower;
            SystemEvents.TimeChanged += OnTimeChanged;
            SystemEvents.SessionSwitch += OnSession;
            log.Write("START action=" + action + " pid=" + Process.GetCurrentProcess().Id + " language=" + Current.Language +
                " schedule=" + Schedule.TimeText(Current.LightMinute) + "/" + Schedule.TimeText(Current.DarkMinute));
            form.BeginInvoke((MethodInvoker)delegate
            {
                if (action == "toggle") Toggle(true);
                else RunSafe(delegate { EvaluateCore("startup"); });
                if (action == "settings" || store.Warning != null) ShowSettings();
                if (store.Warning != null) { log.Write("WARNING " + store.Warning); form.SetFeedback(store.Warning, true); }
            });
        }

        private ToolStripMenuItem Item(string key, EventHandler handler)
        {
            var item = new ToolStripMenuItem(Texts[key]); item.Click += handler; menu.Items.Add(item); return item;
        }
        private RegisteredWaitHandle Register(EventWaitHandle handle, Action action)
        {
            return ThreadPool.RegisterWaitForSingleObject(handle, delegate { Dispatch(action); }, null, Timeout.Infinite, false);
        }
        private void Dispatch(Action action)
        {
            if (exiting || form.IsDisposed) return;
            try { form.BeginInvoke((MethodInvoker)delegate { if (!exiting) action(); }); }
            catch (InvalidOperationException) { }
        }
        private void OnPower(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume) Dispatch(delegate { RunSafe(delegate { EvaluateCore("wake"); }); });
        }
        private void OnTimeChanged(object sender, EventArgs e) { Dispatch(delegate { RunSafe(delegate { EvaluateCore("clock-change"); }); }); }
        private void OnSession(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionUnlock || e.Reason == SessionSwitchReason.SessionLogon)
                Dispatch(delegate { RunSafe(delegate { EvaluateCore("unlock"); }); });
        }
        private void RunSafe(Action action)
        {
            try { action(); lastError = null; }
            catch (Exception error)
            {
                bool newError = error.Message != lastError;
                if (newError) log.Write("ERROR " + error);
                lastError = error.Message;
                form.SetFeedback(Localizer.ErrorKey(error), true);
                tray.Text = "MOONLIGHT";
                if (!form.Visible && newError) Balloon(Texts["app.name"], Texts.Error(error), true);
            }
        }
        private void EvaluateCore(string reason)
        {
            if (exiting) return;
            Evaluation result = engine.Evaluate(DateTimeOffset.Now, SystemInformation.HighContrast);
            if (result.Changed) log.Write("APPLY reason=" + reason + " mode=" + result.Mode);
            RefreshStatus();
        }
        public void Toggle(bool notify)
        {
            RunSafe(delegate
            {
                bool light = WindowsTheme.IsLight("AppsUseLightTheme");
                ManualCore(light ? ThemeMode.Dark : ThemeMode.Light, notify);
            });
        }
        public void Manual(ThemeMode mode, bool notify) { RunSafe(delegate { ManualCore(mode, notify); }); }
        private void ManualCore(ThemeMode mode, bool notify)
        {
            Evaluation result = engine.Manual(mode, DateTimeOffset.Now, SystemInformation.HighContrast);
            log.Write("MANUAL mode=" + mode + " changed=" + result.Changed + " switchingOff=" + result.SwitchingOff);
            RefreshStatus();
            if (result.HighContrast || result.SwitchingOff)
            {
                string key = result.HighContrast ? "notice.contrast" : "notice.disabled";
                form.SetFeedback(key, true);
                if (notify) Balloon("MOONLIGHT", Texts[key], false);
                return;
            }
            form.SetFeedback("switched", false, Texts.Mode(mode));
            if (notify) Balloon(Texts.Format("switched", Texts.Mode(mode)), Texts[Current.Enabled ? "notice.manual" : "notice.paused"], false);
        }
        public void Resume()
        {
            RunSafe(delegate
            {
                Settings next = Current.Copy(); next.Enabled = true; next.ClearOverride();
                engine.Update(next); form.LoadFields(); EvaluateCore("resume-plan");
                form.SetFeedback("schedule.resumed", false);
            });
        }
        public void Save(Settings next, bool runAtLogin)
        {
            RunSafe(delegate
            {
                next.Validate(); next.ClearOverride();
                string oldCommand = startup.ReadCommand();
                try { startup.Set(runAtLogin, Application.ExecutablePath); engine.Update(next); }
                catch { startup.RestoreCommand(oldCommand); throw; }
                log.Write("SAVE schedule=" + Schedule.TimeText(next.LightMinute) + "/" + Schedule.TimeText(next.DarkMinute) +
                    " enabled=" + next.Enabled + " theme=" + next.SwitchWindowsTheme + " startup=" + runAtLogin);
                form.LoadFields(); EvaluateCore("settings"); form.SetFeedback("saved", false);
            });
        }
        public void ChangeLanguage(string language)
        {
            RunSafe(delegate
            {
                Settings next = Current.Copy(); next.Language = language;
                engine.Update(next);
                Texts.SetLanguage(language);
                ApplyTrayLanguage(); form.ApplyLanguage(); RefreshStatus();
                form.SetFeedback("language.saved", false); log.Write("LANGUAGE " + language);
            });
            // Restore the selector too if saving the preference failed.
            form.ApplyLanguage();
        }
        private void ApplyTrayLanguage()
        {
            toggleItem.Text = Texts["tray.toggle"]; lightItem.Text = Texts["switch.light"]; darkItem.Text = Texts["switch.dark"];
            resumeItem.Text = Texts["schedule.resume"]; automaticItem.Text = Texts["schedule.enable"];
            settingsItem.Text = Texts["tray.settings"]; exitItem.Text = Texts["tray.exit"];
        }
        private void RefreshStatus()
        {
            automaticItem.Checked = Current.Enabled;
            ThemeMode ignored;
            string key = SystemInformation.HighContrast ? "status.contrast" : !Current.SwitchWindowsTheme ? "status.disabled" :
                !Current.Enabled ? "status.paused" : Schedule.TryOverride(Current, DateTimeOffset.Now, out ignored) ? "status.override" : "status.schedule";
            string tooltip = "MOONLIGHT · " + Texts[key];
            tray.Text = tooltip.Length <= 63 ? tooltip : "MOONLIGHT";
            form.RefreshStatus(key, !WindowsTheme.IsLight("AppsUseLightTheme"));
        }
        private void Balloon(string title, string text, bool warning)
        {
            tray.ShowBalloonTip(2500, title, text, warning ? ToolTipIcon.Warning : ToolTipIcon.Info);
        }
        private void ShowSettings()
        {
            RefreshStatus(); form.Show(); form.WindowState = FormWindowState.Normal; form.Activate();
        }
        protected override void ExitThreadCore()
        {
            if (exiting) return;
            exiting = true; log.Write("STOP");
            timer.Stop(); timer.Dispose();
            SystemEvents.PowerModeChanged -= OnPower; SystemEvents.TimeChanged -= OnTimeChanged; SystemEvents.SessionSwitch -= OnSession;
            showWait.Unregister(null); toggleWait.Unregister(null); quitWait.Unregister(null);
            tray.Visible = false; tray.Dispose(); menu.Dispose();
            showEvent.Dispose(); toggleEvent.Dispose(); quitEvent.Dispose();
            form.AllowExit = true; form.Close(); form.Dispose(); icon.Dispose();
            base.ExitThreadCore();
        }
    }
}
