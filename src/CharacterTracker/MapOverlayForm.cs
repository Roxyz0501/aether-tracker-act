using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace CharacterTracker
{
    internal sealed class MapOverlayForm : OverlayWindowBase
    {
        private readonly TrackerSettings settings;
        private readonly Font labelFont = new Font("Yu Gothic UI", 8, FontStyle.Bold);
        private MapData map;
        private IReadOnlyList<ActorSnapshot> actors = new ActorSnapshot[0];
        private uint playerId, trackedId, selectedId;
        private float zoom = 1f;
        private PointF viewCenter;
        private string status = Localization.T("map.waiting");
        private readonly OverlayRenderGate renderGate = new OverlayRenderGate();

        public MapOverlayForm(TrackerSettings value) : base(value.OverlayLocked)
        {
            settings = value;
            SetOpacityPercent(settings.OpacityPercent);
            MinimumSize = new Size(230, 270);
            MaximumSize = new Size(1000, 1034);
            RestoreSavedBounds(new Size(540, 574));
        }

        protected override OverlayAnchor PositionAnchor { get { return OverlayAnchor.RightTop; } }
        protected override Point SavedOffset {
            get { return new Point(settings.MapOffsetX, settings.MapOffsetY); }
            set { settings.MapOffsetX = value.X; settings.MapOffsetY = value.Y; }
        }
        protected override Size SavedSize {
            get { return new Size(settings.MapWidth, settings.MapHeight); }
            set { settings.MapWidth = value.Width; settings.MapHeight = value.Height; }
        }
        protected override bool IsDragSurface(Point point) { return point.Y < OverlayTheme.HeaderHeight; }
        protected override void SaveSettings() { settings.Save(); }

        public void SetMap(MapData value, string message)
        {
            string state = (value == null ? "-" : value.MapId + ":" + value.AssetId + ":" + (value.Image == null ? "-" : value.Image.Width + "x" + value.Image.Height)) + "|" + (message ?? "");
            if (!renderGate.Update("map|" + state)) { if (value != null && !ReferenceEquals(map, value)) value.Dispose(); return; }
            if (!ReferenceEquals(map, value) && map != null) map.Dispose();
            map = value; status = message ?? "";
            if (map != null && map.Image != null) {
                zoom = 1f;
                viewCenter = new PointF(map.Image.Width / 2f, map.Image.Height / 2f);
                selectedId = 0;
            }
            RequestRedraw();
        }

        public void SetActors(IReadOnlyList<ActorSnapshot> value, uint currentPlayerId, uint currentTrackedId)
        {
            string state = "actors|" + currentPlayerId + "|" + currentTrackedId + "|" + string.Join(";", value.Select(a => a.Id + ":" + a.Name + ":" + a.X.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" + a.Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" + a.Type));
            if (!renderGate.Update(state)) return;
            actors = value; playerId = currentPlayerId; trackedId = currentTrackedId; RequestRedraw();
            if (selectedId != 0 && !actors.Any(a => a.Id == selectedId)) selectedId = 0;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            string headerStatus = map == null ? Localization.T("overlay.waiting") : map.MapId + "  •  " + Math.Round(zoom * 100) + "%";
            OverlayTheme.Prepare(e.Graphics, settings.BackgroundOpacityPercent, Width, Height,
                "AETHER MAP", "MAP", headerStatus, !IsOverlayLocked);
            var area = MapArea;
            if (map == null || map.Image == null) {
                using (var fill = new SolidBrush(OverlayTheme.Background(settings.BackgroundOpacityPercent, 210, 24, 29, 38)))
                    OverlayTheme.FillRoundedRectangle(e.Graphics, fill, area, 6);
                using (var font = new Font("Yu Gothic UI", 9f))
                using (var brush = new SolidBrush(Color.FromArgb(168, 178, 193))) {
                    var size = e.Graphics.MeasureString(status, font);
                    e.Graphics.DrawString(status, font, brush, area.Left + (area.Width - size.Width) / 2, area.Top + (area.Height - size.Height) / 2);
                }
                OverlayTheme.Finish(e.Graphics, settings.BackgroundOpacityPercent, Width, Height, !IsOverlayLocked);
                return;
            }
            RectangleF source = SourceRectangle;
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            e.Graphics.DrawImage(map.Image, area, source, GraphicsUnit.Pixel);
            DrawActors(e.Graphics, area, source);
            using (var border = new Pen(Color.FromArgb(100, 255, 255, 255))) e.Graphics.DrawRectangle(border, area);
            OverlayTheme.Finish(e.Graphics, settings.BackgroundOpacityPercent, Width, Height, !IsOverlayLocked);
        }

        private void DrawActors(Graphics g, Rectangle area, RectangleF source)
        {
            ActorSnapshot selected = null;
            PointF selectedPoint = PointF.Empty;
            foreach (var actor in actors) {
                if (!VisibleKind(actor.Type)) continue;
                PointF point;
                if (!TryProject(actor, area, source, out point)) continue;
                bool isPlayer = actor.Id == playerId, isTracked = actor.Id == trackedId;
                float radius = isPlayer || isTracked ? 6 : 3;
                Color color = isPlayer ? Color.Cyan : (isTracked ? Color.Yellow : PointColor(actor.Type));
                using (var outline = new Pen(Color.Black, 2)) g.DrawEllipse(outline, point.X - radius, point.Y - radius, radius * 2, radius * 2);
                using (var brush = new SolidBrush(color)) g.FillEllipse(brush, point.X - radius, point.Y - radius, radius * 2, radius * 2);
                if (actor.Id == selectedId) { selected = actor; selectedPoint = point; }
            }
            if (selected != null) DrawSelectedName(g, selected.Name, selectedPoint, area);
        }

        private void DrawSelectedName(Graphics g, string name, PointF point, Rectangle area)
        {
            Size size = TextRenderer.MeasureText(name, labelFont);
            int x = Math.Max(area.Left + 4, Math.Min((int)point.X + 9, area.Right - size.Width - 10));
            int y = Math.Max(area.Top + 4, Math.Min((int)point.Y - size.Height / 2, area.Bottom - size.Height - 6));
            var box = new Rectangle(x - 4, y - 2, size.Width + 8, size.Height + 4);
            using (var fill = new SolidBrush(Color.FromArgb(215, 8, 11, 17))) g.FillRoundedRectangle(fill, box, 5);
            TextRenderer.DrawText(g, name, labelFont, new Point(x, y), Color.White, TextFormatFlags.NoPadding);
        }

        private bool TryProject(ActorSnapshot actor, Rectangle area, RectangleF source, out PointF point)
        {
            PointF imagePoint = map.WorldToImage(actor.X, actor.Y);
            float x = area.Left + (imagePoint.X - source.Left) / source.Width * area.Width;
            float y = area.Top + (imagePoint.Y - source.Top) / source.Height * area.Height;
            point = new PointF(x, y);
            return x >= area.Left && x <= area.Right && y >= area.Top && y <= area.Bottom;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (map == null || map.Image == null || !MapArea.Contains(e.Location)) return;
            Rectangle area = MapArea;
            RectangleF before = SourceRectangle;
            float fx = (e.X - area.Left) / (float)area.Width;
            float fy = (e.Y - area.Top) / (float)area.Height;
            float sourceX = before.Left + before.Width * fx;
            float sourceY = before.Top + before.Height * fy;
            float next = Math.Max(1f, Math.Min(5f, zoom * (e.Delta > 0 ? 1.25f : .8f)));
            if (Math.Abs(next - zoom) < .001f) return;
            zoom = next;
            float newWidth = map.Image.Width / zoom, newHeight = map.Image.Height / zoom;
            viewCenter = new PointF(sourceX + (.5f - fx) * newWidth, sourceY + (.5f - fy) * newHeight);
            ClampViewCenter();
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left || map == null || map.Image == null || !MapArea.Contains(e.Location)) return;
            Rectangle area = MapArea;
            RectangleF source = SourceRectangle;
            uint nearest = 0;
            double best = 13 * 13;
            foreach (var actor in actors) {
                if (!VisibleKind(actor.Type)) continue;
                PointF point;
                if (!TryProject(actor, area, source, out point)) continue;
                double dx = point.X - e.X, dy = point.Y - e.Y, distance = dx * dx + dy * dy;
                if (distance < best) { best = distance; nearest = actor.Id; }
            }
            selectedId = nearest;
            Invalidate();
        }

        private bool VisibleKind(int type)
        {
            if (type == 1) return settings.ShowPlayers;
            if (type == 2 || type == 3) return settings.ShowNpcs;
            return settings.ShowOthers;
        }

        private static Color PointColor(int type)
        {
            if (type == 1) return Color.DeepSkyBlue;
            if (type == 2) return Color.OrangeRed;
            if (type == 3) return Color.LimeGreen;
            return Color.Goldenrod;
        }

        private int MapSide { get { return Math.Max(200, Math.Min(ClientSize.Width - 14, ClientSize.Height - OverlayTheme.HeaderHeight - 12)); } }
        private Rectangle MapArea { get { int side = MapSide; return new Rectangle((ClientSize.Width - side) / 2, OverlayTheme.HeaderHeight + 4, side, side); } }
        private RectangleF SourceRectangle {
            get {
                float width = map.Image.Width / zoom, height = map.Image.Height / zoom;
                float left = Math.Max(0, Math.Min(map.Image.Width - width, viewCenter.X - width / 2));
                float top = Math.Max(0, Math.Min(map.Image.Height - height, viewCenter.Y - height / 2));
                return new RectangleF(left, top, width, height);
            }
        }

        private void ClampViewCenter()
        {
            float halfWidth = map.Image.Width / zoom / 2f, halfHeight = map.Image.Height / zoom / 2f;
            viewCenter = new PointF(Math.Max(halfWidth, Math.Min(map.Image.Width - halfWidth, viewCenter.X)),
                Math.Max(halfHeight, Math.Min(map.Image.Height - halfHeight, viewCenter.Y)));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { labelFont.Dispose(); if (map != null) map.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
