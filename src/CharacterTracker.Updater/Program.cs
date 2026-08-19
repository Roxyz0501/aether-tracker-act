using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace CharacterTracker.Updater
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            string log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AetherTracker", "Updater.log");
            try {
                if (args.Length != 3) throw new ArgumentException("Expected pending DLL, target DLL, and ACT process ID.");
                string pending = Path.GetFullPath(args[0]);
                string target = Path.GetFullPath(args[1]);
                int processId; if (!int.TryParse(args[2], out processId)) throw new ArgumentException("Invalid ACT process ID.");
                string baseDirectory = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string updateRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AetherTracker", "Updates").TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!target.StartsWith(baseDirectory, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(target) != "CharacterTracker.dll") throw new InvalidOperationException("Unsafe target path.");
                if (!pending.StartsWith(updateRoot, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(pending) != "CharacterTracker.dll") throw new InvalidOperationException("Unsafe pending path.");
                if (!File.Exists(pending) || !File.Exists(target)) throw new FileNotFoundException("Update source or installed plugin is missing.");
                try { using (var process = Process.GetProcessById(processId)) process.WaitForExit(30 * 60 * 1000); } catch (ArgumentException) { }
                string backup = target + ".backup";
                File.Copy(target, backup, true);
                try {
                    File.Copy(pending, target, true);
                    WriteLog(log, "Updated " + target + "; backup: " + backup);
                } catch {
                    if (File.Exists(backup)) File.Copy(backup, target, true);
                    throw;
                }
                return 0;
            } catch (Exception ex) {
                try { WriteLog(log, "FAILED: " + ex); } catch { }
                return 1;
            }
        }

        private static void WriteLog(string path, string message)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.AppendAllText(path, DateTime.UtcNow.ToString("o") + " " + message + Environment.NewLine);
        }
    }
}
