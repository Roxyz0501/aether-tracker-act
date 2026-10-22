using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace CharacterTracker
{
    [Serializable]
    public sealed class TrackerSettings
    {
        private byte[] persistedSnapshot;
        private int changeHint;

        public string Language { get; set; }
        public bool CheckUpdatesOnStartup { get; set; } = true;
        public string LastUpdateCheckUtc { get; set; } = "";
        public string SkippedVersion { get; set; } = "";
        public bool OverlayEnabled { get; set; } = true;
        public bool ListOverlayEnabled { get; set; } = true;
        public bool MapOverlayEnabled { get; set; } = false;
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

        // Kept for existing callers. It records a possible mutation; disk I/O is owned by
        // SettingsSaveCoordinator and occurs on its fixed interval or during shutdown.
        public void Save() { Interlocked.Exchange(ref changeHint, 1); }

        internal byte[] SerializeSnapshot()
        {
            using (var stream = new MemoryStream()) {
                new XmlSerializer(typeof(TrackerSettings)).Serialize(stream, this);
                return stream.ToArray();
            }
        }

        internal byte[] GetPersistedSnapshot()
        {
            return persistedSnapshot == null ? SerializeSnapshot() : (byte[])persistedSnapshot.Clone();
        }

        internal void SetPersistedSnapshot(byte[] value)
        {
            persistedSnapshot = value == null ? null : (byte[])value.Clone();
            Interlocked.Exchange(ref changeHint, 0);
        }

        public static TrackerSettings Load()
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Advanced Combat Tracker", "Config", "CharacterTracker.config.xml");
            return Load(path, CultureInfo.CurrentUICulture);
        }

        internal static TrackerSettings Load(string path, CultureInfo uiCulture)
        {
            try {
                if (File.Exists(path)) {
                    using (var stream = File.OpenRead(path)) {
                        var value = (TrackerSettings)new XmlSerializer(typeof(TrackerSettings)).Deserialize(stream);
                        value.FilePath = path;
                        // A canonical snapshot of the successfully loaded values is the baseline.
                        // Missing fields in older Config files therefore do not cause a rewrite.
                        value.SetPersistedSnapshot(value.SerializeSnapshot());
                        if (EnsureLanguage(value, uiCulture)) value.Save();
                        return value;
                    }
                }
            } catch { }
            var created = new TrackerSettings { FilePath = path, Language = Localization.DetectLanguage(uiCulture) };
            byte[] initial = created.SerializeSnapshot();
            new AtomicSettingsFileStore().WriteAtomically(path, initial);
            created.SetPersistedSnapshot(initial);
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

    internal interface ISettingsSaveTimer : IDisposable
    {
        event EventHandler Tick;
        int Interval { get; }
        void Start();
        void Stop();
    }

    internal sealed class WinFormsSettingsSaveTimer : ISettingsSaveTimer
    {
        private readonly System.Windows.Forms.Timer timer;
        public event EventHandler Tick;
        public int Interval { get { return timer.Interval; } }

        public WinFormsSettingsSaveTimer(int interval)
        {
            timer = new System.Windows.Forms.Timer { Interval = interval };
            timer.Tick += (s, e) => { var handler = Tick; if (handler != null) handler(this, EventArgs.Empty); };
        }

        public void Start() { timer.Start(); }
        public void Stop() { timer.Stop(); }
        public void Dispose() { timer.Dispose(); }
    }

    internal interface ISettingsFileStore
    {
        void WriteAtomically(string path, byte[] contents);
    }

    internal sealed class SettingsSaveFailedEventArgs : EventArgs
    {
        public string Message { get; private set; }
        public SettingsSaveFailedEventArgs(string message) { Message = message ?? "Unknown error"; }
    }

    internal sealed class AtomicSettingsFileStore : ISettingsFileStore
    {
        public void WriteAtomically(string path, byte[] contents)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            string directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);
            string temporary = path + ".tmp." + Guid.NewGuid().ToString("N");
            try {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                    stream.Write(contents, 0, contents.Length);
                    stream.Flush(true);
                }
                if (File.Exists(path)) ReplaceWithMove(temporary, path);
                else {
                    try { File.Move(temporary, path); }
                    catch (UnauthorizedAccessException) when (File.Exists(path)) { ReplaceWithMove(temporary, path); }
                }
            } finally {
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
            }
        }

        internal static void ReplaceWithMove(string temporary, string path)
        {
            string backup = path + ".bak." + Guid.NewGuid().ToString("N");
            File.Move(path, backup);
            try { File.Move(temporary, path); try { File.Delete(backup); } catch { } }
            catch {
                if (!File.Exists(path) && File.Exists(backup)) File.Move(backup, path);
                throw;
            }
        }
    }

    internal sealed class SettingsSaveCoordinator : IDisposable
    {
        public const int SaveIntervalMilliseconds = 60000;

        private readonly object gate = new object();
        private readonly TrackerSettings settings;
        private readonly ISettingsFileStore fileStore;
        private readonly ISettingsSaveTimer timer;
        private byte[] savedSnapshot;
        private byte[] scheduledSnapshot;
        private Task<bool> tail = Task.FromResult(true);
        private Task<bool> lastTick = Task.FromResult(false);
        private Task<bool> shutdownTask;
        private bool accepting = true;
        private string lastError;

        public int Interval { get { return timer.Interval; } }
        public string LastError { get { lock (gate) return lastError; } }
        public event EventHandler<SettingsSaveFailedEventArgs> SaveFailed;

        public SettingsSaveCoordinator(TrackerSettings value, ISettingsFileStore store = null, ISettingsSaveTimer saveTimer = null)
        {
            settings = value ?? throw new ArgumentNullException("value");
            fileStore = store ?? new AtomicSettingsFileStore();
            timer = saveTimer ?? new WinFormsSettingsSaveTimer(SaveIntervalMilliseconds);
            savedSnapshot = settings.GetPersistedSnapshot();
            scheduledSnapshot = (byte[])savedSnapshot.Clone();
            timer.Tick += OnTick;
        }

        public void Start() { lock (gate) { if (accepting) timer.Start(); } }

        private void OnTick(object sender, EventArgs e)
        {
            Task<bool> current;
            try { current = CheckAndSaveAsync(); }
            catch (Exception ex) { lock (gate) lastError = ex.GetBaseException().Message; return; }
            lock (gate) lastTick = current;
        }

        public Task<bool> CheckAndSaveAsync()
        {
            byte[] snapshot = settings.SerializeSnapshot();
            lock (gate) {
                if (!accepting) return Task.FromResult(false);
                byte[] projected = tail.IsCompleted ? savedSnapshot : scheduledSnapshot;
                if (Equal(snapshot, projected)) return tail.IsCompleted ? Task.FromResult(false) : tail;
                return QueueLocked(snapshot);
            }
        }

        private Task<bool> QueueLocked(byte[] snapshot)
        {
            Task<bool> previous = tail;
            byte[] immutable = (byte[])snapshot.Clone();
            scheduledSnapshot = immutable;
            tail = WriteAfterAsync(previous, immutable);
            return tail;
        }

        private async Task<bool> WriteAfterAsync(Task<bool> previous, byte[] snapshot)
        {
            if (previous != null) { try { await previous.ConfigureAwait(false); } catch { } }
            try {
                string path = settings.FilePath;
                await Task.Run(() => fileStore.WriteAtomically(path, snapshot)).ConfigureAwait(false);
                lock (gate) { savedSnapshot = (byte[])snapshot.Clone(); lastError = null; }
                settings.SetPersistedSnapshot(snapshot);
                return true;
            } catch (Exception ex) {
                string message = ex.GetBaseException().Message;
                EventHandler<SettingsSaveFailedEventArgs> handler = null;
                lock (gate) {
                    if (!string.Equals(lastError, message, StringComparison.Ordinal)) handler = SaveFailed;
                    lastError = message;
                }
                if (handler != null) { try { handler(this, new SettingsSaveFailedEventArgs(message)); } catch { } }
                return false;
            }
        }

        public bool StopAndFlush()
        {
            Task<bool> work;
            lock (gate) {
                if (shutdownTask == null) {
                    accepting = false;
                    timer.Tick -= OnTick;
                    timer.Stop();
                    timer.Dispose();
                    Task<bool> previousTick = lastTick;
                    var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    shutdownTask = completion.Task;
                    Task.Run(async () => {
                        try { completion.TrySetResult(await FlushCoreAsync(previousTick).ConfigureAwait(false)); }
                        catch (Exception ex) {
                            string message = ex.GetBaseException().Message;
                            EventHandler<SettingsSaveFailedEventArgs> handler;
                            lock (gate) { lastError = message; handler = SaveFailed; }
                            if (handler != null) { try { handler(this, new SettingsSaveFailedEventArgs(message)); } catch { } }
                            completion.TrySetResult(false);
                        }
                    });
                }
                work = shutdownTask;
            }
            // FlushCoreAsync and every file write use ConfigureAwait(false)/Task.Run. Blocking
            // ACT's UI thread here therefore cannot wait on a continuation queued to that thread.
            return work.GetAwaiter().GetResult();
        }

        private async Task<bool> FlushCoreAsync(Task<bool> previousTick)
        {
            if (previousTick != null) { try { await previousTick.ConfigureAwait(false); } catch { } }
            byte[] finalSnapshot = settings.SerializeSnapshot();
            Task<bool> write;
            lock (gate) {
                if (Equal(finalSnapshot, savedSnapshot) && tail.IsCompleted) return true;
                if (!tail.IsCompleted && Equal(finalSnapshot, scheduledSnapshot)) write = tail;
                else write = QueueLocked(finalSnapshot);
            }
            bool success;
            try { success = await write.ConfigureAwait(false); } catch { success = false; }
            lock (gate) {
                if (success && Equal(finalSnapshot, savedSnapshot)) return true;
                if (Equal(finalSnapshot, savedSnapshot)) return true;
                // At shutdown only one explicit retry is allowed. A persistent failure is
                // reported and leaves the last successful snapshot/old file intact.
                write = QueueLocked(finalSnapshot);
            }
            try { success = await write.ConfigureAwait(false); } catch { success = false; }
            lock (gate) return success && Equal(finalSnapshot, savedSnapshot);
        }

        private static bool Equal(byte[] left, byte[] right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null || left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
            return true;
        }

        public void Dispose() { StopAndFlush(); }
    }
}
