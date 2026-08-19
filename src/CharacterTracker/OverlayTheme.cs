using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace CharacterTracker
{
    internal static class OverlayTheme
    {
        public const int HeaderHeight = 46;
        public const int FooterHeight = 10;
        public static readonly Color TransparencyColor = Color.FromArgb(1, 2, 3);

        public static void Configure(Form form)
        {
            form.AutoScaleMode = AutoScaleMode.None;
            form.BackColor = TransparencyColor;
            form.TransparencyKey = TransparencyColor;
            UpdateRoundedRegion(form);
        }

        public static void Prepare(Graphics graphics, int backgroundOpacity, int width, int height,
            string title, string compactTitle, string status, bool unlocked)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using (var body = new SolidBrush(Background(backgroundOpacity, 242, 13, 16, 23)))
                FillRoundedRectangle(graphics, body, new Rectangle(0, 0, width - 1, height - 1), 12);
            using (var header = new LinearGradientBrush(new Rectangle(0, 0, width, HeaderHeight),
                Background(backgroundOpacity, 250, 38, 34, 30), Background(backgroundOpacity, 246, 18, 20, 27),
                LinearGradientMode.Horizontal))
                FillTopRoundedRectangle(graphics, header, new Rectangle(0, 0, width - 1, HeaderHeight), 12);
            using (var gold = new Pen(Background(backgroundOpacity, 190, 194, 155, 82))) {
                graphics.DrawLine(gold, 12, HeaderHeight - 1, width - 12, HeaderHeight - 1);
                graphics.DrawLine(gold, 15, 9, width - 15, 9);
            }
            using (var diamond = new SolidBrush(Color.FromArgb(225, 219, 180, 102)))
                graphics.FillPolygon(diamond, new[] { new Point(14, 18), new Point(18, 22), new Point(14, 26), new Point(10, 22) });

            string displayTitle = width < 190 ? compactTitle : title;
            using (var titleFont = new Font("Georgia", width < 285 ? 8.2f : 9.2f, FontStyle.Bold))
            using (var titleBrush = new SolidBrush(Color.FromArgb(239, 221, 178)))
                graphics.DrawString(displayTitle, titleFont, titleBrush, 25, 14);

            string displayStatus = unlocked ? Localization.T("overlay.unlocked") : status;
            if (width >= 300 && !string.IsNullOrEmpty(displayStatus)) {
                using (var statusFont = new Font("Segoe UI", 7.2f, unlocked ? FontStyle.Regular : FontStyle.Bold))
                using (var statusBrush = new SolidBrush(unlocked ? Color.FromArgb(218, 201, 172, 112) : Color.FromArgb(150, 170, 180, 195))) {
                    SizeF size = graphics.MeasureString(displayStatus, statusFont);
                    graphics.DrawString(displayStatus, statusFont, statusBrush, width - size.Width - 14, 14);
                }
            }
        }

        public static void Finish(Graphics graphics, int backgroundOpacity, int width, int height, bool unlocked)
        {
            if (unlocked) DrawResizeGrip(graphics, width, height);
            using (var border = new Pen(Background(backgroundOpacity, 175, 161, 126, 70)))
                DrawRoundedRectangle(graphics, border, new Rectangle(0, 0, width - 1, height - 1), 12);
        }

        public static Color Background(int percent, int alpha, int red, int green, int blue)
        {
            return Color.FromArgb(alpha * Math.Max(0, Math.Min(100, percent)) / 100, red, green, blue);
        }

        public static Color Background(int percent, int alpha, Color color)
        {
            return Color.FromArgb(alpha * Math.Max(0, Math.Min(100, percent)) / 100, color);
        }

        public static void UpdateRoundedRegion(Form form)
        {
            if (form.ClientSize.Width <= 0 || form.ClientSize.Height <= 0) return;
            using (var path = RoundedPath(new Rectangle(0, 0, form.ClientSize.Width, form.ClientSize.Height), 12)) {
                var old = form.Region;
                form.Region = new Region(path);
                if (old != null) old.Dispose();
            }
        }

        public static void FillRoundedRectangle(Graphics graphics, Brush brush, Rectangle rectangle, int radius)
        {
            using (var path = RoundedPath(rectangle, radius)) graphics.FillPath(brush, path);
        }

        public static void DrawRoundedRectangle(Graphics graphics, Pen pen, Rectangle rectangle, int radius)
        {
            using (var path = RoundedPath(rectangle, radius)) graphics.DrawPath(pen, path);
        }

        private static GraphicsPath RoundedPath(Rectangle rectangle, int radius)
        {
            int diameter = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static void FillTopRoundedRectangle(Graphics graphics, Brush brush, Rectangle rectangle, int radius)
        {
            int diameter = radius * 2;
            using (var path = new GraphicsPath()) {
                path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
                path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
                path.AddLine(rectangle.Right, rectangle.Bottom, rectangle.Left, rectangle.Bottom);
                path.CloseFigure();
                graphics.FillPath(brush, path);
            }
        }

        private static void DrawResizeGrip(Graphics graphics, int width, int height)
        {
            var points = new[] { new Point(width - 28, height - 1), new Point(width - 1, height - 28), new Point(width - 1, height - 1) };
            using (var fill = new SolidBrush(Color.FromArgb(58, 214, 190, 132))) graphics.FillPolygon(fill, points);
            using (var pen = new Pen(Color.FromArgb(145, 229, 207, 151), 1f)) {
                graphics.DrawLine(pen, width - 14, height - 6, width - 6, height - 14);
                graphics.DrawLine(pen, width - 10, height - 6, width - 6, height - 10);
            }
        }
    }
}
