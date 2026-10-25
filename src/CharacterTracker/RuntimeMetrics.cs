using System;
using System.Threading;

namespace CharacterTracker
{
    internal static class TrackerRefreshPolicy
    {
        public static bool ShouldAcquireActors(bool controlVisible, bool arrowVisible, bool listVisible, bool mapVisible)
        { return controlVisible || arrowVisible || listVisible || mapVisible; }

        public static int PollInterval(bool arrowVisible, bool tracking, bool listVisible, bool mapVisible, int arrowMs, int listMs)
        {
            if (arrowVisible && tracking) return Math.Max(100, arrowMs);
            if (listVisible || mapVisible) return Math.Max(500, listMs);
            return 1000;
        }
    }

    internal static class TrackerRuntimeMetrics
    {
        private static long actorReadCount, redrawRequestCount, paintCount;
        public static long ActorReadCount { get { return Interlocked.Read(ref actorReadCount); } }
        public static long RedrawRequestCount { get { return Interlocked.Read(ref redrawRequestCount); } }
        public static long PaintCount { get { return Interlocked.Read(ref paintCount); } }
        public static void ActorRead() { Interlocked.Increment(ref actorReadCount); }
        public static void RedrawRequested() { Interlocked.Increment(ref redrawRequestCount); }
        public static void Painted() { Interlocked.Increment(ref paintCount); }
        public static void Reset() { Interlocked.Exchange(ref actorReadCount, 0); Interlocked.Exchange(ref redrawRequestCount, 0); Interlocked.Exchange(ref paintCount, 0); }
    }

    internal sealed class OverlayRenderGate
    {
        private string last;
        public bool Update(string state)
        {
            if (string.Equals(last, state, StringComparison.Ordinal)) return false;
            last = state;
            return true;
        }
    }
}
