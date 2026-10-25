using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace Moonlight
{
    [DataContract]
    internal sealed class LegacySettings
    {
        [DataMember] public int Version;
        [DataMember] public int LightMinute;
        [DataMember] public int DarkMinute;
        [DataMember] public bool Enabled;
        [DataMember] public bool SwitchApps;
        [DataMember] public bool SwitchSystem;
    }

    internal static class Upgrade
    {
        internal static Settings ConvertSettings(Stream stream)
        {
            var legacy = (LegacySettings)new DataContractJsonSerializer(typeof(LegacySettings)).ReadObject(stream);
            if (legacy == null || legacy.Version != 1) throw new InvalidDataException("validation.version");
            var settings = new Settings { LightMinute = legacy.LightMinute, DarkMinute = legacy.DarkMinute,
                Enabled = legacy.Enabled, SwitchWindowsTheme = legacy.SwitchApps || legacy.SwitchSystem, Language = "en" };
            settings.Validate(); return settings;
        }
        internal static void ImportSettings(SettingsStore store, AppLog log)
        {
            if (File.Exists(store.FilePath) || File.Exists(store.FilePath + ".bak")) return;
            string legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalDayNight", "settings.json");
            if (!File.Exists(legacy)) return;
            try
            {
                using (var stream = File.OpenRead(legacy)) store.Save(ConvertSettings(stream));
                log.Write("UPGRADE imported previous schedule; language=en");
            }
            catch (Exception error) { log.Write("UPGRADE settings import skipped: " + error.Message); }
        }
        internal static void ImportStartup(string executable, AppLog log)
        {
            var previous = new StartupRegistration("LocalDayNight");
            string command = previous.ReadCommand();
            if (command == null || !command.StartsWith("\"", StringComparison.Ordinal)) return;
            int end = command.IndexOf('"', 1);
            if (end < 2 || command.Substring(end + 1).Trim() != "--background") return;
            string oldPath = command.Substring(1, end - 1);
            try
            {
                if (!File.Exists(oldPath) || FileVersionInfo.GetVersionInfo(oldPath).ProductName != "LocalDayNight") return;
                var current = new StartupRegistration();
                string oldCurrent = current.ReadCommand();
                try { current.Set(true, executable); previous.RestoreCommand(null); }
                catch { current.RestoreCommand(oldCurrent); throw; }
                log.Write("UPGRADE migrated previous startup registration");
            }
            catch (Exception error) { log.Write("UPGRADE startup import skipped: " + error.Message); }
        }
    }
}
