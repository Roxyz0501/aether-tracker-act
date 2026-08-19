using System;
using System.Drawing;
using System.Globalization;
using System.IO;

namespace CharacterTracker.Tests
{
    internal static class Program
    {
        private static int failures;
        private static void Equal(double expected, double actual, double tolerance, string name) {
            if (Math.Abs(expected - actual) > tolerance) { Console.Error.WriteLine("FAIL " + name + ": " + actual); failures++; }
        }
        private static void Equal(string expected, string actual, string name) {
            if (expected != actual) { Console.Error.WriteLine("FAIL " + name + ": " + actual); failures++; }
        }
        private static void True(bool value, string name) {
            if (!value) { Console.Error.WriteLine("FAIL " + name); failures++; }
        }
        private static void Throws(Action action, string name) {
            try { action(); Console.Error.WriteLine("FAIL " + name + ": no exception"); failures++; } catch { }
        }
        public static int Main() {
            var a = new CalibrationPoint { TerritoryId = 777, WorldX = -100, WorldY = 50, MapX = 18.5, MapY = 24.0 };
            var b = new CalibrationPoint { TerritoryId = 777, WorldX = 100, WorldY = 150, MapX = 22.5, MapY = 26.0 };
            var c = MapCalibration.FromTwoPoints(777, a, b);
            Equal(20.5, c.MapX(0), .0001, "map x"); Equal(25.0, c.MapY(100), .0001, "map y");
            var p = new ActorSnapshot { X = 0, Y = 0, Heading = 0 };
            Equal(0, TrackingMath.RelativeDirectionRadians(p, new ActorSnapshot { X = 0, Y = 10 }, true), .0001, "ahead");
            Equal(Math.PI / 2, TrackingMath.RelativeDirectionRadians(p, new ActorSnapshot { X = 10, Y = 0 }, true), .0001, "right");
            p.Heading = (float)(Math.PI / 2);
            Equal(0, TrackingMath.RelativeDirectionRadians(p, new ActorSnapshot { X = 10, Y = 0 }, true), .0001, "heading");
            using (var map = new MapData { SizeFactor = 100, OffsetX = 0, OffsetY = 0, Image = new Bitmap(2048, 2048) }) {
                var center = map.WorldToImage(0, 0);
                Equal(1024, center.X, .0001, "map image center x"); Equal(1024, center.Y, .0001, "map image center y");
            }

            Equal("ja", Localization.DetectLanguage(new CultureInfo("ja-JP")), "OS Japanese mapping");
            Equal("zh-Hans", Localization.DetectLanguage(new CultureInfo("zh-TW")), "OS Chinese mapping");
            Equal("ko", Localization.DetectLanguage(new CultureInfo("ko-KR")), "OS Korean mapping");
            Equal("en", Localization.DetectLanguage(new CultureInfo("fr-FR")), "OS fallback mapping");
            var firstRun = new TrackerSettings();
            True(TrackerSettings.EnsureLanguage(firstRun, new CultureInfo("ko-KR")) && firstRun.Language == "ko", "first-run OS language selection");
            firstRun.Language = "ja";
            True(!TrackerSettings.EnsureLanguage(firstRun, new CultureInfo("en-US")) && firstRun.Language == "ja", "saved language is retained");
            Localization.SetLanguage("ja"); Equal("支援", Localization.T("tab.support"), "Japanese support tab");
            Equal("English fallback", Localization.T("test.fallback"), "translation fallback");
            Localization.SetLanguage("invalid"); Equal("Support", Localization.T("tab.support"), "invalid language fallback");
            True(Localization.T("tab.support").IndexOf("★", StringComparison.Ordinal) < 0 && Localization.T("tab.support").IndexOf("☆", StringComparison.Ordinal) < 0, "no star in support tab");

            SemanticVersion v160, v159, pre;
            True(SemanticVersion.TryParse("v1.6.0", out v160), "SemVer tag parse");
            True(SemanticVersion.TryParse("1.5.9", out v159) && v160.CompareTo(v159) > 0, "SemVer comparison");
            True(SemanticVersion.TryParse("1.6.0-beta.1", out pre) && v160.CompareTo(pre) > 0, "stable after prerelease");
            True(!SemanticVersion.TryParse("1.6", out pre), "reject incomplete SemVer");

            string releaseJson = "{\"tag_name\":\"v1.6.1\",\"name\":\"Stable\",\"body\":\"Notes\",\"draft\":false,\"prerelease\":false,\"assets\":[{\"name\":\"Aether-Tracker-ACT-v1.6.1.zip\",\"browser_download_url\":\"https://github.com/owner/repo/releases/download/v1.6.1/Aether-Tracker-ACT-v1.6.1.zip\"}]}";
            var release = UpdateValidation.ParseRelease(releaseJson);
            Equal("v1.6.1", release.TagName, "release response tag"); True(!release.Draft && !release.Prerelease && release.Assets.Count == 1, "stable release parsing");
            True(UpdateValidation.IsUpdateAvailable("1.6.0", release), "update available scenario");
            release.TagName = "v1.6.0"; True(!UpdateValidation.IsUpdateAvailable("1.6.0", release), "no update scenario");
            release.TagName = "v1.6.1"; release.Prerelease = true; True(!UpdateValidation.IsUpdateAvailable("1.6.0", release), "prerelease excluded"); release.Prerelease = false;
            Throws(() => UpdateValidation.ParseRelease("{}"), "invalid release response");

            string temp = Path.GetTempFileName();
            try {
                File.WriteAllText(temp, "Aether Tracker update test");
                string hash = UpdateValidation.Sha256(temp);
                True(UpdateValidation.VerifySha256(temp, hash + "  " + Path.GetFileName(temp), Path.GetFileName(temp)), "SHA-256 valid");
                True(!UpdateValidation.VerifySha256(temp, new string('0', 64) + "  " + Path.GetFileName(temp), Path.GetFileName(temp)), "SHA-256 corrupted asset");
            } finally { File.Delete(temp); }
            True(UpdateValidation.IsSafeEntry("CharacterTracker.dll"), "safe package entry");
            True(!UpdateValidation.IsSafeEntry("../CharacterTracker.dll"), "Zip Slip parent rejected");
            True(!UpdateValidation.IsSafeEntry("C:\\CharacterTracker.dll"), "absolute path rejected");
            True(!UpdateValidation.IsSafeEntry("OtherPlugin.dll"), "unexpected plugin rejected");
            if (failures == 0) Console.WriteLine("All tests passed."); return failures == 0 ? 0 : 1;
        }
    }
}
