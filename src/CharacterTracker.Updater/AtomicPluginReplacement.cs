using System;
using System.IO;

namespace CharacterTracker.Updater
{
    internal static class AtomicPluginReplacement
    {
        public static void Replace(string incoming, string target, string backup, Action verify)
        {
            File.Replace(incoming, target, backup, true);
            try { verify(); }
            catch {
                string rollback = target + ".rollback-" + Guid.NewGuid().ToString("N");
                File.Copy(backup, rollback, false);
                File.Replace(rollback, target, null, true);
                throw;
            }
        }
    }
}
