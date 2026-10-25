using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO.Compression;

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
        private static void Equal(int expected, int actual, string name) {
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
            True(!UpdateValidation.IsSafeEntry("CharacterTracker.Updater.exe"), "helper updater rejected");
            Throws(() => UpdateValidation.ReadBoundedAsync(new MemoryStream(new byte[11]), 10, CancellationToken.None).GetAwaiter().GetResult(), "bounded response rejects oversized body");
            using (var canceled = new CancellationTokenSource()) {
                canceled.Cancel(); Throws(() => UpdateValidation.ReadBoundedAsync(new MemoryStream(new byte[1]), 10, canceled.Token).GetAwaiter().GetResult(), "bounded response honors cancellation");
            }
            TestPackageLimits();
            TrackerRuntimeMetrics.Reset();
            int visibleTicks = 60, hiddenTicks = 60;
            for (int i = 0; i < visibleTicks; i++) if (TrackerRefreshPolicy.ShouldAcquireActors(true, false, false, false)) TrackerRuntimeMetrics.ActorRead();
            for (int i = 0; i < hiddenTicks; i++) if (TrackerRefreshPolicy.ShouldAcquireActors(false, false, false, false)) TrackerRuntimeMetrics.ActorRead();
            Equal(60, (int)TrackerRuntimeMetrics.ActorReadCount, "visible/invisible actor read simulation (60/0)");
            Equal(100, TrackerRefreshPolicy.PollInterval(true, true, false, false, 100, 500), "visible arrow poll rate");
            Equal(1000, TrackerRefreshPolicy.PollInterval(false, false, false, false, 100, 500), "all hidden poll rate");
            var renderGate = new OverlayRenderGate();
            True(renderGate.Update("same"), "first overlay state requests render");
            True(!renderGate.Update("same"), "unchanged overlay state suppresses render");
            True(renderGate.Update("changed"), "changed overlay state requests render");
            TestSettingsPersistence();
            TestSettingsRecovery();
            TestMapShutdown();
            TestMapCacheReuse();
            if (failures == 0) Console.WriteLine("All tests passed."); return failures == 0 ? 0 : 1;
        }

        private static void TestPackageLimits()
        {
            string path = Path.Combine(Path.GetTempPath(), "AT-zip-" + Guid.NewGuid().ToString("N") + ".zip");
            SemanticVersion expected;
            SemanticVersion.TryParse("1.6.2", out expected);
            try {
                using (var archive = ZipFile.Open(path, ZipArchiveMode.Create)) {
                    var entry = archive.CreateEntry("CharacterTracker.dll", CompressionLevel.Optimal);
                    using (var output = entry.Open()) {
                        byte[] zeros = new byte[64 * 1024];
                        for (int i = 0; i < 1025; i++) output.Write(zeros, 0, zeros.Length);
                    }
                }
                Throws(() => UpdateValidation.ValidatePackage(path, expected), "ZIP expanded-size limit rejects package bomb");
            } finally { try { File.Delete(path); } catch { } }
            try {
                using (var archive = ZipFile.Open(path, ZipArchiveMode.Create)) {
                    using (var writer = new StreamWriter(archive.CreateEntry("../escape.txt").Open())) writer.Write("unsafe");
                }
                Throws(() => UpdateValidation.ValidatePackage(path, expected), "package validator rejects Zip Slip path");
            } finally { try { File.Delete(path); } catch { } }
        }

        private static void TestSettingsRecovery()
        {
            string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AetherTracker-recovery-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "settings.xml");
            try {
                var saved = NewSettingsObject(path); saved.OpacityPercent = 73;
                File.WriteAllBytes(path + ".bak.1", saved.SerializeSnapshot());
                File.WriteAllText(path, "<broken");
                var recovered = TrackerSettings.Load(path, new CultureInfo("en-US"));
                Equal(73, recovered.OpacityPercent, "valid settings backup recovery");
                True(File.Exists(path) && File.ReadAllText(path) == "<broken", "recovery leaves damaged primary intact");
                var atomic = new AtomicSettingsFileStore();
                byte[] next = recovered.SerializeSnapshot(); atomic.WriteAtomically(path, next);
                True(BytesEqual(next, File.ReadAllBytes(path)), "atomic settings replacement writes complete primary");
                True(Directory.GetFiles(directory, "settings.xml.bak.*").Length >= 1, "atomic settings replacement keeps recovery backup");
            } finally { try { Directory.Delete(directory, true); } catch { } }
        }

        private static void TestSettingsPersistence()
        {
            string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AetherTracker-settings-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "settings.xml");
            try {
                byte[] saved = NewSettings(path);
                var settings = NewSettingsObject(path);
                saved = settings.GetPersistedSnapshot();
                var store = new CountingSettingsStore();
                var timer = new ManualSettingsTimer();
                using (var saver = new SettingsSaveCoordinator(settings, store, timer)) {
                    Equal(SettingsSaveCoordinator.SaveIntervalMilliseconds, saver.Interval, "settings fixed 60-second interval");
                    saver.Start();
                    timer.Fire(); saver.CheckAndSaveAsync().GetAwaiter().GetResult();
                    Equal(0, store.Writes, "unchanged settings do not write");
                    settings.OpacityPercent = 80; settings.Save(); settings.OpacityPercent = 70; settings.Save();
                    timer.Fire(); saver.CheckAndSaveAsync().GetAwaiter().GetResult();
                    Equal(1, store.Writes, "continuous edits coalesce to latest snapshot");
                    settings.OpacityPercent = 70; timer.Fire(); saver.CheckAndSaveAsync().GetAwaiter().GetResult();
                    Equal(1, store.Writes, "same snapshot does not write again");
                    settings.OpacityPercent = 61; settings.Save();
                    True(saver.StopAndFlush(), "shutdown final save succeeds");
                    var reloaded = LoadSnapshot(store.LastWritten);
                    Equal(61, reloaded.OpacityPercent, "shutdown final change reloads");
                    int finalWrites = store.Writes;
                    settings.OpacityPercent = 50;
                    timer.Fire();
                    Equal(finalWrites, store.Writes, "no save work after shutdown");
                }

                var failedSettings = NewSettingsObject(path);
                saved = failedSettings.GetPersistedSnapshot();
                byte[] oldContents = (byte[])saved.Clone();
                var failingStore = new CountingSettingsStore { FailWrites = true };
                var retryTimer = new ManualSettingsTimer();
                using (var saver = new SettingsSaveCoordinator(failedSettings, failingStore, retryTimer)) {
                    saver.Start(); failedSettings.OpacityPercent = 47; retryTimer.Fire(); saver.CheckAndSaveAsync().GetAwaiter().GetResult();
                    True(saver.LastError != null, "write failure is retained");
                    True(BytesEqual(oldContents, failedSettings.GetPersistedSnapshot()), "failed write preserves old saved bytes");
                    failingStore.FailWrites = false; retryTimer.Fire(); saver.CheckAndSaveAsync().GetAwaiter().GetResult();
                    True(saver.LastError == null, "next interval retries failed dirty state");
                    True(BytesEqual(failedSettings.SerializeSnapshot(), failingStore.LastWritten), "retry stores latest settings snapshot");
                    True(saver.StopAndFlush(), "retry shutdown succeeds");
                }

                var slowSettings = NewSettingsObject(path);
                var slowStore = new CountingSettingsStore { BlockWrites = true };
                var slowTimer = new ManualSettingsTimer();
                var slowSaver = new SettingsSaveCoordinator(slowSettings, slowStore, slowTimer);
                slowSaver.Start(); slowSettings.OpacityPercent = 39; slowTimer.Fire();
                True(slowStore.Entered.Wait(5000), "in-progress save enters store");
                Task<bool> stopping = Task.Run(() => slowSaver.StopAndFlush());
                Thread.Sleep(30); True(!stopping.IsCompleted, "shutdown waits for in-progress save");
                slowStore.Release.Set(); True(stopping.GetAwaiter().GetResult(), "shutdown finishes after in-progress save");
                int writesAtShutdown = slowStore.Writes; Thread.Sleep(50);
                Equal(writesAtShutdown, slowStore.Writes, "no writes after shutdown completes");
                slowSaver.Dispose();
            } finally { try { Directory.Delete(directory, true); } catch { } }
        }

        private static byte[] NewSettings(string path)
        {
            var value = NewSettingsObject(path);
            return value.SerializeSnapshot();
        }

        private static TrackerSettings NewSettingsObject(string path)
        {
            var value = new TrackerSettings { FilePath = path, Language = "en" };
            byte[] initial = value.SerializeSnapshot(); value.SetPersistedSnapshot(initial); return value;
        }

        private static TrackerSettings LoadSnapshot(byte[] snapshot)
        {
            using (var stream = new MemoryStream(snapshot))
                return (TrackerSettings)new System.Xml.Serialization.XmlSerializer(typeof(TrackerSettings)).Deserialize(stream);
        }

        private static void TestMapShutdown()
        {
            string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AetherTracker-map-test-" + Guid.NewGuid().ToString("N"));
            var source = new BlockingMapSource(); var store = new CountingMapStore();
            var service = new MapImageService(directory, source, store);
            int callbacks = 0;
            Task request = service.BeginLoadForTerritory(777, (data, error) => { Interlocked.Increment(ref callbacks); if (data != null) data.Dispose(); });
            True(source.AssetStarted.Wait(5000), "map asset request reaches controlled wait");
            service.StopAsync().GetAwaiter().GetResult(); request.GetAwaiter().GetResult();
            int writesAfterStop = store.Writes; Thread.Sleep(50);
            Equal(writesAfterStop, store.Writes, "map cache stops writing after shutdown returns");
            Equal(0, callbacks, "stopped map request does not dispatch UI callback");
            service.Dispose();
            try { if (Directory.Exists(directory)) Directory.Delete(directory, true); } catch { }
        }

        private static void TestMapCacheReuse()
        {
            string directory = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "AT-" + Guid.NewGuid().ToString("N")));
            try {
                Directory.CreateDirectory(directory);
                string fakeHash = new string('A', 64);
                for (int id = 1; id <= 66; id++) {
                    File.WriteAllText(Path.Combine(directory, "map-" + id + ".cache"), "fixture");
                    File.WriteAllBytes(Path.Combine(directory, "map-" + id + "-" + fakeHash + ".png"), new byte[] { 1 });
                }
                File.WriteAllText(Path.Combine(directory, "map-0.cache"), "stale");
                File.WriteAllBytes(Path.Combine(directory, "map-0-" + fakeHash + ".png"), new byte[] { 1 });
                File.SetLastWriteTimeUtc(Path.Combine(directory, "map-0.cache"), DateTime.UtcNow.AddDays(-200));
                File.SetLastWriteTimeUtc(Path.Combine(directory, "map-0-" + fakeHash + ".png"), DateTime.UtcNow.AddDays(-200));
                for (int id = 1; id <= 513; id++) File.WriteAllText(Path.Combine(directory, "territory-" + id + ".txt"), "1");
                var source = new StaticMapSource(CreatePng());
                using (var service = new MapImageService(directory, source, new AtomicMapCacheStore())) {
                    MapData loaded = null;
                    service.BeginLoad(9876, (data, error) => { loaded = data; if (error != null) throw new Exception(error); }).GetAwaiter().GetResult();
                    True(loaded != null && loaded.Image.Width == 4, "map downloaded and dimension-validated");
                    True(service.CacheWriteCount > 0, "bounded map cache files written");
                    if (loaded != null) loaded.Dispose();
                    Equal(1, source.ImageRequests, "first map load downloads image once");
                    Equal(64, Directory.GetFiles(directory, "map-*.cache").Length, "map cache count is pruned to limit");
                    Equal(512, Directory.GetFiles(directory, "territory-*.txt").Length, "territory mapping count is pruned to limit");
                    True(!File.Exists(Path.Combine(directory, "map-0.cache")), "map cache TTL removes entries older than 180 days");
                    True(service.CacheWriteBytes > 0 && service.CacheWriteCount == 2, "logical cache writes counted (image plus manifest)");
                }
                var cacheOnly = new StaticMapSource(null);
                using (var service = new MapImageService(directory, cacheOnly, new AtomicMapCacheStore())) {
                    MapData loaded = null;
                    service.BeginLoad(9876, (data, error) => { loaded = data; if (error != null) throw new Exception(error); }).GetAwaiter().GetResult();
                    True(loaded != null && loaded.Image.Width == 4, "map cache reused");
                    if (loaded != null) loaded.Dispose();
                    Equal(0, cacheOnly.ImageRequests, "cached map causes zero network image downloads");
                }
                True(Directory.GetFiles(directory, "map-*.cache").Length == 64, "bounded schema manifest written");
            } finally { try { if (Directory.Exists(directory)) Directory.Delete(directory, true); } catch { } }
        }

        private static byte[] CreatePng()
        {
            using (var bitmap = new Bitmap(4, 4)) using (var stream = new MemoryStream()) { bitmap.Save(stream, ImageFormat.Png); return stream.ToArray(); }
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private sealed class ManualSettingsTimer : ISettingsSaveTimer
        {
            public event EventHandler Tick; public int Interval { get; private set; } = 60000;
            public void Start() { } public void Stop() { } public void Dispose() { }
            public void Fire() { var handler = Tick; if (handler != null) handler(this, EventArgs.Empty); }
        }

        private sealed class CountingSettingsStore : ISettingsFileStore
        {
            private int writes;
            public int Writes { get { return Volatile.Read(ref writes); } }
            public byte[] LastWritten;
            public bool FailWrites; public bool BlockWrites;
            public readonly ManualResetEventSlim Entered = new ManualResetEventSlim(false);
            public readonly ManualResetEventSlim Release = new ManualResetEventSlim(false);
            public void WriteAtomically(string path, byte[] contents)
            {
                Interlocked.Increment(ref writes); Entered.Set();
                if (BlockWrites) Release.Wait();
                if (FailWrites) { LastWritten = null; throw new IOException("simulated write failure"); }
                LastWritten = (byte[])contents.Clone();
            }
        }

        private sealed class CountingMapStore : IMapCacheStore
        {
            private int writes; public int Writes { get { return Volatile.Read(ref writes); } }
            public void WriteAtomically(string path, byte[] contents) { Interlocked.Increment(ref writes); }
        }

        private sealed class BlockingMapSource : IMapDataSource
        {
            public readonly ManualResetEventSlim AssetStarted = new ManualResetEventSlim(false);
            public Task<string> DownloadStringAsync(string url, CancellationToken token)
            {
                if (url.Contains("TerritoryType")) return Task.FromResult("{\"Map@as(raw)\": 123}");
                return Task.FromResult("{\"Id\":\"asset-123\",\"SizeFactor\":100,\"OffsetX\":0,\"OffsetY\":0}");
            }
            public Task<byte[]> DownloadBytesAsync(string url, CancellationToken token)
            {
                AssetStarted.Set();
                var completion = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                token.Register(() => completion.TrySetCanceled());
                return completion.Task;
            }
        }

        private sealed class StaticMapSource : IMapDataSource
        {
            private readonly byte[] image;
            public int ImageRequests;
            public StaticMapSource(byte[] value) { image = value; }
            public Task<string> DownloadStringAsync(string url, CancellationToken token)
            { return Task.FromResult("{\"Id\":\"fixture-asset\",\"SizeFactor\":100,\"OffsetX\":0,\"OffsetY\":0}"); }
            public Task<byte[]> DownloadBytesAsync(string url, CancellationToken token)
            { Interlocked.Increment(ref ImageRequests); if (image == null) throw new IOException("Unexpected network image request with cache present."); return Task.FromResult((byte[])image.Clone()); }
        }
    }
}
