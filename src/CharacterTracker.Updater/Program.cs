using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace CharacterTracker.Updater
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            string log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AetherTracker", "Updater.log");
            string temporary = null, backup = null;
            try {
                var options = Parse(args);
                string self = Assembly.GetExecutingAssembly().Location;
                if (!string.Equals(Hash(self), options["updater-sha256"], StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Updater executable checksum does not match.");
                string source = Path.GetFullPath(options["source"]), target = Path.GetFullPath(options["target"]);
                string updates = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AetherTracker", "Updates")).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!Inside(source, updates) || !Inside(AppDomain.CurrentDomain.BaseDirectory, updates)) throw new InvalidOperationException("Updater inputs are outside the private update staging directory.");
                if (!string.Equals(Path.GetFileName(source), "CharacterTracker.dll", StringComparison.OrdinalIgnoreCase) || !string.Equals(Path.GetFileName(target), "CharacterTracker.dll", StringComparison.OrdinalIgnoreCase) || !File.Exists(source) || !File.Exists(target)) throw new InvalidOperationException("Update source or target is invalid.");
                if (!string.Equals(Hash(source), options["sha256"], StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Staged DLL checksum does not match.");
                CheckAssembly(source, "Aether Tracker", options["version"]);
                CheckInstalledAssembly(target);
                if (!string.Equals(Hash(target), options["original"], StringComparison.OrdinalIgnoreCase)) throw new IOException("Installed DLL changed after update was prepared; refusing replacement.");
                int pid; if (!int.TryParse(options["wait-pid"], out pid) || pid <= 0) throw new ArgumentException("Invalid ACT process ID.");
                try { using (var process = Process.GetProcessById(pid)) if (!process.WaitForExit(30 * 60 * 1000)) throw new TimeoutException("Timed out waiting for ACT to exit."); }
                catch (ArgumentException) { }
                if (!string.Equals(Hash(target), options["original"], StringComparison.OrdinalIgnoreCase)) throw new IOException("Installed DLL changed while waiting; refusing replacement.");
                temporary = Path.Combine(Path.GetDirectoryName(target), ".CharacterTracker.update-" + Guid.NewGuid().ToString("N") + ".dll");
                backup = target + ".backup-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N");
                File.Copy(source, temporary, false);
                if (!string.Equals(Hash(temporary), options["sha256"], StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Staged copy checksum does not match.");
                AtomicPluginReplacement.Replace(temporary, target, backup, () => {
                    if (!string.Equals(Hash(target), options["sha256"], StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Installed checksum does not match.");
                    CheckAssembly(target, "Aether Tracker", options["version"]);
                }); temporary = null;
                WriteLog(log, "Updated " + target + "; backup retained at " + backup);
                return 0;
            } catch (Exception ex) {
                try { if (temporary != null && File.Exists(temporary)) File.Delete(temporary); } catch { }
                try { WriteLog(log, "FAILED: " + ex); } catch { }
                return 1;
            }
        }

        private static System.Collections.Generic.Dictionary<string, string> Parse(string[] args)
        {
            var result = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < args.Length; i += 2) {
                if (i + 1 >= args.Length || !args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Invalid updater arguments.");
                result.Add(args[i].Substring(2), args[i + 1]);
            }
            foreach (string key in new[] { "wait-pid", "source", "target", "sha256", "updater-sha256", "version", "original" }) if (!result.ContainsKey(key)) throw new ArgumentException("Missing updater argument: " + key);
            return result;
        }

        private static void CheckAssembly(string path, string product, string version)
        {
            AssemblyName name = AssemblyName.GetAssemblyName(path);
            var info = FileVersionInfo.GetVersionInfo(path);
            if (name.Name != "CharacterTracker" || info.ProductName != product || info.CompanyName != "Roxyz0501" || !VersionMatches(info.FileVersion, version)) throw new InvalidDataException("Plugin identity, author, or version did not match.");
        }
        private static void CheckInstalledAssembly(string path)
        {
            AssemblyName name = AssemblyName.GetAssemblyName(path); var info = FileVersionInfo.GetVersionInfo(path);
            if (name.Name != "CharacterTracker" || info.ProductName != "Aether Tracker" || info.CompanyName != "Roxyz0501" || !Regex.IsMatch(info.FileVersion ?? "", "^\\d+\\.\\d+\\.\\d+\\.\\d+$"))
                throw new InvalidDataException("The installed target is not an Aether Tracker assembly.");
        }
        private static bool VersionMatches(string fileVersion, string version) { return fileVersion == version + ".0" || fileVersion == version; }
        private static bool Inside(string path, string root) { string full = Path.GetFullPath(path); return full.StartsWith(root, StringComparison.OrdinalIgnoreCase); }
        private static string Hash(string path) { using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", ""); }
        private static void WriteLog(string path, string message) { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.AppendAllText(path, DateTime.UtcNow.ToString("o") + " " + message + Environment.NewLine); }
    }
}
