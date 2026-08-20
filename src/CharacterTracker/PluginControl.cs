using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace CharacterTracker
{
    internal sealed class PluginControl : UserControl
    {
        private const string SupportUrl = "https://ko-fi.com/roxyz0501";
        private readonly TextBox search = new TextBox { Width = 240, MaxLength = 100, Anchor = AnchorStyles.Left };
        private readonly CheckBox players = LocCheck("show.players");
        private readonly CheckBox npcs = LocCheck("show.npcs");
        private readonly CheckBox others = LocCheck("show.others");
        private readonly CheckBox arrowOverlay = LocCheck("show.arrow");
        private readonly CheckBox listOverlay = LocCheck("show.list");
        private readonly CheckBox mapOverlay = LocCheck("show.map");
        private readonly CheckBox overlayLocked = LocCheck("overlay.lock");
        private readonly CheckBox facing = LocCheck("facing");
        private readonly CheckBox inactive = LocCheck("inactive");
        private readonly CheckBox checkUpdatesOnStartup = LocCheck("update.startup");
        private readonly ComboBox language = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180, Anchor = AnchorStyles.Left };
        private readonly TrackBar opacity = Slider(35);
        private readonly Label opacityValue = Label("");
        private readonly TrackBar backgroundOpacity = Slider(0);
        private readonly Label backgroundOpacityValue = Label("");
        private readonly Label status = LocLabel("connection.wait");
        private readonly Label calibration = LocLabel("calibration.none");
        private readonly Label tracked = LocLabel("tracking.none");
        private readonly Label version = Label("");
        private readonly Label supportStatus = Label("");
        private readonly Button clear = LocButton("tracking.clear", true);
        private readonly Button pointA = LocButton("calibration.a", true);
        private readonly Button pointB = LocButton("calibration.b", true);
        private readonly Button checkNow = LocButton("update.checkNow", true);
        private readonly Button installUpdate = LocButton("update.install", true);
        private readonly Button laterUpdate = LocButton("update.later", true);
        private readonly Label updateStatus = LocLabel("update.idle");
        private readonly Label updateVersions = Label("");
        private readonly TextBox updateNotes = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        private CalibrationPoint anchorA;
        private IReadOnlyList<ActorSnapshot> actors = new ActorSnapshot[0];
        private uint playerId, territoryId;
        private readonly TrackerSettings settings;
        private bool loading = true;
        private TabControl tabs;
        private TabPage supportPage;
        private int hoveredTabIndex = -1;
        private string lastConnectionError;
        private UpdateResult displayedUpdate;

        public event Action ClearRequested;
        public event Action SettingsChanged;
        public event Action LanguageChanged;
        public event Action CheckUpdatesRequested;
        public event Action InstallUpdateRequested;
        public event Action DismissUpdateRequested;

        public PluginControl(TrackerSettings value)
        {
            settings = value;
            Localization.SetLanguage(value.Language);
            Dock = DockStyle.Fill;
            Font = new Font(Localization.UiFontName, 9f);
            BackColor = Color.FromArgb(244, 247, 251);
            Padding = new Padding(14);

            arrowOverlay.Checked = value.OverlayEnabled;
            listOverlay.Checked = value.ListOverlayEnabled;
            mapOverlay.Checked = value.MapOverlayEnabled;
            overlayLocked.Checked = value.OverlayLocked;
            facing.Checked = value.PlayerFacingUp;
            inactive.Checked = value.HideWhenGameInactive;
            players.Checked = value.ShowPlayers;
            npcs.Checked = value.ShowNpcs;
            others.Checked = value.ShowOthers;
            checkUpdatesOnStartup.Checked = value.CheckUpdatesOnStartup;
            search.Text = value.SearchText ?? "";
            opacity.Value = Clamp(value.OpacityPercent, 35, 100);
            backgroundOpacity.Value = Clamp(value.BackgroundOpacityPercent, 0, 100);
            opacityValue.Text = opacity.Value + "%";
            backgroundOpacityValue.Text = backgroundOpacity.Value + "%";
            version.Text = "v" + Assembly.GetExecutingAssembly().GetName().Version.ToString(3) + "  •  Roxyz0501";

            language.Items.AddRange(new object[] {
                new LanguageItem("en", "English"), new LanguageItem("ja", "日本語"),
                new LanguageItem("zh-Hans", "简体中文"), new LanguageItem("ko", "한국어")
            });
            SelectLanguage(value.Language);

            BuildLayout();
            pointB.Enabled = false;
            WireEvents();
            ApplyLocalization();
            loading = false;
        }

        public bool ArrowOverlayEnabled { get { return arrowOverlay.Checked; } }
        public bool ListOverlayEnabled { get { return listOverlay.Checked; } }
        public bool MapOverlayEnabled { get { return mapOverlay.Checked; } }
        public bool OverlayLocked { get { return overlayLocked.Checked; } }
        public bool PlayerFacingUp { get { return facing.Checked; } }
        public bool HideWhenInactive { get { return inactive.Checked; } }

        private void BuildLayout()
        {
            tabs = new TabControl { Dock = DockStyle.Fill, Font = new Font("Yu Gothic UI Semibold", 9f), Padding = new Point(18, 6), DrawMode = TabDrawMode.OwnerDrawFixed };
            tabs.TabPages.Add(BuildDisplayTab());
            tabs.TabPages.Add(BuildTrackingTab());
            tabs.TabPages.Add(BuildMapTab());
            supportPage = BuildSupportTab();
            tabs.TabPages.Add(BuildUpdateTab());
            tabs.TabPages.Add(supportPage);
            tabs.DrawItem += DrawTab;
            tabs.MouseMove += TabMouseMove;
            tabs.MouseLeave += delegate { hoveredTabIndex = -1; tabs.Invalidate(); };

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = BackColor };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            root.Controls.Add(BuildHeader(), 0, 0);
            root.Controls.Add(tabs, 0, 1);
            var footer = new Panel { Dock = DockStyle.Fill, BackColor = BackColor };
            version.AutoSize = false;
            version.Dock = DockStyle.Right;
            version.Width = 180;
            version.TextAlign = ContentAlignment.MiddleRight;
            version.Padding = new Padding(0, 0, 8, 2);
            footer.Controls.Add(version);
            root.Controls.Add(footer, 0, 2);
            Controls.Add(root);

            version.Font = new Font("Segoe UI", 8f);
            version.ForeColor = Color.FromArgb(113, 126, 146);
            version.BackColor = Color.Transparent;

            StyleButton(clear, false);
            StyleButton(pointA, true);
            StyleButton(pointB, false);
            opacityValue.ForeColor = Color.FromArgb(71, 84, 103);
            backgroundOpacityValue.ForeColor = Color.FromArgb(71, 84, 103);
            status.ForeColor = Color.FromArgb(176, 65, 56);
            calibration.ForeColor = Color.FromArgb(73, 96, 128);
            tracked.ForeColor = Color.FromArgb(73, 96, 128);
        }

        private Control BuildHeader()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(18, 11, 18, 8), Margin = new Padding(0, 0, 0, 10) };
            panel.Paint += delegate(object sender, PaintEventArgs e) {
                using (var pen = new Pen(Color.FromArgb(202, 165, 87), 2f)) e.Graphics.DrawLine(pen, 0, panel.Height - 2, panel.Width, panel.Height - 2);
            };
            panel.Controls.Add(new Label { Text = "Aether Tracker", Font = new Font(Localization.UiFontName, 17f, FontStyle.Bold),
                ForeColor = Color.FromArgb(34, 44, 61), AutoSize = true, Location = new Point(18, 9) });
            panel.Controls.Add(new Label { Text = Localization.T("header.subtitle"), Tag = "loc:header.subtitle", Font = new Font(Localization.UiFontName, 8.5f),
                ForeColor = Color.FromArgb(103, 116, 137), AutoSize = true, Location = new Point(20, 43) });
            return panel;
        }

        private TabPage BuildDisplayTab()
        {
            var page = LocPage("tab.display");
            var layout = BaseLayout(13);
            layout.Controls.Add(LocLabel("language"), 0, 0);
            layout.Controls.Add(language, 1, 0);
            AddWide(layout, arrowOverlay, 1);
            AddWide(layout, listOverlay, 2);
            AddWide(layout, mapOverlay, 3);
            AddWide(layout, inactive, 4);
            AddWide(layout, overlayLocked, 5);
            layout.Controls.Add(LocLabel("opacity"), 0, 6);
            layout.Controls.Add(SliderPanel(opacity, opacityValue), 1, 6);
            layout.Controls.Add(LocLabel("background.opacity"), 0, 7);
            layout.Controls.Add(SliderPanel(backgroundOpacity, backgroundOpacityValue), 1, 7);
            var hint = LocExplanation("display.hint");
            AddWide(layout, hint, 8);
            status.AutoSize = false; status.Dock = DockStyle.Fill;
            AddWide(layout, status, 9);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            for (int i = 1; i <= 5; i++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            page.Controls.Add(layout);
            return page;
        }

        private TabPage BuildTrackingTab()
        {
            var page = LocPage("tab.tracking");
            var layout = BaseLayout(11);
            AddWide(layout, facing, 0);
            var directionHint = LocExplanation("direction.hint");
            directionHint.Padding = new Padding(18, 0, 0, 0);
            AddWide(layout, directionHint, 1);
            layout.Controls.Add(LocLabel("tracking.search"), 0, 2);
            layout.Controls.Add(search, 1, 2);
            AddWide(layout, players, 3);
            AddWide(layout, npcs, 4);
            AddWide(layout, others, 5);
            var action = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = Padding.Empty };
            action.Controls.Add(clear); tracked.Margin = new Padding(10, 8, 0, 0); action.Controls.Add(tracked);
            AddWide(layout, action, 6);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            for (int i = 3; i <= 5; i++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            page.Controls.Add(layout);
            return page;
        }

        private TabPage BuildMapTab()
        {
            var page = LocPage("tab.map");
            var layout = BaseLayout(8);
            var title = LocLabel("calibration.title");
            title.Font = new Font("Yu Gothic UI Semibold", 10f, FontStyle.Bold);
            title.ForeColor = Color.FromArgb(48, 61, 80);
            AddWide(layout, title, 0);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = Padding.Empty };
            buttons.Controls.Add(pointA); buttons.Controls.Add(pointB);
            AddWide(layout, buttons, 1);
            AddWide(layout, calibration, 2);
            var help = LocExplanation("calibration.help");
            AddWide(layout, help, 3);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            page.Controls.Add(layout);
            return page;
        }

        private TabPage BuildSupportTab()
        {
            var page = LocPage("tab.support");
            var layout = BaseLayout(7);

            var title = LocLabel("support.title");
            title.Font = new Font("Yu Gothic UI Semibold", 17f, FontStyle.Bold);
            title.ForeColor = Color.FromArgb(166, 83, 15);
            AddWide(layout, title, 0);

            var description = LocExplanation("support.description");
            description.Font = new Font("Yu Gothic UI", 9.5f);
            description.ForeColor = Color.FromArgb(67, 61, 54);
            AddWide(layout, description, 1);

            var supportButton = new Button {
                Text = Localization.T("support.button"),
                Tag = "loc:support.button",
                AutoSize = false,
                Width = 360,
                Height = 50,
                Anchor = AnchorStyles.Left,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(202, 101, 20),
                ForeColor = Color.White,
                Font = new Font("Yu Gothic UI Semibold", 11f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            supportButton.FlatAppearance.BorderColor = Color.FromArgb(164, 79, 12);
            supportButton.FlatAppearance.BorderSize = 1;
            supportButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(224, 126, 31);
            supportButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(174, 83, 13);
            supportButton.Click += OpenSupportLink;
            AddWide(layout, supportButton, 2);

            var link = Explanation(SupportUrl);
            link.ForeColor = Color.FromArgb(115, 91, 60);
            AddWide(layout, link, 3);

            supportStatus.AutoSize = false;
            supportStatus.Dock = DockStyle.Fill;
            supportStatus.ForeColor = Color.FromArgb(73, 96, 128);
            AddWide(layout, supportStatus, 4);

            var note = LocExplanation("support.note");
            note.ForeColor = Color.FromArgb(103, 109, 119);
            AddWide(layout, note, 5);

            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            page.Controls.Add(layout);
            return page;
        }

        private TabPage BuildUpdateTab()
        {
            var page = LocPage("tab.update");
            var layout = BaseLayout(9);
            var title = LocLabel("update.title");
            title.Font = new Font(Localization.UiFontName, 13f, FontStyle.Bold);
            title.ForeColor = Color.FromArgb(48, 61, 80);
            AddWide(layout, title, 0);
            AddWide(layout, checkUpdatesOnStartup, 1);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = Padding.Empty };
            actions.Controls.Add(checkNow); actions.Controls.Add(installUpdate); actions.Controls.Add(laterUpdate);
            installUpdate.Enabled = false; laterUpdate.Enabled = false;
            StyleButton(checkNow, false); StyleButton(installUpdate, true); StyleButton(laterUpdate, false);
            AddWide(layout, actions, 2);
            updateStatus.AutoSize = false; updateStatus.Dock = DockStyle.Fill; AddWide(layout, updateStatus, 3);
            updateVersions.AutoSize = false; updateVersions.Dock = DockStyle.Fill; AddWide(layout, updateVersions, 4);
            AddWide(layout, LocLabel("update.notes"), 5);
            AddWide(layout, updateNotes, 6);
            AddWide(layout, LocExplanation("update.safety"), 7);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            page.Controls.Add(layout);
            return page;
        }

        private void OpenSupportLink(object sender, EventArgs e)
        {
            try {
                Process.Start(new ProcessStartInfo(SupportUrl) { UseShellExecute = true });
                supportStatus.ForeColor = Color.FromArgb(17, 108, 75);
                supportStatus.Text = Localization.T("support.opened");
            } catch (Exception ex) {
                supportStatus.ForeColor = Color.FromArgb(176, 65, 56);
                supportStatus.Text = Localization.F("support.failed", ex.Message);
            }
        }

        private void DrawTab(object sender, DrawItemEventArgs e)
        {
            bool selected = e.Index == tabs.SelectedIndex;
            bool hovered = e.Index == hoveredTabIndex;
            bool support = tabs.TabPages[e.Index] == supportPage;
            Color background;
            Color foreground;

            background = selected ? Color.White : Color.FromArgb(240, 242, 245);
            foreground = Color.FromArgb(42, 53, 69);
            if (support) {
                background = selected ? Color.FromArgb(255, 245, 226) : hovered ? Color.FromArgb(255, 238, 207) : Color.FromArgb(250, 231, 197);
                foreground = selected ? Color.FromArgb(137, 62, 7) : hovered ? Color.FromArgb(157, 72, 8) : Color.FromArgb(174, 84, 13);
            } else if (hovered && !selected) background = Color.FromArgb(247, 248, 250);

            using (var brush = new SolidBrush(background)) e.Graphics.FillRectangle(brush, e.Bounds);
            TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, tabs.Font, e.Bounds, foreground,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            if (support && selected) {
                using (var pen = new Pen(Color.FromArgb(213, 137, 32), 2f))
                    e.Graphics.DrawLine(pen, e.Bounds.Left + 4, e.Bounds.Bottom - 2, e.Bounds.Right - 4, e.Bounds.Bottom - 2);
            }
        }

        private void TabMouseMove(object sender, MouseEventArgs e)
        {
            int index = -1;
            for (int i = 0; i < tabs.TabCount; i++) {
                if (tabs.GetTabRect(i).Contains(e.Location)) { index = i; break; }
            }
            if (index == hoveredTabIndex) return;
            hoveredTabIndex = index;
            tabs.Invalidate();
        }

        private void WireEvents()
        {
            foreach (var box in new[] { arrowOverlay, listOverlay, mapOverlay, overlayLocked, facing, inactive, players, npcs, others })
                box.CheckedChanged += SettingChanged;
            checkUpdatesOnStartup.CheckedChanged += SettingChanged;
            language.SelectedIndexChanged += LanguageSelected;
            search.TextChanged += SettingChanged;
            opacity.ValueChanged += delegate { opacityValue.Text = opacity.Value + "%"; SettingChanged(this, EventArgs.Empty); };
            backgroundOpacity.ValueChanged += delegate { backgroundOpacityValue.Text = backgroundOpacity.Value + "%"; SettingChanged(this, EventArgs.Empty); };
            clear.Click += delegate { if (ClearRequested != null) ClearRequested(); };
            pointA.Click += delegate { CaptureA(); };
            pointB.Click += delegate { CaptureB(); };
            checkNow.Click += delegate { if (CheckUpdatesRequested != null) CheckUpdatesRequested(); };
            installUpdate.Click += delegate { if (InstallUpdateRequested != null) InstallUpdateRequested(); };
            laterUpdate.Click += delegate { if (DismissUpdateRequested != null) DismissUpdateRequested(); };
        }

        public void UpdateStatus(IReadOnlyList<ActorSnapshot> value, uint currentPlayerId, uint currentTerritoryId, string error)
        {
            actors = value; playerId = currentPlayerId; territoryId = currentTerritoryId; lastConnectionError = error;
            bool connected = string.IsNullOrEmpty(error);
            status.ForeColor = connected ? Color.FromArgb(17, 108, 75) : Color.FromArgb(176, 65, 56);
            status.Text = connected ? Localization.F("connected", actors.Count, territoryId) : "●  " + error;
            calibration.Text = settings.FindCalibration(territoryId) == null ? Localization.T("calibration.none") : Localization.F("calibration.done", territoryId);
        }

        public void SetTracked(ActorSnapshot actor)
        {
            tracked.Text = actor == null ? Localization.T("tracking.none") : Localization.F("tracking.target", actor.Name, actor.Id.ToString("X8"));
        }

        public void SetUpdateResult(UpdateResult result)
        {
            displayedUpdate = result;
            RenderUpdateResult();
        }

        private void RenderUpdateResult()
        {
            UpdateResult value = displayedUpdate;
            if (value == null) {
                updateStatus.Text = Localization.T("update.idle"); updateVersions.Text = ""; updateNotes.Text = "";
                installUpdate.Enabled = laterUpdate.Enabled = false; return;
            }
            installUpdate.Enabled = value.State == UpdateState.Available;
            laterUpdate.Enabled = value.State == UpdateState.Available;
            checkNow.Enabled = value.State != UpdateState.Checking && value.State != UpdateState.Downloading;
            if (value.State == UpdateState.Unconfigured) updateStatus.Text = Localization.T("update.unconfigured");
            else if (value.State == UpdateState.Checking) updateStatus.Text = Localization.T("update.checking");
            else if (value.State == UpdateState.UpToDate) updateStatus.Text = Localization.T("update.none");
            else if (value.State == UpdateState.Available) updateStatus.Text = Localization.T("update.available");
            else if (value.State == UpdateState.Downloading) updateStatus.Text = Localization.T("update.downloading");
            else if (value.State == UpdateState.Ready) updateStatus.Text = Localization.T("update.ready");
            else updateStatus.Text = Localization.F("update.failed", value.Error ?? "Unknown error");
            updateStatus.ForeColor = value.State == UpdateState.Failed ? Color.FromArgb(176, 65, 56) :
                value.State == UpdateState.Available ? Color.FromArgb(177, 94, 20) : Color.FromArgb(45, 78, 103);
            updateVersions.Text = (string.IsNullOrEmpty(value.CurrentVersion) ? "" : Localization.F("update.current", value.CurrentVersion)) +
                (string.IsNullOrEmpty(value.LatestVersion) ? "" : "    " + Localization.F("update.latest", value.LatestVersion));
            updateNotes.Text = value.Notes ?? "";
        }

        private void LanguageSelected(object sender, EventArgs e)
        {
            if (loading) return;
            var item = language.SelectedItem as LanguageItem;
            if (item == null) return;
            settings.Language = item.Code;
            Localization.SetLanguage(item.Code);
            settings.Save();
            ApplyLocalization();
            UpdateStatus(actors, playerId, territoryId, lastConnectionError);
            RenderUpdateResult();
            if (LanguageChanged != null) LanguageChanged();
        }

        private void SelectLanguage(string code)
        {
            string normalized = Localization.NormalizeLanguage(code);
            for (int i = 0; i < language.Items.Count; i++) {
                var item = language.Items[i] as LanguageItem;
                if (item != null && item.Code == normalized) { language.SelectedIndex = i; return; }
            }
            language.SelectedIndex = 0;
        }

        private void ApplyLocalization()
        {
            ApplyLocalizationTo(this);
            tabs.Invalidate();
        }

        private static void ApplyLocalizationTo(Control parent)
        {
            string tag = parent.Tag as string;
            if (tag != null && tag.StartsWith("loc:", StringComparison.Ordinal)) parent.Text = Localization.T(tag.Substring(4));
            if (!(parent is TextBox) && !(parent is TrackBar)) {
                try { parent.Font = new Font(Localization.UiFontName, parent.Font.Size, parent.Font.Style); } catch { }
            }
            foreach (Control child in parent.Controls) ApplyLocalizationTo(child);
        }

        private void SettingChanged(object sender, EventArgs e)
        {
            if (loading) return;
            settings.OverlayEnabled = arrowOverlay.Checked;
            settings.ListOverlayEnabled = listOverlay.Checked;
            settings.MapOverlayEnabled = mapOverlay.Checked;
            settings.OverlayLocked = overlayLocked.Checked;
            settings.PlayerFacingUp = facing.Checked;
            settings.HideWhenGameInactive = inactive.Checked;
            settings.ShowPlayers = players.Checked;
            settings.ShowNpcs = npcs.Checked;
            settings.ShowOthers = others.Checked;
            settings.SearchText = search.Text;
            settings.OpacityPercent = opacity.Value;
            settings.BackgroundOpacityPercent = backgroundOpacity.Value;
            settings.CheckUpdatesOnStartup = checkUpdatesOnStartup.Checked;
            settings.Save();
            if (SettingsChanged != null) SettingsChanged();
        }

        private ActorSnapshot CurrentPlayer() { return actors.FirstOrDefault(a => a.Id == playerId); }

        private void CaptureA()
        {
            var player = CurrentPlayer();
            if (player == null) { MessageBox.Show(Localization.T("calibration.noPlayer")); return; }
            double x, y;
            if (!CoordinateDialog.TryGet(this, Localization.T("calibration.dialogA"), out x, out y)) return;
            anchorA = new CalibrationPoint { TerritoryId = territoryId, WorldX = player.X, WorldY = player.Y, MapX = x, MapY = y };
            pointB.Enabled = true;
            calibration.Text = Localization.T("calibration.aSaved");
        }

        private void CaptureB()
        {
            var player = CurrentPlayer();
            if (player == null || anchorA == null) return;
            if (territoryId != anchorA.TerritoryId) { MessageBox.Show(Localization.T("calibration.sameMap")); return; }
            double x, y;
            if (!CoordinateDialog.TryGet(this, Localization.T("calibration.dialogB"), out x, out y)) return;
            try {
                var b = new CalibrationPoint { TerritoryId = territoryId, WorldX = player.X, WorldY = player.Y, MapX = x, MapY = y };
                settings.PutCalibration(MapCalibration.FromTwoPoints(territoryId, anchorA, b));
                anchorA = null; pointB.Enabled = false; calibration.Text = Localization.T("calibration.saved");
                if (SettingsChanged != null) SettingsChanged();
            } catch (Exception ex) { MessageBox.Show(ex.Message, Localization.T("calibration.failed"), MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        private static TabPage LocPage(string key) { return new TabPage(Localization.T(key)) { Tag = "loc:" + key, BackColor = Color.White, Padding = new Padding(4) }; }

        private static TableLayoutPanel BaseLayout(int rows)
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = rows, Padding = new Padding(12), AutoScroll = true, BackColor = Color.White };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 235));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return layout;
        }

        private static void AddWide(TableLayoutPanel layout, Control control, int row) { layout.Controls.Add(control, 0, row); layout.SetColumnSpan(control, 2); }
        private static CheckBox Check(string text) { return new CheckBox { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Color.FromArgb(48, 61, 80) }; }
        private static CheckBox LocCheck(string key) { var value = Check(Localization.T(key)); value.Tag = "loc:" + key; return value; }
        private static Label Label(string text) { return new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Color.FromArgb(45, 58, 77) }; }
        private static Label LocLabel(string key) { var value = Label(Localization.T(key)); value.Tag = "loc:" + key; return value; }
        private static Label Explanation(string text) { return new Label { Text = text, AutoSize = false, Dock = DockStyle.Fill, ForeColor = Color.FromArgb(73, 78, 88), Padding = new Padding(0, 8, 0, 0) }; }
        private static Label LocExplanation(string key) { var value = Explanation(Localization.T(key)); value.Tag = "loc:" + key; return value; }
        private static Button LocButton(string key, bool autoSize) { return new Button { Text = Localization.T(key), Tag = "loc:" + key, AutoSize = autoSize }; }
        private static TrackBar Slider(int minimum) { return new TrackBar { Minimum = minimum, Maximum = 100, TickFrequency = 5, Width = 190, Height = 38, Anchor = AnchorStyles.Left }; }
        private static Control SliderPanel(TrackBar slider, Label value)
        {
            var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty };
            value.Margin = new Padding(4, 10, 0, 0); panel.Controls.Add(slider); panel.Controls.Add(value); return panel;
        }

        private static void StyleButton(Button button, bool primary)
        {
            button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = primary ? Color.FromArgb(164, 127, 58) : Color.FromArgb(171, 181, 196);
            button.BackColor = primary ? Color.FromArgb(48, 55, 69) : Color.White;
            button.ForeColor = primary ? Color.FromArgb(244, 225, 183) : Color.FromArgb(63, 76, 95);
            button.Font = new Font("Yu Gothic UI Semibold", 8.5f, FontStyle.Bold);
            button.Padding = new Padding(9, 3, 9, 3); button.Cursor = Cursors.Hand;
        }

        private static int Clamp(int value, int min, int max) { return Math.Max(min, Math.Min(max, value)); }

        private sealed class LanguageItem
        {
            public readonly string Code, Name;
            public LanguageItem(string code, string name) { Code = code; Name = name; }
            public override string ToString() { return Name; }
        }
    }

    internal sealed class CoordinateDialog : Form
    {
        private readonly NumericUpDown x = new NumericUpDown { DecimalPlaces = 1, Minimum = -1000, Maximum = 1000, Width = 100 };
        private readonly NumericUpDown y = new NumericUpDown { DecimalPlaces = 1, Minimum = -1000, Maximum = 1000, Width = 100 };

        private CoordinateDialog(string title)
        {
            Text = title; Font = new Font(Localization.UiFontName, 9f); BackColor = Color.FromArgb(244, 247, 251);
            FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(320, 130); MaximizeBox = false; MinimizeBox = false;
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14) };
            flow.Controls.Add(new Label { Text = "X", AutoSize = true, Margin = new Padding(3, 7, 4, 0) }); flow.Controls.Add(x);
            flow.Controls.Add(new Label { Text = "Y", AutoSize = true, Margin = new Padding(14, 7, 4, 0) }); flow.Controls.Add(y);
            var ok = new Button { Text = Localization.T("record"), DialogResult = DialogResult.OK, Width = 90, Margin = new Padding(48, 20, 3, 3) };
            var cancel = new Button { Text = Localization.T("cancel"), DialogResult = DialogResult.Cancel, Width = 90, Margin = new Padding(6, 20, 3, 3) };
            flow.Controls.Add(ok); flow.Controls.Add(cancel); Controls.Add(flow); AcceptButton = ok; CancelButton = cancel;
        }

        public static bool TryGet(IWin32Window owner, string title, out double mapX, out double mapY)
        {
            using (var dialog = new CoordinateDialog(title)) {
                if (dialog.ShowDialog(owner) == DialogResult.OK) { mapX = (double)dialog.x.Value; mapY = (double)dialog.y.Value; return true; }
            }
            mapX = mapY = 0; return false;
        }
    }
}
