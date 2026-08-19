using Advanced_Combat_Tracker;
using System;
using System.Collections.Generic;
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
        private IReadOnlyList<ActorSnapshot> actors = new ActorSnapshot[0];
        private uint playerId, territoryId, trackedId;
        private int requestedMapId;
        private uint requestedTerritoryId;
        private UpdateService updateService;
        private CancellationTokenSource updateCancellation;
        private UpdateResult availableUpdate;

        public void InitPlugin(TabPage pluginScreenSpace, Label pluginStatusText)
        {
            pluginStatus = pluginStatusText; settings = TrackerSettings.Load(); Localization.SetLanguage(settings.Language); adapter = new FfxivAdapter();
            pluginScreenSpace.Text = "Aether Tracker";
            control = new PluginControl(settings); pluginScreenSpace.Controls.Add(control);
            control.ClearRequested += () => { trackedId = 0; control.SetTracked(null); ApplyVisibility(); };
            control.SettingsChanged += ApplyVisibility;
            control.LanguageChanged += ApplyLanguage;
            control.CheckUpdatesRequested += CheckForUpdates;
            control.InstallUpdateRequested += InstallUpdate;
            control.DismissUpdateRequested += DismissUpdate;
            overlay = new OverlayForm(settings);
            listOverlay = new ActorListOverlayForm(settings);
            listOverlay.TrackRequested += Track;
            mapOverlay = new MapOverlayForm(settings);
            mapImageService = new MapImageService();
            updateService = new UpdateService();
            updateCancellation = new CancellationTokenSource();
            ActGlobals.oFormActMain.OnLogLineRead += OnLogLineRead;
            pollTimer = new System.Windows.Forms.Timer { Interval = Math.Max(100, settings.PollIntervalMs) };
            pollTimer.Tick += (s, e) => Poll(); pollTimer.Start();
            listTimer = new System.Windows.Forms.Timer { Interval = Math.Max(250, settings.ListRefreshMs) };
            listTimer.Tick += (s, e) => RefreshList(); listTimer.Start();
            pluginStatus.Text = Localization.T("plugin.started");
            Poll();
            if (settings.CheckUpdatesOnStartup) CheckForUpdates();
        }

        private async void CheckForUpdates()
        {
            if (control == null || updateService == null) return;
            control.SetUpdateResult(new UpdateResult { State = UpdateState.Checking, CurrentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(3) });
            UpdateResult result = await updateService.CheckAsync(updateCancellation.Token);
            if (control == null || control.IsDisposed) return;
            availableUpdate = result.State == UpdateState.Available ? result : null;
            settings.LastUpdateCheckUtc = DateTime.UtcNow.ToString("o"); settings.Save();
            control.SetUpdateResult(result);
            if (result.State == UpdateState.Available && !string.Equals(settings.SkippedVersion, result.LatestVersion, StringComparison.OrdinalIgnoreCase))
                pluginStatus.Text = Localization.T("update.available") + " " + Localization.F("update.current", result.CurrentVersion) + " / " + Localization.F("update.latest", result.LatestVersion);
            else pluginStatus.Text = Localization.T("plugin.started");
        }

        private async void InstallUpdate()
        {
            if (availableUpdate == null || updateService == null || control == null) return;
            control.SetUpdateResult(new UpdateResult { State = UpdateState.Downloading, CurrentVersion = availableUpdate.CurrentVersion, LatestVersion = availableUpdate.LatestVersion, Notes = availableUpdate.Notes });
            UpdateResult result = await updateService.PrepareAndLaunchAsync(availableUpdate, updateCancellation.Token);
            if (control == null || control.IsDisposed) return;
            control.SetUpdateResult(result);
            pluginStatus.Text = result.State == UpdateState.Ready ? Localization.T("update.ready") : Localization.F("update.installFailed", result.Error);
        }

        private void DismissUpdate()
        {
            if (availableUpdate == null) return;
            settings.SkippedVersion = availableUpdate.LatestVersion ?? ""; settings.Save();
            pluginStatus.Text = Localization.T("plugin.started");
            control.SetUpdateResult(new UpdateResult { State = UpdateState.UpToDate, CurrentVersion = availableUpdate.CurrentVersion, LatestVersion = availableUpdate.LatestVersion, Notes = Localization.F("update.skipped", availableUpdate.LatestVersion) });
            availableUpdate = null;
        }

        private void ApplyLanguage()
        {
            pluginStatus.Text = Localization.T("plugin.started");
            if (overlay != null) overlay.Invalidate();
            if (listOverlay != null) listOverlay.Invalidate();
            if (mapOverlay != null) mapOverlay.Invalidate();
            RefreshList();
        }

        private void Poll()
        {
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
            trackedId = id;
            control.SetTracked(actors.FirstOrDefault(a => a.Id == id));
            RefreshList(); UpdateArrow(); ApplyVisibility();
        }

        private void RefreshList()
        {
            control.UpdateStatus(actors, playerId, territoryId, adapter.LastError);
            if (listOverlay != null && !listOverlay.IsDisposed) listOverlay.SetActors(actors, playerId, territoryId, trackedId);
            if (mapOverlay != null && !mapOverlay.IsDisposed) mapOverlay.SetActors(actors, playerId, trackedId);
            ApplyVisibility();
        }

        private void OnLogLineRead(bool isImport, LogLineEventArgs logInfo)
        {
            if (isImport || logInfo == null || logInfo.detectedType != 40 || string.IsNullOrEmpty(logInfo.originalLogLine)) return;
            string[] fields = logInfo.originalLogLine.Split('|');
            int mapId;
            if (fields.Length < 3 || !int.TryParse(fields[2], out mapId) || mapId <= 0) return;
            if (control != null && control.InvokeRequired) {
                control.BeginInvoke(new Action<int>(HandleMapChange), mapId);
                return;
            }
            HandleMapChange(mapId);
        }

        private void HandleMapChange(int mapId)
        {
            settings.LastMapId = mapId; settings.LastMapTerritoryId = territoryId; settings.Save();
            if (control != null && control.MapOverlayEnabled) BeginMapLoad(mapId);
        }

        private void BeginTerritoryMapLoad(uint currentTerritoryId)
        {
            if (mapImageService == null || currentTerritoryId == 0) return;
            if (mapOverlay != null) mapOverlay.SetMap(null, Localization.F("map.checkingTerritory", currentTerritoryId));
            mapImageService.BeginLoadForTerritory(currentTerritoryId, (data, error) => {
                if (mapOverlay == null || mapOverlay.IsDisposed) { if (data != null) data.Dispose(); return; }
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
                if (mapOverlay == null || mapOverlay.IsDisposed) { if (data != null) data.Dispose(); return; }
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
            if (overlay == null || listOverlay == null || mapOverlay == null || control == null) return;
            overlay.SetOverlayLocked(control.OverlayLocked);
            listOverlay.SetOverlayLocked(control.OverlayLocked);
            mapOverlay.SetOverlayLocked(control.OverlayLocked);
            overlay.SetOpacityPercent(settings.OpacityPercent);
            listOverlay.SetOpacityPercent(settings.OpacityPercent);
            mapOverlay.SetOpacityPercent(settings.OpacityPercent);
            overlay.Invalidate(); listOverlay.Invalidate(); mapOverlay.Invalidate();
            int desiredPoll = control.ArrowOverlayEnabled && trackedId != 0
                ? Math.Max(100, settings.PollIntervalMs)
                : ((control.ListOverlayEnabled || control.MapOverlayEnabled) ? Math.Max(500, settings.ListRefreshMs) : 1000);
            if (pollTimer != null && pollTimer.Interval != desiredPoll) pollTimer.Interval = desiredPoll;
            bool active = overlay.IsGameOrSelfForeground || listOverlay.IsGameOrSelfForeground || mapOverlay.IsGameOrSelfForeground;
            bool activityAllowed = !control.HideWhenInactive || active;
            bool showArrow = control.ArrowOverlayEnabled && trackedId != 0 && overlay.HasGameWindow && activityAllowed;
            bool showList = control.ListOverlayEnabled && listOverlay.HasGameWindow && activityAllowed;
            bool showMap = control.MapOverlayEnabled && mapOverlay.HasGameWindow && activityAllowed;
            SetVisible(overlay, showArrow); SetVisible(listOverlay, showList); SetVisible(mapOverlay, showMap);
        }

        private static void SetVisible(Form form, bool visible)
        {
            if (visible && !form.Visible) form.Show(); else if (!visible && form.Visible) form.Hide();
        }

        public void DeInitPlugin()
        {
            if (ActGlobals.oFormActMain != null) ActGlobals.oFormActMain.OnLogLineRead -= OnLogLineRead;
            if (pollTimer != null) { pollTimer.Stop(); pollTimer.Dispose(); pollTimer = null; }
            if (listTimer != null) { listTimer.Stop(); listTimer.Dispose(); listTimer = null; }
            if (overlay != null) { overlay.Close(); overlay.Dispose(); overlay = null; }
            if (listOverlay != null) { listOverlay.Close(); listOverlay.Dispose(); listOverlay = null; }
            if (mapOverlay != null) { mapOverlay.Close(); mapOverlay.Dispose(); mapOverlay = null; }
            if (settings != null) settings.Save();
            if (updateCancellation != null) { updateCancellation.Cancel(); updateCancellation.Dispose(); updateCancellation = null; }
            if (updateService != null) { updateService.Dispose(); updateService = null; }
            if (pluginStatus != null) pluginStatus.Text = Localization.T("plugin.stopped");
        }
    }
}
