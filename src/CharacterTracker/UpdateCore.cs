using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

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
            Match match = Regex.Match(value, "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*))?(?:\\+([0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*))?$");
            if (!match.Success) return false;
            int major, minor, patch;
            if (!int.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out major) ||
                !int.TryParse(match.Groups[2].Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out minor) ||
                !int.TryParse(match.Groups[3].Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out patch)) return false;
            string pre = match.Groups[4].Success ? match.Groups[4].Value : null;
            if (pre != null) foreach (string identifier in pre.Split('.'))
                if (Regex.IsMatch(identifier, "^[0-9]+$") && identifier.Length > 1 && identifier[0] == '0') return false;
            version = new SemanticVersion { Major = major, Minor = minor, Patch = patch, PreRelease = pre };
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
            if (PreRelease == null && other.PreRelease == null) return 0;
            string[] left = PreRelease.Split('.'), right = other.PreRelease.Split('.');
            for (int i = 0; i < Math.Min(left.Length, right.Length); i++) {
                bool leftNumber = Regex.IsMatch(left[i], "^[0-9]+$"), rightNumber = Regex.IsMatch(right[i], "^[0-9]+$");
                int identifierCompare;
                if (leftNumber && rightNumber) { identifierCompare = left[i].Length.CompareTo(right[i].Length); if (identifierCompare == 0) identifierCompare = string.CompareOrdinal(left[i], right[i]); }
                else if (leftNumber != rightNumber) identifierCompare = leftNumber ? -1 : 1;
                else identifierCompare = string.CompareOrdinal(left[i], right[i]);
                if (identifierCompare != 0) return identifierCompare;
            }
            return left.Length.CompareTo(right.Length);
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
        public const int MaximumReleaseZipBytes = 64 * 1024 * 1024;
        public const int MaximumPluginDllBytes = 32 * 1024 * 1024;
        public const int MaximumManifestBytes = 16 * 1024;
        public const int MaximumApiResponseBytes = 1024 * 1024;
        public const int MaximumPackageEntries = 8;
        public const long MaximumPackageExpandedBytes = 64L * 1024 * 1024;

        public static async Task<byte[]> ReadBoundedAsync(Stream input, int maximumBytes, CancellationToken cancellationToken)
        {
            if (input == null) throw new ArgumentNullException("input");
            using (var output = new MemoryStream()) {
                await CopyBoundedAsync(input, output, maximumBytes, cancellationToken).ConfigureAwait(false);
                return output.ToArray();
            }
        }

        public static async Task<long> CopyBoundedAsync(Stream input, Stream output, long maximumBytes, CancellationToken cancellationToken)
        {
            if (input == null || output == null) throw new ArgumentNullException();
            byte[] buffer = new byte[64 * 1024];
            long total = 0;
            while (true) {
                cancellationToken.ThrowIfCancellationRequested();
                int read = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                if (read == 0) return total;
                if (read > maximumBytes - total) throw new InvalidDataException("The response exceeds its allowed size.");
                await output.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                total += read;
            }
        }

        public static long CopyBounded(Stream input, Stream output, long maximumBytes, CancellationToken cancellationToken)
        {
            if (input == null || output == null) throw new ArgumentNullException();
            byte[] buffer = new byte[64 * 1024];
            long total = 0;
            while (true) {
                cancellationToken.ThrowIfCancellationRequested();
                int read = input.Read(buffer, 0, buffer.Length);
                if (read == 0) return total;
                if (read > maximumBytes - total) throw new InvalidDataException("The package entry exceeds its allowed size.");
                output.Write(buffer, 0, read);
                total += read;
            }
        }

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
            return normalized == "CharacterTracker.dll" || normalized == "CharacterTracker.Updater.exe" || normalized == "README.md" || normalized == "UI_COMMON_SPEC.md";
        }

        public static bool IsApiRateLimit(int statusCode, string remainingHeader)
        {
            return statusCode == 429 || statusCode == 403 || string.Equals(remainingHeader, "0", StringComparison.Ordinal);
        }

        public static Uri BuildOfficialReleaseAssetUri(string owner, string repository, string tag, string assetName)
        {
            SemanticVersion version;
            if (!string.Equals(owner, "Roxyz0501", StringComparison.Ordinal) || !string.Equals(repository, "aether-tracker-act", StringComparison.Ordinal) || !SemanticVersion.TryParse(tag, out version))
                throw new InvalidDataException("Release owner, repository, or tag is invalid.");
            string package = "Aether-Tracker-ACT-v" + version + ".zip";
            if (!string.Equals(assetName, package, StringComparison.Ordinal) && !string.Equals(assetName, Path.GetFileNameWithoutExtension(package) + ".sha256", StringComparison.Ordinal))
                throw new InvalidDataException("Release asset name is not allowlisted.");
            var uri = new Uri("https://github.com/" + owner + "/" + repository + "/releases/download/" + Uri.EscapeDataString(tag) + "/" + Uri.EscapeDataString(assetName));
            if (uri.Scheme != Uri.UriSchemeHttps || uri.Host != "github.com") throw new InvalidDataException("Release asset host is invalid.");
            return uri;
        }

        public static string NormalizeRegisteredPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException("ACT did not provide a plugin path.");
            string path = value.Trim(); Uri uri;
            if (Uri.TryCreate(path, UriKind.Absolute, out uri) && uri.IsFile) path = uri.LocalPath;
            path = Path.GetFullPath(path);
            if (!string.Equals(Path.GetFileName(path), "CharacterTracker.dll", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The ACT plugin path is not CharacterTracker.dll.");
            if (!File.Exists(path)) throw new FileNotFoundException("The installed plugin DLL was not found.", path);
            return path;
        }

        public static void ValidatePackage(string zipPath, SemanticVersion expectedVersion, CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var zipInfo = new FileInfo(zipPath);
            if (!zipInfo.Exists || zipInfo.Length <= 0 || zipInfo.Length > MaximumReleaseZipBytes)
                throw new InvalidDataException("The release ZIP is missing or exceeds the allowed size.");
            bool dll = false, updater = false, readme = false, commonSpec = false;
            var names = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            using (var archive = ZipFile.OpenRead(zipPath)) {
                if (archive.Entries.Count > MaximumPackageEntries) throw new InvalidDataException("The package contains too many entries.");
                long expandedBytes = 0;
                foreach (var entry in archive.Entries) {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!IsSafeEntry(entry.FullName)) throw new InvalidDataException("Unexpected or unsafe package entry: " + entry.FullName);
                    if (!names.Add(entry.FullName)) throw new InvalidDataException("Duplicate package entry: " + entry.FullName);
                    if (entry.Length < 0 || entry.Length > MaximumPackageExpandedBytes - expandedBytes)
                        throw new InvalidDataException("The package expands beyond the allowed size.");
                    expandedBytes += entry.Length;
                    if (entry.FullName == "CharacterTracker.dll") dll = true;
                    if (entry.FullName == "CharacterTracker.Updater.exe") updater = true;
                    if (entry.FullName == "README.md") readme = true;
                    if (entry.FullName == "UI_COMMON_SPEC.md") commonSpec = true;
                }
                if (!dll || !updater || !readme || !commonSpec || archive.Entries.Count != 4) throw new InvalidDataException("The package contents do not match the expected plugin release.");
            }
            string temp = Path.Combine(Path.GetTempPath(), "AetherTracker-verify-" + Guid.NewGuid().ToString("N") + ".dll");
            try {
                using (var archive = ZipFile.OpenRead(zipPath))
                using (var input = archive.GetEntry("CharacterTracker.dll").Open())
                using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    CopyBounded(input, output, MaximumPluginDllBytes, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(temp);
                if (System.Reflection.AssemblyName.GetAssemblyName(temp).Name != "CharacterTracker") throw new InvalidDataException("The package does not contain the expected plugin assembly.");
                SemanticVersion actual;
                string fileVersion = info.FileVersion;
                if (fileVersion != null && fileVersion.EndsWith(".0", StringComparison.Ordinal) && fileVersion.Split('.').Length == 4) fileVersion = fileVersion.Substring(0, fileVersion.Length - 2);
                if (!SemanticVersion.TryParse(fileVersion, out actual) || actual.CompareTo(expectedVersion) != 0 || info.ProductName != "Aether Tracker" || info.CompanyName != "Roxyz0501")
                    throw new InvalidDataException("Plugin name or version does not match the release.");
                string helper = Path.Combine(Path.GetTempPath(), "AetherTracker-verify-" + Guid.NewGuid().ToString("N") + ".exe");
                try {
                    using (var archive = ZipFile.OpenRead(zipPath))
                    using (var input = archive.GetEntry("CharacterTracker.Updater.exe").Open())
                    using (var output = new FileStream(helper, FileMode.CreateNew, FileAccess.Write, FileShare.None)) CopyBounded(input, output, MaximumPluginDllBytes, cancellationToken);
                    var helperInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(helper);
                    if (System.Reflection.AssemblyName.GetAssemblyName(helper).Name != "CharacterTracker.Updater") throw new InvalidDataException("Unexpected helper program identity.");
                    fileVersion = helperInfo.FileVersion;
                    if (fileVersion != null && fileVersion.EndsWith(".0", StringComparison.Ordinal) && fileVersion.Split('.').Length == 4) fileVersion = fileVersion.Substring(0, fileVersion.Length - 2);
                    if (!SemanticVersion.TryParse(fileVersion, out actual) || actual.CompareTo(expectedVersion) != 0 || helperInfo.CompanyName != "Roxyz0501") throw new InvalidDataException("Updater name or version does not match the release.");
                } finally { try { if (File.Exists(helper)) File.Delete(helper); } catch { } }
            } finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
        }

    }
}
