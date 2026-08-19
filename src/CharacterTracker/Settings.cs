using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Xml.Serialization;

namespace CharacterTracker
{
    [Serializable]
    public sealed class TrackerSettings
    {
        public string Language { get; set; }
        public bool CheckUpdatesOnStartup { get; set; } = true;
        public string LastUpdateCheckUtc { get; set; } = "";
        public string SkippedVersion { get; set; } = "";
        public bool OverlayEnabled { get; set; } = true;
        public bool ListOverlayEnabled { get; set; } = true;
        public bool MapOverlayEnabled { get; set; } = false;
        // false が既定。既存設定ファイルに項目がない場合も、最初から配置を変更できる。
        public bool OverlayLocked { get; set; } = false;
        public bool PlayerFacingUp { get; set; } = true;
        public bool HideWhenGameInactive { get; set; } = true;
        public bool ShowPlayers { get; set; } = true;
        public bool ShowNpcs { get; set; } = true;
        public bool ShowOthers { get; set; } = true;
        public int OpacityPercent { get; set; } = 96;
        public int BackgroundOpacityPercent { get; set; } = 100;
        public string SearchText { get; set; } = "";
        public int ListMaxRows { get; set; } = 18;
        public int ArrowOffsetX { get; set; } = 0;
        public int ArrowOffsetY { get; set; } = 45;
        public int ArrowWidth { get; set; } = 300;
        public int ArrowHeight { get; set; } = 150;
        public int ListOffsetX { get; set; } = 0;
        public int ListOffsetY { get; set; } = 0;
        public int ListWidth { get; set; } = 610;
        public int ListHeight { get; set; } = 551;
        public int MapOffsetX { get; set; } = 0;
        public int MapOffsetY { get; set; } = 0;
        public int MapWidth { get; set; } = 540;
        public int MapHeight { get; set; } = 574;
        public int LastMapId { get; set; } = 0;
        public uint LastMapTerritoryId { get; set; } = 0;
        public int PollIntervalMs { get; set; } = 100;
        public int ListRefreshMs { get; set; } = 500;
        public List<MapCalibration> Calibrations { get; set; } = new List<MapCalibration>();

        [XmlIgnore]
        public string FilePath { get; set; }

        public MapCalibration FindCalibration(uint territoryId) { return Calibrations.Find(c => c.TerritoryId == territoryId); }

        public void PutCalibration(MapCalibration calibration)
        {
            Calibrations.RemoveAll(c => c.TerritoryId == calibration.TerritoryId);
            Calibrations.Add(calibration);
            Save();
        }

        public void Save()
        {
            if (string.IsNullOrEmpty(FilePath)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            string temp = FilePath + ".tmp";
            using (var stream = File.Create(temp)) new XmlSerializer(typeof(TrackerSettings)).Serialize(stream, this);
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, null); else File.Move(temp, FilePath);
        }

        public static TrackerSettings Load()
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Advanced Combat Tracker", "Config", "CharacterTracker.config.xml");
            try {
                if (File.Exists(path)) {
                    using (var stream = File.OpenRead(path)) {
                        var value = (TrackerSettings)new XmlSerializer(typeof(TrackerSettings)).Deserialize(stream);
                        value.FilePath = path;
                        if (EnsureLanguage(value, CultureInfo.CurrentUICulture)) value.Save();
                        return value;
                    }
                }
            } catch { }
            var created = new TrackerSettings { FilePath = path, Language = Localization.DetectLanguage(CultureInfo.CurrentUICulture) };
            created.Save();
            return created;
        }

        public static bool EnsureLanguage(TrackerSettings value, CultureInfo uiCulture)
        {
            if (value == null) throw new ArgumentNullException("value");
            string selected = string.IsNullOrWhiteSpace(value.Language)
                ? Localization.DetectLanguage(uiCulture)
                : Localization.NormalizeLanguage(value.Language);
            bool changed = !string.Equals(value.Language, selected, StringComparison.Ordinal);
            value.Language = selected;
            return changed;
        }
    }
}
