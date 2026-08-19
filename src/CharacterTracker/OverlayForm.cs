using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CharacterTracker
{
    internal sealed class OverlayForm : OverlayWindowBase
    {
        private ActorSnapshot player, target;
        private bool facingUp, lost;
        private readonly TrackerSettings settings;

        public OverlayForm(TrackerSettings value) : base(value.OverlayLocked)
        {
            settings = value;
            SetOpacityPercent(settings.OpacityPercent);
            MinimumSize = new Size(180, 120);
            MaximumSize = new Size(600, 300);
            RestoreSavedBounds(new Size(300, 150));
        }

        protected override OverlayAnchor PositionAnchor { get { return OverlayAnchor.CenterTop; } }
        protected override Point AnchorMargin { get { return new Point(0, 45); } }
        protected override Point SavedOffset {
            get { return new Point(settings.ArrowOffsetX, settings.ArrowOffsetY - 45); }
            set { settings.ArrowOffsetX = value.X; settings.ArrowOffsetY = value.Y + 45; }
        }
        protected override Size SavedSize {
            get { return new Size(settings.ArrowWidth, settings.ArrowHeight); }
            set { settings.ArrowWidth = value.Width; settings.ArrowHeight = value.Height; }
        }
        protected override bool ClickThroughWhenLocked { get { return true; } }
        protected override void SaveSettings() { settings.Save(); }

        public void SetTracking(ActorSnapshot localPlayer, ActorSnapshot tracked, bool playerFacingUp, bool isLost)
        {
            player = localPlayer; target = tracked; facingUp = playerFacingUp; lost = isLost;
            if (!IsDisposed) Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            string headerStatus = lost ? Localization.T("overlay.lost") : (target == null ? Localization.T("overlay.waiting") : Localization.T("overlay.tracking"));
            OverlayTheme.Prepare(e.Graphics, settings.BackgroundOpacityPercent, Width, Height,
                "AETHER COMPASS", "NAV", headerStatus, !IsOverlayLocked);
            if (target == null) {
                OverlayTheme.Finish(e.Graphics, settings.BackgroundOpacityPercent, Width, Height, !IsOverlayLocked);
                return;
            }
            string title = lost ? Localization.T("tracking.lost") : target.Name;
            using (var font = new Font("Yu Gothic UI Semibold", ClientSize.Width < 240 ? 8.5f : 9.5f, FontStyle.Bold))
            using (var brush = new SolidBrush(lost ? Color.Orange : Color.White)) {
                var size = e.Graphics.MeasureString(title, font);
                e.Graphics.DrawString(title, font, brush, Math.Max(12, (Width - size.Width) / 2), OverlayTheme.HeaderHeight + 5);
            }
            if (player == null || lost) {
                OverlayTheme.Finish(e.Graphics, settings.BackgroundOpacityPercent, Width, Height, !IsOverlayLocked);
                return;
            }
            double radians = TrackingMath.RelativeDirectionRadians(player, target, facingUp);
            double distance = TrackingMath.HorizontalDistance(player, target);
            float contentHeight = Math.Max(58, ClientSize.Height - OverlayTheme.HeaderHeight);
            float arrowScale = Math.Max(.55f, Math.Min(1f, contentHeight / 104f));
            var state = e.Graphics.Save();
            e.Graphics.TranslateTransform(ClientSize.Width / 2f, OverlayTheme.HeaderHeight + contentHeight * .52f);
            e.Graphics.RotateTransform((float)(radians * 180 / Math.PI));
            e.Graphics.ScaleTransform(arrowScale, arrowScale);
            PointF[] arrow = { new PointF(0, -23), new PointF(15, 9), new PointF(6, 6), new PointF(6, 20), new PointF(-6, 20), new PointF(-6, 6), new PointF(-15, 9) };
            using (var pen = new Pen(Color.FromArgb(230, 5, 7, 10), 4) { LineJoin = LineJoin.Round }) e.Graphics.DrawPolygon(pen, arrow);
            using (var brush = new SolidBrush(Color.FromArgb(245, 239, 194, 82))) e.Graphics.FillPolygon(brush, arrow);
            e.Graphics.Restore(state);
            string info = string.Format("{0:0.0} yalms  {1}", distance, Localization.F("height", target.Z - player.Z));
            using (var font = new Font("Segoe UI Semibold", ClientSize.Width < 240 ? 7.5f : 8.5f, FontStyle.Bold))
            using (var brush = new SolidBrush(Color.FromArgb(225, 236, 226, 205))) {
                var size = e.Graphics.MeasureString(info, font);
                e.Graphics.DrawString(info, font, brush, Math.Max(8, (Width - size.Width) / 2), Height - size.Height - 8);
            }
            OverlayTheme.Finish(e.Graphics, settings.BackgroundOpacityPercent, Width, Height, !IsOverlayLocked);
        }

    }

    internal static class DrawingExtensions
    {
        public static void FillRoundedRectangle(this Graphics graphics, Brush brush, Rectangle rect, int radius)
        {
            int d = radius * 2;
            using (var path = new GraphicsPath()) {
                path.AddArc(rect.Left, rect.Top, d, d, 180, 90); path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
                path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90); path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
                path.CloseFigure(); graphics.FillPath(brush, path);
            }
        }
    }
}
