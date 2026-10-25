using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CharacterTracker
{
    internal enum OverlayAnchor { CenterTop, LeftTop, RightTop }

    internal abstract class OverlayWindowBase : Form
    {
        private readonly Timer positionTimer;
        private bool overlayLocked;
        private bool dragging;
        private Point dragStartScreen;
        private Point dragStartLocation;
        private IntPtr gameWindow;
        private bool visualDirty = true;
        private static readonly object gameWindowGate = new object();
        private static IntPtr cachedGameWindow;
        private static long lastGameWindowLookup;
        protected bool IsOverlayLocked { get { return overlayLocked; } }

        protected OverlayWindowBase(bool locked)
        {
            overlayLocked = locked;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            OverlayTheme.Configure(this);
            positionTimer = new Timer { Interval = 1000 };
            positionTimer.Tick += (s, e) => Reposition();
            positionTimer.Start();
        }

        protected abstract OverlayAnchor PositionAnchor { get; }
        protected abstract Point SavedOffset { get; set; }
        protected abstract Size SavedSize { get; set; }
        protected virtual Point AnchorMargin { get { return new Point(24, 90); } }
        protected virtual bool ClickThroughWhenLocked { get { return false; } }
        protected virtual bool IsDragSurface(Point point) { return true; }

        public bool HasGameWindow { get { return gameWindow != IntPtr.Zero; } }
        public bool IsGameOrSelfForeground {
            get { var foreground = GetForegroundWindow(); return gameWindow != IntPtr.Zero && (foreground == gameWindow || foreground == Handle); }
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams {
            get {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80 | 0x08000000;
                if (overlayLocked && ClickThroughWhenLocked) cp.ExStyle |= 0x20;
                return cp;
            }
        }

        public void SetOverlayLocked(bool locked)
        {
            if (overlayLocked == locked) return;
            overlayLocked = locked;
            if (IsHandleCreated) RecreateHandle();
            Invalidate();
        }

        public void SetOpacityPercent(int percent)
        {
            double value = Math.Max(.35, Math.Min(1.0, percent / 100.0));
            if (Math.Abs(Opacity - value) < .001) return;
            Opacity = value;
            RequestRedraw();
        }

        protected void RequestRedraw()
        {
            visualDirty = true;
            if (!Visible || !IsHandleCreated) return;
            visualDirty = false;
            TrackerRuntimeMetrics.RedrawRequested();
            Invalidate();
        }

        public void RefreshVisuals() { RequestRedraw(); }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible && visualDirty) RequestRedraw();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            TrackerRuntimeMetrics.Painted();
            base.OnPaint(e);
        }

        public void RestoreSavedBounds(Size defaultSize)
        {
            Size saved = SavedSize;
            Size = new Size(saved.Width > 0 ? saved.Width : defaultSize.Width, saved.Height > 0 ? saved.Height : defaultSize.Height);
            Reposition();
        }

        private Point AnchorLocation(Rectangle rect)
        {
            Point margin = AnchorMargin;
            switch (PositionAnchor) {
                case OverlayAnchor.CenterTop: return new Point(rect.Left + (rect.Width - Width) / 2, rect.Top + margin.Y);
                case OverlayAnchor.RightTop: return new Point(rect.Right - Width - margin.X, rect.Top + margin.Y);
                default: return new Point(rect.Left + margin.X, rect.Top + margin.Y);
            }
        }

        protected void Reposition()
        {
            gameWindow = FindGameWindow();
            if (gameWindow == IntPtr.Zero || dragging) return;
            RECT nativeRect;
            if (!GetWindowRect(gameWindow, out nativeRect)) return;
            var rect = Rectangle.FromLTRB(nativeRect.Left, nativeRect.Top, nativeRect.Right, nativeRect.Bottom);
            Point anchor = AnchorLocation(rect);
            Point offset = SavedOffset;
            Location = new Point(anchor.X + offset.X, anchor.Y + offset.Y);
        }

        private void SavePlacement()
        {
            if (gameWindow == IntPtr.Zero) return;
            RECT nativeRect;
            if (!GetWindowRect(gameWindow, out nativeRect)) return;
            var rect = Rectangle.FromLTRB(nativeRect.Left, nativeRect.Top, nativeRect.Right, nativeRect.Bottom);
            Point anchor = AnchorLocation(rect);
            SavedOffset = new Point(Left - anchor.X, Top - anchor.Y);
            SavedSize = Size;
            SaveSettings();
        }

        protected abstract void SaveSettings();

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (overlayLocked || e.Button != MouseButtons.Left || !IsDragSurface(e.Location)) return;
            dragging = true;
            Capture = true;
            dragStartScreen = Cursor.Position;
            dragStartLocation = Location;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragging) return;
            Point now = Cursor.Position;
            Location = new Point(dragStartLocation.X + now.X - dragStartScreen.X, dragStartLocation.Y + now.Y - dragStartScreen.Y);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!dragging) return;
            dragging = false;
            Capture = false;
            SavePlacement();
        }

        protected override void OnResizeEnd(EventArgs e)
        {
            base.OnResizeEnd(e);
            if (!overlayLocked) SavePlacement();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            OverlayTheme.UpdateRoundedRegion(this);
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x84, HTCLIENT = 1;
            if (m.Msg == WM_NCHITTEST && !overlayLocked) {
                base.WndProc(ref m);
                if ((int)m.Result == HTCLIENT) {
                    Point p = PointToClient(new Point((short)((long)m.LParam & 0xffff), (short)(((long)m.LParam >> 16) & 0xffff)));
                    const int grip = 7;
                    bool left = p.X < grip, right = p.X >= ClientSize.Width - grip;
                    bool top = p.Y < grip, bottom = p.Y >= ClientSize.Height - grip;
                    if (left && top) m.Result = (IntPtr)13;
                    else if (right && top) m.Result = (IntPtr)14;
                    else if (left && bottom) m.Result = (IntPtr)16;
                    else if (right && bottom) m.Result = (IntPtr)17;
                    else if (left) m.Result = (IntPtr)10;
                    else if (right) m.Result = (IntPtr)11;
                    else if (top) m.Result = (IntPtr)12;
                    else if (bottom) m.Result = (IntPtr)15;
                }
                return;
            }
            base.WndProc(ref m);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) positionTimer.Dispose();
            base.Dispose(disposing);
        }

        private static IntPtr FindGameWindow()
        {
            lock (gameWindowGate) {
                long now = Stopwatch.GetTimestamp();
                if (lastGameWindowLookup != 0 && (now - lastGameWindowLookup) * 1000 / Stopwatch.Frequency < 1000) return cachedGameWindow;
                lastGameWindowLookup = now;
                IntPtr found = IntPtr.Zero;
                foreach (string name in new[] { "ffxiv_dx11", "ffxiv" })
                    foreach (var process in Process.GetProcessesByName(name))
                        try { if (found == IntPtr.Zero && process.MainWindowHandle != IntPtr.Zero) found = process.MainWindowHandle; }
                        finally { process.Dispose(); }
                cachedGameWindow = found;
                return found;
            }
        }

        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    }
}
