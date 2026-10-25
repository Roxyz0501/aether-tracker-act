using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace CharacterTracker
{
    // DataGridViewを使わず、画面に見える行だけを描画する軽量一覧。
    internal sealed class ActorListOverlayForm : OverlayWindowBase
    {
        private const int HeaderHeight = OverlayTheme.HeaderHeight;
        private const int RowHeight = 40;
        private readonly TrackerSettings settings;
        private readonly Font rowFont = new Font("Yu Gothic UI", 9, FontStyle.Regular);
        private readonly Font rowBoldFont = new Font("Yu Gothic UI Semibold", 9, FontStyle.Bold);
        private List<ActorSnapshot> actors = new List<ActorSnapshot>();
        private ActorSnapshot player;
        private MapCalibration calibration;
        private uint trackedId;
        private int scrollOffset;
        private readonly OverlayRenderGate renderGate = new OverlayRenderGate();

        public event Action<uint> TrackRequested;

        public ActorListOverlayForm(TrackerSettings value) : base(value.OverlayLocked)
        {
            settings = value;
            SetOpacityPercent(settings.OpacityPercent);
            MinimumSize = new Size(230, 110);
            MaximumSize = new Size(1000, 1000);
            RestoreSavedBounds(new Size(610, HeaderHeight + RowHeight * (Math.Max(5, settings.ListMaxRows) + 1) + 24));
            MouseWheel += OnListMouseWheel;
        }

        protected override OverlayAnchor PositionAnchor { get { return OverlayAnchor.LeftTop; } }
        protected override Point SavedOffset {
            get { return new Point(settings.ListOffsetX, settings.ListOffsetY); }
            set { settings.ListOffsetX = value.X; settings.ListOffsetY = value.Y; }
        }
        protected override Size SavedSize {
            get { return new Size(settings.ListWidth, settings.ListHeight); }
            set { settings.ListWidth = value.Width; settings.ListHeight = value.Height; }
        }
        protected override bool IsDragSurface(Point point) { return point.Y < HeaderHeight; }
        protected override void SaveSettings() { settings.Save(); }

        public void SetActors(IReadOnlyList<ActorSnapshot> snapshot, uint playerId, uint territoryId, uint currentTrackedId)
        {
            var nextPlayer = snapshot.FirstOrDefault(a => a.Id == playerId);
            var nextCalibration = settings.FindCalibration(territoryId);
            string query = (settings.SearchText ?? "").Trim();
            var nextActors = snapshot.Where(a => VisibleKind(a.Type) &&
                    (query.Length == 0 || a.Name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 || a.Id.ToString("X8").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0))
                .OrderBy(a => nextPlayer == null ? double.MaxValue : TrackingMath.HorizontalDistance(nextPlayer, a))
                .ToList();
            string state = currentTrackedId + "|" + territoryId + "|" + query + "|" + (nextCalibration == null ? "-" : nextCalibration.XOffset.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" + nextCalibration.YOffset.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" + nextCalibration.XFactor.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" + nextCalibration.YFactor.ToString("R", System.Globalization.CultureInfo.InvariantCulture)) + "|" + (nextPlayer == null ? "-" : nextPlayer.Id + ":" + nextPlayer.X + ":" + nextPlayer.Y) + "|" + string.Join(";", nextActors.Select(a => a.Id + ":" + a.Name + ":" + a.Type + ":" + a.X.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" + a.Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
            if (!renderGate.Update(state)) return;
            player = nextPlayer; calibration = nextCalibration; trackedId = currentTrackedId; actors = nextActors;
            scrollOffset = Math.Max(0, Math.Min(scrollOffset, Math.Max(0, actors.Count - VisibleRows)));
            RequestRedraw();
        }

        private bool VisibleKind(int type)
        {
            if (type == 1) return settings.ShowPlayers;
            if (type == 2 || type == 3) return settings.ShowNpcs;
            return settings.ShowOthers;
        }

        private int VisibleRows { get { return Math.Max(1, (ClientSize.Height - HeaderHeight - OverlayTheme.FooterHeight) / RowHeight); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            string coordinateMode = calibration == null ? Localization.T("coordinates.raw") : Localization.T("coordinates.game");
            OverlayTheme.Prepare(e.Graphics, settings.BackgroundOpacityPercent, Width, Height,
                "AETHER TRACKER", "AT", actors.Count + "  •  " + coordinateMode, !IsOverlayLocked);
            DrawColumnHeaders(e.Graphics);

            int count = Math.Min(VisibleRows, Math.Max(0, actors.Count - scrollOffset));
            for (int row = 0; row < count; row++) DrawActorRow(e.Graphics, actors[scrollOffset + row], row);
            if (actors.Count == 0) using (var brush = new SolidBrush(Color.FromArgb(155, 168, 178, 193)))
                e.Graphics.DrawString(Localization.T("actors.waiting"), rowFont, brush, 14, HeaderHeight + 13);
            OverlayTheme.Finish(e.Graphics, settings.BackgroundOpacityPercent, Width, Height, !IsOverlayLocked);
        }

        private void DrawColumnHeaders(Graphics g)
        {
            var c = Columns();
            using (var font = new Font("Segoe UI Semibold", 6.5f, FontStyle.Bold))
            using (var brush = new SolidBrush(Color.FromArgb(175, 206, 187, 142))) {
                g.DrawString(Localization.T("column.name"), font, brush, c.NameX, 29);
                if (c.KindWidth > 0) g.DrawString(Localization.T("column.type"), font, brush, c.KindX, 29);
                g.DrawString("X", font, brush, c.X, 29);
                g.DrawString("Y", font, brush, c.Y, 29);
                g.DrawString(Localization.T("column.distance"), font, brush, c.Distance, 29);
            }
        }

        private void DrawActorRow(Graphics g, ActorSnapshot actor, int visibleRow)
        {
            int y = HeaderHeight + RowHeight * visibleRow;
            Color kindColor = KindColor(actor.Type);
            Color rowColor = actor.Id == trackedId ? Color.FromArgb(194, 155, 82) : kindColor;
            var rowRect = new Rectangle(7, y + 2, Width - 14, RowHeight - 4);
            using (var background = new LinearGradientBrush(rowRect,
                OverlayTheme.Background(settings.BackgroundOpacityPercent, actor.Id == trackedId ? 170 : 116, rowColor),
                OverlayTheme.Background(settings.BackgroundOpacityPercent, actor.Id == trackedId ? 90 : 52, rowColor), LinearGradientMode.Horizontal))
                OverlayTheme.FillRoundedRectangle(g, background, rowRect, 6);
            if (actor.Id == trackedId) using (var accent = new SolidBrush(Color.FromArgb(235, 239, 202, 116)))
                g.FillRectangle(accent, 7, y + 8, 3, RowHeight - 16);
            else if (visibleRow > 0) using (var separator = new Pen(OverlayTheme.Background(settings.BackgroundOpacityPercent, 35, 230, 218, 188)))
                g.DrawLine(separator, 14, y, Width - 14, y);
            var c = Columns();
            var dotRect = new Rectangle(14, y + 7, 26, 26);
            using (var iconBack = new SolidBrush(Color.FromArgb(150, 8, 10, 15))) g.FillEllipse(iconBack, dotRect);
            using (var dot = new SolidBrush(kindColor)) g.FillEllipse(dot, new Rectangle(21, y + 14, 12, 12));
            var nameRect = new Rectangle(c.NameX, y + 4, c.NameWidth, RowHeight - 8);
            TextRenderer.DrawText(g, actor.Name, actor.Id == trackedId ? rowBoldFont : rowFont, nameRect, Color.White, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
            if (c.KindWidth > 0) TextRenderer.DrawText(g, actor.Kind, rowFont, new Rectangle(c.KindX, y + 4, c.KindWidth, RowHeight - 8), Color.FromArgb(205, 215, 225), TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
            string x = calibration == null ? actor.X.ToString("0.0") : calibration.MapX(actor.X).ToString("0.0");
            string mapY = calibration == null ? actor.Y.ToString("0.0") : calibration.MapY(actor.Y).ToString("0.0");
            string distance = player == null ? "—" : TrackingMath.HorizontalDistance(player, actor).ToString("0.0");
            using (var number = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold)) {
                g.DrawString(x, number, Brushes.WhiteSmoke, c.X, y + 11); g.DrawString(mapY, number, Brushes.WhiteSmoke, c.Y, y + 11);
                g.DrawString(distance, number, Brushes.WhiteSmoke, c.Distance, y + 11);
            }
        }

        private ColumnLayout Columns()
        {
            int w = Math.Max(320, ClientSize.Width - 16);
            bool showKind = ClientSize.Width >= 480;
            int nameWidth = showKind ? (int)(w * .38) : (int)(w * .50);
            int kindWidth = showKind ? (int)(w * .22) : 0;
            int numberWidth = Math.Max(48, (w - nameWidth - kindWidth) / 3);
            int kindX = 50 + nameWidth;
            int x = kindX + kindWidth;
            return new ColumnLayout { NameX = 50, NameWidth = nameWidth - 8, KindX = kindX,
                KindWidth = Math.Max(0, kindWidth - 8), X = x, Y = x + numberWidth, Distance = x + numberWidth * 2 };
        }

        private struct ColumnLayout { public int NameX, NameWidth, KindX, KindWidth, X, Y, Distance; }

        private static Color KindColor(int type)
        {
            if (type == 1) return Color.FromArgb(110, 205, 255);
            if (type == 2) return Color.FromArgb(255, 145, 120);
            if (type == 3) return Color.FromArgb(145, 235, 160);
            return Color.FromArgb(220, 205, 150);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            int row = (e.Y - HeaderHeight) / RowHeight;
            int index = scrollOffset + row;
            if (e.Y >= HeaderHeight && row >= 0 && index >= 0 && index < actors.Count && TrackRequested != null)
                TrackRequested(actors[index].Id);
        }

        private void OnListMouseWheel(object sender, MouseEventArgs e)
        {
            int delta = e.Delta > 0 ? -3 : 3;
            scrollOffset = Math.Max(0, Math.Min(scrollOffset + delta, Math.Max(0, actors.Count - VisibleRows)));
            Invalidate();
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { rowFont.Dispose(); rowBoldFont.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
