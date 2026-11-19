using Advanced_Combat_Tracker;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CharacterTracker
{
    public sealed class PositionTrackerPlugin : IActPluginV1
    {
        private Label pluginStatus;
        private PluginControl control;
        private FfxivAdapter adapter;
        private OverlayForm overlay;
        private ActorListOverlayForm listOverlay;
        private MapOverlayForm mapOverlay;
        private MapImageService mapImageService;
        private TrackerSettings settings;
        private System.Windows.Forms.Timer pollTimer, listTimer;
        private SettingsSaveCoordinator settingsSaver;
        private IReadOnlyList<ActorSnapshot> actors = new ActorSnapshot[0];
        private uint playerId, territoryId, trackedId;
        private int requestedMapId;
        private uint requestedTerritoryId;
        private UpdateService updateService;
        private CancellationTokenSource updateCancellation;
        private UpdateResult availableUpdate;
        private UpdateResult preparedUpdate;
        private string registeredPluginPath;
        private readonly object lifecycleGate = new object();
        private readonly HashSet<Task> backgroundTasks = new HashSet<Task>();
        private readonly HashSet<UiDispatchTicket> pendingUiDispatches = new HashSet<UiDispatchTicket>();
        private volatile bool stopping;

        public void SetHostedLanguage(string code) { if (!stopping) control?.SetHostedLanguage(code); }

        public void InitPlugin(TabPage pluginScreenSpace, Label pluginStatusText)
        {
            pluginStatus = pluginStatusText; settings = TrackerSettings.Load(); Localization.SetLanguage(settings.Language); adapter = new FfxivAdapter();
            stopping = false;
            settingsSaver = new SettingsSaveCoordinator(settings);
            settingsSaver.SaveFailed += OnSettingsSaveFailed;
            settingsSaver.Start();
            pluginScreenSpace.Text = "Aether Tracker";
            control = new PluginControl(settings); pluginScreenSpace.Controls.Add(control);
            control.ClearRequested += HandleClearRequested;
            control.SettingsChanged += ApplyVisibility;
            control.LanguageChanged += ApplyLanguage;
            control.CheckUpdatesRequested += StartCheckForUpdates;
            control.InstallUpdateRequested += StartInstallUpdate;
            control.DismissUpdateRequested += DismissUpdate;
            overlay = new OverlayForm(settings);
            listOverlay = new ActorListOverlayForm(settings);
            listOverlay.TrackRequested += Track;
            mapOverlay = new MapOverlayForm(settings);
            mapImageService = new MapImageService();
            updateService = new UpdateService();
            updateCancellation = new CancellationTokenSource();
            try {
                var registration = ActGlobals.oFormActMain.ActPlugins.FirstOrDefault(p => p != null && ReferenceEquals(p.pluginObj, this));
                if (registration != null && registration.pluginFile != null) registeredPluginPath = UpdateValidation.NormalizeRegisteredPath(registration.pluginFile.FullName);
            } catch (Exception) { pluginStatus.Text = Localization.T("update.targetUnavailable"); }
            ActGlobals.oFormActMain.OnLogLineRead += OnLogLineRead;
            pollTimer = new System.Windows.Forms.Timer { Interval = Math.Max(100, settings.PollIntervalMs) };
            pollTimer.Tick += (s, e) => Poll(); pollTimer.Start();
            listTimer = new System.Windows.Forms.Timer { Interval = Math.Max(250, settings.ListRefreshMs) };
            listTimer.Tick += (s, e) => RefreshList(); listTimer.Start();
            pluginStatus.Text = Localization.T("plugin.started");
            Poll();
            if (settings.CheckUpdatesOnStartup) StartCheckForUpdates();
        }

        private void StartCheckForUpdates()
        {
            if (stopping || control == null || updateService == null) return;
            control.SetUpdateResult(new UpdateResult { State = UpdateState.Checking, CurrentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(3) });
            TrackBackgroundTask(CheckForUpdatesAsync);
        }

        private async Task CheckForUpdatesAsync()
        {
            UpdateResult result;
            try { result = await updateService.CheckAsync(updateCancellation.Token).ConfigureAwait(false); }
            catch (Exception ex) { result = new UpdateResult { State = UpdateState.Failed, Error = ex is System.Net.Http.HttpRequestException || ex is TaskCanceledException ? "network" : "package" }; }
            PostToUi(() => {
                if (control == null || control.IsDisposed) return;
                availableUpdate = result.State == UpdateState.Available ? result : null;
                settings.LastUpdateCheckUtc = DateTime.UtcNow.ToString("o"); settings.Save();
                control.SetUpdateResult(result);
                if (result.State == UpdateState.Available && !string.Equals(settings.SkippedVersion, result.LatestVersion, StringComparison.OrdinalIgnoreCase))
                    pluginStatus.Text = Localization.T("update.available") + " " + Localization.F("update.current", result.CurrentVersion) + " / " + Localization.F("update.latest", result.LatestVersion);
                else pluginStatus.Text = Localization.T("plugin.started");
            });
        }

        private void StartInstallUpdate()
        {
            if (stopping || availableUpdate == null || updateService == null || control == null || preparedUpdate != null) return;
            if (string.IsNullOrWhiteSpace(registeredPluginPath)) {
                control.SetUpdateResult(new UpdateResult { State = UpdateState.Failed, Error = "target" });
                return;
            }
            UpdateResult update = availableUpdate;
            control.SetUpdateResult(new UpdateResult { State = UpdateState.Downloading, CurrentVersion = update.CurrentVersion, LatestVersion = update.LatestVersion, Notes = update.Notes });
            TrackBackgroundTask(() => InstallUpdateAsync(update));
        }

        private async Task InstallUpdateAsync(UpdateResult update)
        {
            UpdateResult result;
            try { result = await updateService.PreparePackageAsync(update, registeredPluginPath, updateCancellation.Token).ConfigureAwait(false); }
            catch (Exception ex) { result = new UpdateResult { State = UpdateState.Failed, Error = ex is System.Net.Http.HttpRequestException || ex is TaskCanceledException ? "network" : "package" }; }
            PostToUi(() => {
                if (control == null || control.IsDisposed) return;
                if (result.State == UpdateState.Ready) preparedUpdate = result;
                control.SetUpdateResult(result);
                pluginStatus.Text = result.State == UpdateState.Ready ? Localization.T("update.ready") : Localization.F("update.installFailed", result.Error);
            });
        }

        private void TrackBackgroundTask(Func<Task> operation)
        {
            if (operation == null) return;
            Task task;
            lock (lifecycleGate) {
                if (stopping) return;
                task = Task.Run(operation);
                backgroundTasks.Add(task);
            }
            task.ContinueWith(completed => {
                // Observe any unexpected fault and remove only after the operation actually ends.
                if (completed.IsFaulted) { var ignored = completed.Exception; }
                lock (lifecycleGate) backgroundTasks.Remove(completed);
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        private void PostToUi(Action action)
        {
            if (action == null) return;
            PluginControl target;
            lock (lifecycleGate) {
                if (stopping) return;
                target = control;
                if (target == null || target.IsDisposed || !target.IsHandleCreated) return;
                if (!target.InvokeRequired) { action(); return; }
                var ticket = new UiDispatchTicket(action);
                pendingUiDispatches.Add(ticket);
                try {
                    target.BeginInvoke(new Action(() => {
                        try { if (!stopping) ticket.Run(); }
                        finally { lock (lifecycleGate) pendingUiDispatches.Remove(ticket); }
                    }));
                } catch {
                    pendingUiDispatches.Remove(ticket);
                    ticket.Cancel();
                }
            }
        }

        private void OnSettingsSaveFailed(object sender, SettingsSaveFailedEventArgs e)
        {
            PostToUi(() => { if (pluginStatus != null) pluginStatus.Text = Localization.F("settings.saveFailed", e.Message); });
        }

        private void DismissUpdate()
        {
            if (stopping || availableUpdate == null) return;
            settings.SkippedVersion = availableUpdate.LatestVersion ?? ""; settings.Save();
            pluginStatus.Text = Localization.T("plugin.started");
            control.SetUpdateResult(new UpdateResult { State = UpdateState.UpToDate, CurrentVersion = availableUpdate.CurrentVersion, LatestVersion = availableUpdate.LatestVersion, Notes = Localization.F("update.skipped", availableUpdate.LatestVersion) });
            availableUpdate = null;
        }

        private void ApplyLanguage()
        {
            if (stopping) return;
            pluginStatus.Text = preparedUpdate != null ? Localization.T("update.ready") :
                availableUpdate != null ? Localization.T("update.available") + " " + Localization.F("update.latest", availableUpdate.LatestVersion) : Localization.T("plugin.started");
            if (overlay != null) overlay.RefreshVisuals();
            if (listOverlay != null) listOverlay.RefreshVisuals();
            if (mapOverlay != null) mapOverlay.RefreshVisuals();
            RefreshList();
        }

        private void Poll()
        {
            if (stopping || adapter == null) return;
            ApplyVisibility();
            if (!TrackerRefreshPolicy.ShouldAcquireActors(control != null && control.Visible,
                overlay != null && overlay.Visible, listOverlay != null && listOverlay.Visible, mapOverlay != null && mapOverlay.Visible)) return;
            TrackerRuntimeMetrics.ActorRead();
            actors = adapter.ReadActors(out playerId, out territoryId);
            if (control != null && control.MapOverlayEnabled && territoryId != 0 && territoryId != requestedTerritoryId) {
                requestedTerritoryId = territoryId;
                requestedMapId = 0;
                if (settings.LastMapId > 0 && settings.LastMapTerritoryId == territoryId) BeginMapLoad(settings.LastMapId);
                else BeginTerritoryMapLoad(territoryId);
            } else if (control != null && control.MapOverlayEnabled && territoryId != 0 &&
                settings.LastMapTerritoryId == territoryId && settings.LastMapId > 0 && settings.LastMapId != requestedMapId) {
                BeginMapLoad(settings.LastMapId);
            }
            UpdateArrow();
            ApplyVisibility();
        }

        private void Track(uint id)
        {
            if (stopping || control == null) return;
            trackedId = id;
            control.SetTracked(actors.FirstOrDefault(a => a.Id == id));
            RefreshList(); UpdateArrow(); ApplyVisibility();
        }

        private void HandleClearRequested()
        {
            if (stopping || control == null) return;
            trackedId = 0;
            control.SetTracked(null);
            ApplyVisibility();
        }

        private void RefreshList()
        {
            if (stopping || control == null || adapter == null) return;
            if (control.Visible) control.UpdateStatus(actors, playerId, territoryId, adapter.LastError);
            if (listOverlay != null && listOverlay.Visible) listOverlay.SetActors(actors, playerId, territoryId, trackedId);
            if (mapOverlay != null && mapOverlay.Visible) mapOverlay.SetActors(actors, playerId, trackedId);
        }

        private void OnLogLineRead(bool isImport, LogLineEventArgs logInfo)
        {
            if (stopping) return;
            if (isImport || logInfo == null || logInfo.detectedType != 40 || string.IsNullOrEmpty(logInfo.originalLogLine)) return;
            string[] fields = logInfo.originalLogLine.Split('|');
            int mapId;
            if (fields.Length < 3 || !int.TryParse(fields[2], out mapId) || mapId <= 0) return;
            if (control != null && control.InvokeRequired) {
                PostToUi(() => HandleMapChange(mapId));
                return;
            }
            HandleMapChange(mapId);
        }

        private void HandleMapChange(int mapId)
        {
            if (stopping || settings == null) return;
            settings.LastMapId = mapId; settings.LastMapTerritoryId = territoryId; settings.Save();
            if (control != null && control.MapOverlayEnabled) BeginMapLoad(mapId);
        }

        private void BeginTerritoryMapLoad(uint currentTerritoryId)
        {
            if (mapImageService == null || currentTerritoryId == 0) return;
            if (mapOverlay != null) mapOverlay.SetMap(null, Localization.F("map.checkingTerritory", currentTerritoryId));
            mapImageService.BeginLoadForTerritory(currentTerritoryId, (data, error) => {
                if (stopping || mapOverlay == null || mapOverlay.IsDisposed) { if (data != null) data.Dispose(); return; }
                if (data != null) {
                    requestedMapId = data.MapId;
                    settings.LastMapId = data.MapId;
                    settings.LastMapTerritoryId = currentTerritoryId;
                    settings.Save();
                }
                mapOverlay.SetMap(data, error == null ? null : Localization.F("map.failed", error));
                ApplyVisibility();
            });
        }

        private void BeginMapLoad(int mapId)
        {
            if (mapImageService == null || mapId == requestedMapId) return;
            requestedMapId = mapId;
            if (mapOverlay != null) mapOverlay.SetMap(null, Localization.F("map.loading", mapId));
            mapImageService.BeginLoad(mapId, (data, error) => {
                if (stopping || mapOverlay == null || mapOverlay.IsDisposed) { if (data != null) data.Dispose(); return; }
                mapOverlay.SetMap(data, error == null ? null : Localization.F("map.failed", error));
                ApplyVisibility();
            });
        }

        private void UpdateArrow()
        {
            if (overlay == null || overlay.IsDisposed || control == null) return;
            var player = actors.FirstOrDefault(a => a.Id == playerId);
            var target = actors.FirstOrDefault(a => a.Id == trackedId);
            bool lost = trackedId != 0 && target == null;
            if (lost) target = new ActorSnapshot { Id = trackedId, Name = Localization.T("tracking.placeholder") };
            overlay.SetTracking(player, target, control.PlayerFacingUp, lost);
        }

        private void ApplyVisibility()
        {
            if (stopping) return;
            if (overlay == null || listOverlay == null || mapOverlay == null || control == null) return;
            overlay.SetOverlayLocked(control.OverlayLocked);
            listOverlay.SetOverlayLocked(control.OverlayLocked);
            mapOverlay.SetOverlayLocked(control.OverlayLocked);
            overlay.SetOpacityPercent(settings.OpacityPercent);
            listOverlay.SetOpacityPercent(settings.OpacityPercent);
            mapOverlay.SetOpacityPercent(settings.OpacityPercent);
            bool active = overlay.IsGameOrSelfForeground || listOverlay.IsGameOrSelfForeground || mapOverlay.IsGameOrSelfForeground;
            bool activityAllowed = !control.HideWhenInactive || active;
            bool showArrow = control.ArrowOverlayEnabled && trackedId != 0 && overlay.HasGameWindow && activityAllowed;
            bool showList = control.ListOverlayEnabled && listOverlay.HasGameWindow && activityAllowed;
            bool showMap = control.MapOverlayEnabled && mapOverlay.HasGameWindow && activityAllowed;
            SetVisible(overlay, showArrow); SetVisible(listOverlay, showList); SetVisible(mapOverlay, showMap);
            int desiredPoll = TrackerRefreshPolicy.PollInterval(overlay.Visible, trackedId != 0, listOverlay.Visible || control.Visible,
                mapOverlay.Visible, settings.PollIntervalMs, settings.ListRefreshMs);
            if (pollTimer != null && pollTimer.Interval != desiredPoll) pollTimer.Interval = desiredPoll;
        }

        private static void SetVisible(Form form, bool visible)
        {
            if (visible && !form.Visible) form.Show(); else if (!visible && form.Visible) form.Hide();
        }

        public void DeInitPlugin()
        {
            lock (lifecycleGate) {
                if (stopping) return;
                stopping = true;
                foreach (var ticket in pendingUiDispatches) ticket.Cancel();
                pendingUiDispatches.Clear();
            }
            if (ActGlobals.oFormActMain != null) ActGlobals.oFormActMain.OnLogLineRead -= OnLogLineRead;
            if (pollTimer != null) { pollTimer.Stop(); pollTimer.Dispose(); pollTimer = null; }
            if (listTimer != null) { listTimer.Stop(); listTimer.Dispose(); listTimer = null; }

            if (control != null) {
                control.ClearRequested -= HandleClearRequested;
                control.SettingsChanged -= ApplyVisibility;
                control.LanguageChanged -= ApplyLanguage;
                control.CheckUpdatesRequested -= StartCheckForUpdates;
                control.InstallUpdateRequested -= StartInstallUpdate;
                control.DismissUpdateRequested -= DismissUpdate;
            }
            if (listOverlay != null) listOverlay.TrackRequested -= Track;

            if (updateCancellation != null) { try { updateCancellation.Cancel(); } catch { } }
            if (mapImageService != null) {
                try { mapImageService.StopAsync().GetAwaiter().GetResult(); } catch { }
                mapImageService = null;
            }

            Task[] pending;
            lock (lifecycleGate) { pending = new Task[backgroundTasks.Count]; backgroundTasks.CopyTo(pending); }
            try { Task.WhenAll(pending).GetAwaiter().GetResult(); } catch { }
            lock (lifecycleGate) backgroundTasks.Clear();

            if (overlay != null) { overlay.Close(); overlay.Dispose(); overlay = null; }
            if (listOverlay != null) { listOverlay.Close(); listOverlay.Dispose(); listOverlay = null; }
            if (mapOverlay != null) { mapOverlay.Close(); mapOverlay.Dispose(); mapOverlay = null; }
            bool settingsSaved = true;
            string settingsSaveError = null;
            if (settingsSaver != null) {
                settingsSaver.SaveFailed -= OnSettingsSaveFailed;
                settingsSaved = settingsSaver.StopAndFlush();
                settingsSaveError = settingsSaver.LastError;
                settingsSaver = null;
            }
            if (updateCancellation != null) { updateCancellation.Dispose(); updateCancellation = null; }
            if (updateService != null) { updateService.Dispose(); updateService = null; }
            if (settingsSaved && preparedUpdate != null) LaunchPreparedUpdate(preparedUpdate);
            if (pluginStatus != null) pluginStatus.Text = settingsSaved
                ? Localization.T("plugin.stopped")
                : Localization.F("settings.saveFailed", settingsSaveError ?? "Unknown error");
        }

        private static void LaunchPreparedUpdate(UpdateResult update)
        {
            try {
                if (update == null || !File.Exists(update.StagedDllPath) || !File.Exists(update.StagedUpdaterPath) ||
                    !string.Equals(UpdateValidation.Sha256(update.StagedDllPath), update.StagedDllSha256, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(UpdateValidation.Sha256(update.StagedUpdaterPath), update.StagedUpdaterSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The staged update files failed verification.");
                int processId; using (var process = Process.GetCurrentProcess()) processId = process.Id;
                string arguments = "--wait-pid " + processId + " --source " + Quote(update.StagedDllPath) +
                    " --target " + Quote(update.TargetPath) + " --sha256 " + Quote(update.StagedDllSha256) +
                    " --updater-sha256 " + Quote(update.StagedUpdaterSha256) + " --version " + Quote(update.LatestVersion) + " --original " + Quote(update.OriginalTargetSha256);
                Process.Start(new ProcessStartInfo(update.StagedUpdaterPath, arguments) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
            } catch (Exception ex) {
                string log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AetherTracker", "Updater.log");
                try { Directory.CreateDirectory(Path.GetDirectoryName(log)); File.AppendAllText(log, DateTime.UtcNow.ToString("o") + " FAILED TO START: " + ex + Environment.NewLine); } catch { }
            }
        }

        private static string Quote(string value)
        {
            string text = value ?? ""; var result = new System.Text.StringBuilder("\""); int slashes = 0;
            foreach (char c in text) {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') { result.Append('\\', slashes * 2 + 1); result.Append('"'); slashes = 0; continue; }
                result.Append('\\', slashes); slashes = 0; result.Append(c);
            }
            result.Append('\\', slashes * 2); result.Append('"'); return result.ToString();
        }

        private sealed class UiDispatchTicket
        {
            private readonly Action action;
            private int cancelled;
            public UiDispatchTicket(Action value) { action = value; }
            public void Run() { if (Interlocked.CompareExchange(ref cancelled, 0, 0) == 0) action(); }
            public void Cancel() { Interlocked.Exchange(ref cancelled, 1); }
        }
    }
}
