using System;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace Moonlight
{
    public enum ThemeMode { Light, Dark }

    [DataContract]
    public sealed class Settings
    {
        [DataMember] public int Version = 2;
        [DataMember] public int LightMinute = 7 * 60;
        [DataMember] public int DarkMinute = 19 * 60;
        [DataMember] public bool Enabled = true;
        [DataMember] public bool SwitchWindowsTheme = true;
        [DataMember] public string Language = "en";
        [DataMember] public string OverrideMode;
        [DataMember] public string OverrideUntilUtc;

        public Settings Copy() { return (Settings)MemberwiseClone(); }
        public void ClearOverride() { OverrideMode = null; OverrideUntilUtc = null; }

        public void Validate()
        {
            if (Version != 2) throw new InvalidDataException("validation.version");
            if (LightMinute < 0 || LightMinute >= 1440 || DarkMinute < 0 || DarkMinute >= 1440)
                throw new InvalidDataException("validation.time");
            if (LightMinute == DarkMinute) throw new InvalidDataException("validation.equalTimes");
            if (Language != "en" && Language != "zh-CN") throw new InvalidDataException("validation.language");
            DateTimeOffset until;
            if (OverrideMode != null && OverrideMode != "Light" && OverrideMode != "Dark")
                throw new InvalidDataException("validation.override");
            if ((OverrideMode == null) != (OverrideUntilUtc == null) ||
                (OverrideUntilUtc != null && !DateTimeOffset.TryParse(OverrideUntilUtc,
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out until)))
                throw new InvalidDataException("validation.expiry");
        }
    }

    public static class Schedule
    {
        // The light interval is [LightMinute, DarkMinute), including when it crosses midnight.
        public static ThemeMode At(Settings settings, DateTime localNow)
        {
            settings.Validate();
            double minute = localNow.TimeOfDay.TotalMinutes;
            bool light = settings.LightMinute < settings.DarkMinute
                ? minute >= settings.LightMinute && minute < settings.DarkMinute
                : minute >= settings.LightMinute || minute < settings.DarkMinute;
            return light ? ThemeMode.Light : ThemeMode.Dark;
        }

        public static DateTime Next(Settings settings, DateTime localNow)
        {
            settings.Validate();
            DateTime light = localNow.Date.AddMinutes(settings.LightMinute);
            DateTime dark = localNow.Date.AddMinutes(settings.DarkMinute);
            if (light <= localNow) light = light.AddDays(1);
            if (dark <= localNow) dark = dark.AddDays(1);
            return light < dark ? light : dark;
        }

        public static DateTimeOffset NextInstant(Settings settings, DateTimeOffset now)
        {
            DateTime next = Next(settings, now.LocalDateTime);
            // If a DST change skips the selected time, resume at the next real local minute.
            while (TimeZoneInfo.Local.IsInvalidTime(next)) next = next.AddMinutes(1);
            return new DateTimeOffset(next);
        }

        public static bool TryOverride(Settings settings, DateTimeOffset now, out ThemeMode mode)
        {
            DateTimeOffset until;
            mode = ThemeMode.Light;
            if (settings.OverrideMode == null || !DateTimeOffset.TryParse(settings.OverrideUntilUtc,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out until) || now >= until) return false;
            mode = settings.OverrideMode == "Dark" ? ThemeMode.Dark : ThemeMode.Light;
            return true;
        }

        public static string TimeText(int minute)
        {
            return (minute / 60).ToString("00") + ":" + (minute % 60).ToString("00");
        }
    }

    public sealed class SettingsStore
    {
        public string FilePath { get; private set; }
        public string Warning { get; private set; }
        public SettingsStore(string directory) { FilePath = Path.Combine(directory, "settings.json"); }

        private Settings Read(string path)
        {
            using (var stream = File.OpenRead(path))
            {
                var value = (Settings)new DataContractJsonSerializer(typeof(Settings)).ReadObject(stream);
                if (value == null) throw new InvalidDataException("validation.empty");
                value.Validate();
                return value;
            }
        }

        public Settings Load()
        {
            Warning = null;
            if (!File.Exists(FilePath))
            {
                if (File.Exists(FilePath + ".bak"))
                {
                    try
                    {
                        Settings backup = Read(FilePath + ".bak");
                        Save(backup);
                        Warning = "warning.missing";
                        return backup;
                    }
                    catch (SerializationException) { }
                    catch (InvalidDataException) { }
                    catch (System.Xml.XmlException) { }
                }
                Settings fresh = new Settings();
                Save(fresh);
                return fresh;
            }
            try { return Read(FilePath); }
            catch (Exception error)
            {
                if (!(error is SerializationException) && !(error is InvalidDataException) &&
                    !(error is System.Xml.XmlException)) throw;
                Settings recovered = null;
                if (File.Exists(FilePath + ".bak"))
                {
                    try { recovered = Read(FilePath + ".bak"); }
                    catch (SerializationException) { }
                    catch (InvalidDataException) { }
                    catch (System.Xml.XmlException) { }
                }
                // Keep the bad file for diagnosis, and never replace a valid backup with it.
                File.Move(FilePath, FilePath + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
                bool usedBackup = recovered != null;
                recovered = recovered ?? new Settings();
                Save(recovered);
                Warning = usedBackup ? "warning.backup" : "warning.defaults";
                return recovered;
            }
        }

        public void Save(Settings settings)
        {
            settings.Validate();
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            string temporary = FilePath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    new DataContractJsonSerializer(typeof(Settings)).WriteObject(stream, settings);
                    stream.Flush(true);
                }
                if (File.Exists(FilePath)) File.Replace(temporary, FilePath, FilePath + ".bak", true);
                else File.Move(temporary, FilePath);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    public interface IThemeAccess
    {
        bool Apply(ThemeMode mode, bool apps, bool system);
    }

    public sealed class Evaluation
    {
        public ThemeMode? Mode;
        public bool Temporary;
        public bool Changed;
        public bool HighContrast;
        public bool SwitchingOff;
    }

    public sealed class ThemeEngine
    {
        private readonly SettingsStore store;
        private readonly IThemeAccess access;
        public Settings Settings { get; private set; }
        public ThemeEngine(Settings settings, SettingsStore store, IThemeAccess access)
        {
            settings.Validate();
            Settings = settings.Copy(); this.store = store; this.access = access;
        }

        public void Update(Settings settings)
        {
            settings.Validate();
            store.Save(settings); // Do not update the live schedule if saving fails.
            Settings = settings.Copy();
        }

        public Evaluation Evaluate(DateTimeOffset now, bool highContrast)
        {
            var result = new Evaluation { HighContrast = highContrast, SwitchingOff = !Settings.SwitchWindowsTheme };
            if (!Settings.Enabled || highContrast || !Settings.SwitchWindowsTheme) return result;
            ThemeMode desired;
            result.Temporary = Schedule.TryOverride(Settings, now, out desired);
            if (!result.Temporary)
            {
                if (Settings.OverrideMode != null)
                {
                    Settings clean = Settings.Copy(); clean.ClearOverride(); Update(clean);
                }
                desired = Schedule.At(Settings, now.LocalDateTime);
            }
            result.Mode = desired;
            result.Changed = access.Apply(desired, true, true);
            return result;
        }

        public Evaluation Manual(ThemeMode mode, DateTimeOffset now, bool highContrast)
        {
            if (highContrast || !Settings.SwitchWindowsTheme)
                return new Evaluation { HighContrast = highContrast, SwitchingOff = !Settings.SwitchWindowsTheme };
            Settings next = Settings.Copy();
            next.ClearOverride();
            if (next.Enabled)
            {
                string until = Schedule.NextInstant(next, now).ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
                next.OverrideMode = mode.ToString();
                next.OverrideUntilUtc = until;
            }
            Update(next);
            return new Evaluation { Mode = mode, Temporary = next.Enabled,
                Changed = access.Apply(mode, true, true) };
        }
    }
}
