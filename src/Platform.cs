using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace Moonlight
{
    public sealed class AppLog
    {
        private readonly string path;
        private readonly object gate = new object();
        public AppLog(string directory) { path = Path.Combine(directory, "MOONLIGHT.log"); }
        public void Write(string text)
        {
            try
            {
                lock (gate)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024)
                    {
                        if (File.Exists(path + ".1")) File.Delete(path + ".1");
                        File.Move(path, path + ".1");
                    }
                    using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                        writer.WriteLine(DateTimeOffset.Now.ToString("o") + " " + text);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public static class Native
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, UIntPtr wParam,
            string lParam, uint flags, uint timeout, out UIntPtr result);
        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        public static bool BroadcastTheme()
        {
            UIntPtr result;
            return SendMessageTimeout(new IntPtr(0xffff), 0x001a, UIntPtr.Zero,
                "ImmersiveColorSet", 0x0022, 200, out result) != IntPtr.Zero;
        }

        public static string Identity(string directory)
        {
            using (SHA256 hash = SHA256.Create())
            {
                byte[] data = hash.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(directory).TrimEnd('\\').ToUpperInvariant()));
                return BitConverter.ToString(data, 0, 12).Replace("-", "");
            }
        }
    }

    public sealed class RegistryValue
    {
        public object Value;
        public RegistryValueKind Kind;
        public static RegistryValue Read(RegistryKey key, string name)
        {
            object value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            return value == null ? null : new RegistryValue { Value = value, Kind = key.GetValueKind(name) };
        }
        public static void Restore(RegistryKey key, string name, RegistryValue value)
        {
            if (value == null) key.DeleteValue(name, false);
            else key.SetValue(name, value.Value, value.Kind);
        }
    }

    public sealed class WindowsTheme : IThemeAccess
    {
        public const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private readonly AppLog log;
        private readonly bool asynchronousBroadcast;
        private int notifyRunning;
        private int notifyPending;
        public WindowsTheme(AppLog log, bool asynchronousBroadcast)
        {
            this.log = log; this.asynchronousBroadcast = asynchronousBroadcast;
        }

        public static bool IsLight(string name)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(KeyPath))
            {
                object value = key == null ? null : key.GetValue(name);
                return !(value is int) || (int)value != 0;
            }
        }

        public bool Apply(ThemeMode mode, bool apps, bool system)
        {
            if (!apps && !system) throw new ArgumentException("validation.scope");
            int desired = mode == ThemeMode.Light ? 1 : 0;
            bool changed = false;
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath))
            {
                RegistryValue oldApps = RegistryValue.Read(key, "AppsUseLightTheme");
                RegistryValue oldSystem = RegistryValue.Read(key, "SystemUsesLightTheme");
                bool changeApps = apps && !Matches(oldApps, desired);
                bool changeSystem = system && !Matches(oldSystem, desired);
                if (!changeApps && !changeSystem) return false;
                bool wroteApps = false, wroteSystem = false;
                try
                {
                    if (changeApps) { key.SetValue("AppsUseLightTheme", desired, RegistryValueKind.DWord); wroteApps = true; }
                    if (changeSystem) { key.SetValue("SystemUsesLightTheme", desired, RegistryValueKind.DWord); wroteSystem = true; }
                    key.Flush(); changed = true;
                }
                catch
                {
                    if (wroteApps) RegistryValue.Restore(key, "AppsUseLightTheme", oldApps);
                    if (wroteSystem) RegistryValue.Restore(key, "SystemUsesLightTheme", oldSystem);
                    throw;
                }
            }
            if (changed)
            {
                if (log != null) log.Write("THEME " + mode + " apps=" + apps + " system=" + system);
                Notify();
            }
            return changed;
        }

        private static bool Matches(RegistryValue value, int desired)
        {
            return value != null && value.Kind == RegistryValueKind.DWord && value.Value is int && (int)value.Value == desired;
        }

        public void Notify()
        {
            if (!asynchronousBroadcast) { Broadcast(); return; }
            Interlocked.Exchange(ref notifyPending, 1);
            StartNotifyWorker();
        }

        private void StartNotifyWorker()
        {
            if (Interlocked.CompareExchange(ref notifyRunning, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    while (Interlocked.Exchange(ref notifyPending, 0) != 0) Broadcast();
                }
                finally
                {
                    Interlocked.Exchange(ref notifyRunning, 0);
                    if (Interlocked.CompareExchange(ref notifyPending, 0, 0) != 0) StartNotifyWorker();
                }
            });
        }

        private void Broadcast()
        {
            bool success = Native.BroadcastTheme();
            if (log != null) log.Write(success ? "NOTIFY delivered" : "NOTIFY some windows timed out; saved theme remains active");
        }
    }

    public sealed class StartupRegistration
    {
        public const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private readonly string valueName;
        public StartupRegistration() : this("MOONLIGHT") { }
        public StartupRegistration(string valueName) { this.valueName = valueName; }
        public string ReadCommand()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(KeyPath))
                return key == null ? null : key.GetValue(valueName) as string;
        }
        public static string Command(string executable)
        {
            if (executable.IndexOf('"') >= 0) throw new ArgumentException("validation.path");
            string command = "\"" + Path.GetFullPath(executable) + "\" --background";
            if (command.Length > 260) throw new InvalidOperationException("validation.longPath");
            return command;
        }
        public bool IsEnabled(string executable)
        {
            return string.Equals(ReadCommand(), Command(executable), StringComparison.OrdinalIgnoreCase);
        }
        public void Set(bool enabled, string executable)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath))
            {
                if (enabled) key.SetValue(valueName, Command(executable), RegistryValueKind.String);
                else key.DeleteValue(valueName, false);
            }
        }
        public void RestoreCommand(string command)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath))
            {
                if (command == null) key.DeleteValue(valueName, false);
                else key.SetValue(valueName, command, RegistryValueKind.String);
            }
        }
    }
}
