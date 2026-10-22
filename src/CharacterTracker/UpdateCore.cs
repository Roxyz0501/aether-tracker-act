using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace CharacterTracker
{
    public sealed class SemanticVersion : IComparable<SemanticVersion>
    {
        public int Major, Minor, Patch;
        public string PreRelease;

        public static bool TryParse(string text, out SemanticVersion version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string value = text.Trim();
            if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase)) value = value.Substring(1);
            int plus = value.IndexOf('+'); if (plus >= 0) value = value.Substring(0, plus);
            string pre = null; int dash = value.IndexOf('-'); if (dash >= 0) { pre = value.Substring(dash + 1); value = value.Substring(0, dash); }
            string[] parts = value.Split('.'); int major, minor, patch;
            if (parts.Length != 3 || !int.TryParse(parts[0], out major) || !int.TryParse(parts[1], out minor) || !int.TryParse(parts[2], out patch) || major < 0 || minor < 0 || patch < 0) return false;
            version = new SemanticVersion { Major = major, Minor = minor, Patch = patch, PreRelease = string.IsNullOrEmpty(pre) ? null : pre };
            return true;
        }

        public int CompareTo(SemanticVersion other)
        {
            if (other == null) return 1;
            int value = Major.CompareTo(other.Major); if (value != 0) return value;
            value = Minor.CompareTo(other.Minor); if (value != 0) return value;
            value = Patch.CompareTo(other.Patch); if (value != 0) return value;
            if (PreRelease == null && other.PreRelease != null) return 1;
            if (PreRelease != null && other.PreRelease == null) return -1;
            return string.Compare(PreRelease, other.PreRelease, StringComparison.OrdinalIgnoreCase);
        }

        public override string ToString() { return Major + "." + Minor + "." + Patch + (PreRelease == null ? "" : "-" + PreRelease); }
    }

    [DataContract]
    public sealed class GitHubRelease
    {
        [DataMember(Name = "tag_name")] public string TagName { get; set; }
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "body")] public string Body { get; set; }
        [DataMember(Name = "draft")] public bool Draft { get; set; }
        [DataMember(Name = "prerelease")] public bool Prerelease { get; set; }
        [DataMember(Name = "assets")] public List<GitHubAsset> Assets { get; set; }
    }

    [DataContract]
    public sealed class GitHubAsset
    {
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "browser_download_url")] public string DownloadUrl { get; set; }
    }

    public static class UpdateValidation
    {
        public static GitHubRelease ParseRelease(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("Empty GitHub response.");
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json))) {
                var value = (GitHubRelease)new DataContractJsonSerializer(typeof(GitHubRelease)).ReadObject(stream);
                if (value == null || string.IsNullOrWhiteSpace(value.TagName)) throw new InvalidDataException("GitHub response has no release tag.");
                if (value.Assets == null) value.Assets = new List<GitHubAsset>();
                return value;
            }
        }

        public static bool IsUpdateAvailable(string currentVersion, GitHubRelease release)
        {
            if (release == null || release.Draft || release.Prerelease) return false;
            SemanticVersion current, latest;
            if (!SemanticVersion.TryParse(currentVersion, out current) || !SemanticVersion.TryParse(release.TagName, out latest)) throw new InvalidDataException("The current version or release tag is not valid SemVer.");
            return latest.CompareTo(current) > 0;
        }

        public static string Sha256(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
        }

        public static bool VerifySha256(string path, string manifest, string expectedFileName)
        {
            if (string.IsNullOrWhiteSpace(manifest)) return false;
            foreach (string raw in manifest.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
                string line = raw.Trim();
                if (line.Length < 64) continue;
                string hash = line.Substring(0, 64);
                string name = line.Substring(64).Trim().TrimStart('*');
                if (string.Equals(name, expectedFileName, StringComparison.OrdinalIgnoreCase) && string.Equals(hash, Sha256(path), StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public static bool IsSafeEntry(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || Path.IsPathRooted(name)) return false;
            string normalized = name.Replace('\\', '/');
            if (normalized.StartsWith("/", StringComparison.Ordinal) || normalized.Contains("../") || normalized.Contains(":")) return false;
            return normalized == "CharacterTracker.dll" || normalized == "README.md" || normalized == "UI_COMMON_SPEC.md";
        }

        public static void ValidatePackage(string zipPath, SemanticVersion expectedVersion)
        {
            bool dll = false;
            using (var archive = ZipFile.OpenRead(zipPath)) {
                foreach (var entry in archive.Entries) {
                    if (!IsSafeEntry(entry.FullName)) throw new InvalidDataException("Unexpected or unsafe package entry: " + entry.FullName);
                    if (entry.FullName == "CharacterTracker.dll") dll = true;
                }
                if (!dll) throw new InvalidDataException("The package does not contain the expected plugin DLL.");
            }
            string temp = Path.Combine(Path.GetTempPath(), "AetherTracker-verify-" + Guid.NewGuid().ToString("N") + ".dll");
            try {
                using (var archive = ZipFile.OpenRead(zipPath))
                using (var input = archive.GetEntry("CharacterTracker.dll").Open())
                using (var output = File.Create(temp)) input.CopyTo(output);
                var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(temp);
                SemanticVersion actual;
                string fileVersion = info.FileVersion;
                if (fileVersion != null && fileVersion.EndsWith(".0", StringComparison.Ordinal) && fileVersion.Split('.').Length == 4) fileVersion = fileVersion.Substring(0, fileVersion.Length - 2);
                if (!SemanticVersion.TryParse(fileVersion, out actual) || actual.CompareTo(expectedVersion) != 0 || info.ProductName != "Aether Tracker")
                    throw new InvalidDataException("Plugin name or version does not match the release.");
            } finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
        }

    }
}
