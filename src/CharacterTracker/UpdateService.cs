using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace CharacterTracker
{
    public static class UpdateConfiguration
    {
        public const string GitHubOwner = "Roxyz0501";
        public const string GitHubRepository = "aether-tracker-act";
        public static bool IsConfigured { get { return GitHubOwner.Length > 0 && GitHubRepository.Length > 0; } }
    }

    public enum UpdateState { Unconfigured, Checking, UpToDate, Available, Failed, Downloading, Ready }

    public sealed class UpdateResult
    {
        public UpdateState State;
        public string CurrentVersion;
        public string LatestVersion;
        public string Notes;
        public string Error;
        public GitHubRelease Release;
        public string StagedDllPath;
        public string StagedUpdaterPath;
        public string StagedDllSha256;
        public string StagedUpdaterSha256;
        public string TargetPath;
        public string OriginalTargetSha256;
        public bool ApiRateLimited;
    }

    public sealed class UpdateService : IDisposable
    {
        private readonly HttpClient client;

        public UpdateService()
        {
            var handler = new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate };
            client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Aether-Tracker-ACT/1.6 (+https://ko-fi.com/roxyz0501)");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        }

        public async Task<UpdateResult> CheckAsync(CancellationToken cancellationToken)
        {
            string currentText = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
            if (!UpdateConfiguration.IsConfigured) return new UpdateResult { State = UpdateState.Unconfigured, CurrentVersion = currentText };
            try {
                string latestTag = await ResolveLatestStableTagAsync(cancellationToken).ConfigureAwait(false);
                SemanticVersion current, latest;
                if (!SemanticVersion.TryParse(currentText, out current) || !SemanticVersion.TryParse(latestTag, out latest)) throw new InvalidDataException("The release tag is not valid SemVer.");
                if (latest.CompareTo(current) <= 0) return new UpdateResult { State = UpdateState.UpToDate, CurrentVersion = current.ToString(), LatestVersion = latest.ToString() };
                GitHubRelease release;
                bool rateLimited = false;
                try { release = await FetchReleaseByTagAsync(latest.ToString(), cancellationToken).ConfigureAwait(false); }
                catch (GitHubRateLimitException) {
                    rateLimited = true;
                    string baseUrl = "https://github.com/" + UpdateConfiguration.GitHubOwner + "/" + UpdateConfiguration.GitHubRepository + "/releases/download/" + Uri.EscapeDataString(latest.ToString()) + "/";
                    string package = "Aether-Tracker-ACT-v" + latest + ".zip";
                    release = new GitHubRelease { TagName = "v" + latest, Draft = false, Prerelease = false, Body = "", Assets = new System.Collections.Generic.List<GitHubAsset> {
                        new GitHubAsset { Name = package, DownloadUrl = baseUrl + Uri.EscapeDataString(package) },
                        new GitHubAsset { Name = Path.GetFileNameWithoutExtension(package) + ".sha256", DownloadUrl = baseUrl + Uri.EscapeDataString(Path.GetFileNameWithoutExtension(package) + ".sha256") }
                    } };
                }
                if (!string.Equals(release.TagName.TrimStart('v', 'V'), latest.ToString(), StringComparison.OrdinalIgnoreCase) || release.Draft || release.Prerelease)
                    throw new InvalidDataException("The GitHub release did not match the verified latest stable tag.");
                return new UpdateResult {
                    State = UpdateState.Available, CurrentVersion = current.ToString(), LatestVersion = latest.ToString(), Notes = Summarize(release.Body), Release = release, ApiRateLimited = rateLimited
                };
            } catch (Exception ex) {
                bool network = ex is HttpRequestException || ex is TaskCanceledException;
                return new UpdateResult { State = UpdateState.Failed, CurrentVersion = currentText, Error = network ? "network" : FriendlyError(ex) };
            }
        }

        private async Task<string> ResolveLatestStableTagAsync(CancellationToken token)
        {
            Uri current = new Uri("https://github.com/" + UpdateConfiguration.GitHubOwner + "/" + UpdateConfiguration.GitHubRepository + "/releases/latest");
            for (int i = 0; i < 6; i++) {
                if (current.Scheme != Uri.UriSchemeHttps || !string.Equals(current.Host, "github.com", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Latest-release redirect left github.com.");
                using (var request = new HttpRequestMessage(HttpMethod.Head, current))
                using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false)) {
                    if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400) {
                        if (response.Headers.Location == null) throw new InvalidDataException("Latest-release redirect had no location.");
                        current = response.Headers.Location.IsAbsoluteUri ? response.Headers.Location : new Uri(current, response.Headers.Location); continue;
                    }
                    response.EnsureSuccessStatusCode();
                    string[] parts = current.AbsolutePath.TrimEnd('/').Split('/');
                    if (parts.Length < 2 || !string.Equals(parts[parts.Length - 2], "tag", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("GitHub did not resolve a stable release tag.");
                    string tag = Uri.UnescapeDataString(parts[parts.Length - 1]); SemanticVersion parsed;
                    if (!SemanticVersion.TryParse(tag, out parsed)) throw new InvalidDataException("The latest stable release tag is not SemVer.");
                    return parsed.ToString();
                }
            }
            throw new InvalidDataException("Too many latest-release redirects.");
        }

        private async Task<GitHubRelease> FetchReleaseByTagAsync(string tag, CancellationToken token)
        {
            string api = "https://api.github.com/repos/" + UpdateConfiguration.GitHubOwner + "/" + UpdateConfiguration.GitHubRepository + "/releases/tags/" + Uri.EscapeDataString("v" + tag);
            ValidateGitHubUri(new Uri(api), true);
            using (var response = await client.GetAsync(api, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false)) {
                string remaining = response.Headers.Contains("X-RateLimit-Remaining") ? response.Headers.GetValues("X-RateLimit-Remaining").FirstOrDefault() : null;
                if (UpdateValidation.IsApiRateLimit((int)response.StatusCode, remaining)) throw new GitHubRateLimitException();
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value > UpdateValidation.MaximumApiResponseBytes) throw new InvalidDataException("The GitHub API response is too large.");
                using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false)) {
                    var json = System.Text.Encoding.UTF8.GetString(await UpdateValidation.ReadBoundedAsync(stream, UpdateValidation.MaximumApiResponseBytes, token).ConfigureAwait(false));
                    return UpdateValidation.ParseRelease(json);
                }
            }
        }

        private sealed class GitHubRateLimitException : Exception { }

        public async Task<UpdateResult> PreparePackageAsync(UpdateResult available, string targetPath, CancellationToken cancellationToken)
        {
            if (available == null || available.State != UpdateState.Available || available.Release == null) return new UpdateResult { State = UpdateState.Failed, Error = "No validated release is selected." };
            string folder = null;
            try {
                SemanticVersion version;
                if (!SemanticVersion.TryParse(available.Release.TagName, out version)) throw new InvalidDataException("Invalid release version.");
                string baseName = "Aether-Tracker-ACT-v" + version;
                var zipAsset = available.Release.Assets.FirstOrDefault(a => string.Equals(a.Name, baseName + ".zip", StringComparison.OrdinalIgnoreCase));
                var hashAsset = available.Release.Assets.FirstOrDefault(a => string.Equals(a.Name, baseName + ".sha256", StringComparison.OrdinalIgnoreCase));
                if (zipAsset == null || hashAsset == null) throw new InvalidDataException("Release assets or SHA-256 manifest are missing.");
                Uri zipUri = ValidateAssetUri(zipAsset.DownloadUrl);
                Uri hashUri = ValidateAssetUri(hashAsset.DownloadUrl);
                targetPath = UpdateValidation.NormalizeRegisteredPath(targetPath);
                string originalHash = UpdateValidation.Sha256(targetPath);
                folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AetherTracker", "Updates", version.ToString(), Guid.NewGuid().ToString("N"));
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(folder);
                string zipPath = Path.Combine(folder, baseName + ".zip");
                byte[] package = await DownloadWithRedirectsAsync(zipUri, cancellationToken, false, UpdateValidation.MaximumReleaseZipBytes).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                await WriteDownloadedPackageAsync(zipPath, package, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                string manifest = System.Text.Encoding.UTF8.GetString(await DownloadWithRedirectsAsync(hashUri, cancellationToken, false, UpdateValidation.MaximumManifestBytes).ConfigureAwait(false));
                cancellationToken.ThrowIfCancellationRequested();
                if (!UpdateValidation.VerifySha256(zipPath, manifest, Path.GetFileName(zipPath))) throw new InvalidDataException("SHA-256 verification failed.");
                UpdateValidation.ValidatePackage(zipPath, version, cancellationToken);
                string stage = Path.Combine(folder, "package"); Directory.CreateDirectory(stage);
                ExtractValidatedPackage(zipPath, stage, cancellationToken);
                string dll = Path.Combine(stage, "CharacterTracker.dll"), helper = Path.Combine(stage, "CharacterTracker.Updater.exe");
                return new UpdateResult { State = UpdateState.Ready, CurrentVersion = available.CurrentVersion, LatestVersion = available.LatestVersion, Notes = available.Notes,
                    StagedDllPath = dll, StagedUpdaterPath = helper, StagedDllSha256 = UpdateValidation.Sha256(dll), StagedUpdaterSha256 = UpdateValidation.Sha256(helper),
                    TargetPath = targetPath, OriginalTargetSha256 = originalHash };
            } catch (Exception ex) {
                TryDeleteStagingFolder(folder);
                return new UpdateResult { State = UpdateState.Failed, CurrentVersion = available.CurrentVersion, LatestVersion = available.LatestVersion, Error = FriendlyError(ex) };
            }
        }

        private static void TryDeleteStagingFolder(string folder)
        {
            try {
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;
                string root = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AetherTracker", "Updates")).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string target = Path.GetFullPath(folder);
                if (target.StartsWith(root, StringComparison.OrdinalIgnoreCase) && new DirectoryInfo(target).Name.Length == 32) Directory.Delete(target, true);
            } catch { }
        }

        private static void ExtractValidatedPackage(string zipPath, string root, CancellationToken token)
        {
            string canonicalRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            using (var archive = ZipFile.OpenRead(zipPath)) foreach (var entry in archive.Entries) {
                token.ThrowIfCancellationRequested();
                if (!UpdateValidation.IsSafeEntry(entry.FullName)) throw new InvalidDataException("Unexpected package entry.");
                string destination = Path.GetFullPath(Path.Combine(canonicalRoot, entry.FullName));
                if (!destination.StartsWith(canonicalRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Package path escapes staging directory.");
                using (var input = entry.Open()) using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    UpdateValidation.CopyBounded(input, output, UpdateValidation.MaximumPackageExpandedBytes, token);
            }
        }

        private static async Task WriteDownloadedPackageAsync(string destination, byte[] bytes, CancellationToken cancellationToken)
        {
            string temporary = destination + ".tmp." + Guid.NewGuid().ToString("N");
            try {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                    using (var input = new MemoryStream(bytes, false))
                        await UpdateValidation.CopyBoundedAsync(input, stream, UpdateValidation.MaximumReleaseZipBytes, cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    stream.Flush(true);
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(destination)) {
                    string backup = destination + ".bak." + Guid.NewGuid().ToString("N");
                    File.Replace(temporary, destination, backup);
                    try { File.Delete(backup); } catch { }
                }
                else File.Move(temporary, destination);
            } finally {
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
            }
        }

        private async Task<byte[]> DownloadWithRedirectsAsync(Uri uri, CancellationToken token, bool apiOnly, int maximumBytes)
        {
            Uri current = uri;
            for (int i = 0; i < 6; i++) {
                ValidateGitHubUri(current, apiOnly && i == 0);
                using (var response = await client.GetAsync(current, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false)) {
                    if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400) {
                        Uri location = response.Headers.Location;
                        if (location == null) throw new InvalidDataException("GitHub redirect has no location.");
                        current = location.IsAbsoluteUri ? location : new Uri(current, location);
                        continue;
                    }
                    response.EnsureSuccessStatusCode();
                    long? contentLength = response.Content.Headers.ContentLength;
                    if (contentLength.HasValue && contentLength.Value > maximumBytes)
                        throw new InvalidDataException("The release response exceeds its allowed size.");
                    using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        return await UpdateValidation.ReadBoundedAsync(stream, maximumBytes, token).ConfigureAwait(false);
                }
            }
            throw new InvalidDataException("Too many GitHub redirects.");
        }

        private static Uri ValidateAssetUri(string text)
        {
            Uri uri;
            if (!Uri.TryCreate(text, UriKind.Absolute, out uri)) throw new InvalidDataException("Invalid release asset URL.");
            ValidateGitHubUri(uri, false);
            return uri;
        }

        private static void ValidateGitHubUri(Uri uri, bool apiOnly)
        {
            if (uri == null || uri.Scheme != Uri.UriSchemeHttps) throw new InvalidDataException("Only HTTPS GitHub URLs are allowed.");
            string host = uri.Host.ToLowerInvariant();
            bool allowed = host == "api.github.com" || host == "github.com" || host.EndsWith(".githubusercontent.com", StringComparison.Ordinal);
            if (!allowed || (apiOnly && host != "api.github.com")) throw new InvalidDataException("The update URL is not an allowed GitHub host.");
        }

        private static string Summarize(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return "";
            string text = body.Trim().Replace("\r\n", "\n");
            return text.Length <= 1200 ? text : text.Substring(0, 1200) + "…";
        }

        private static string FriendlyError(Exception ex)
        {
            if (ex is TaskCanceledException || ex is HttpRequestException) return "network";
            return "package";
        }

        public void Dispose() { client.Dispose(); }
    }
}
