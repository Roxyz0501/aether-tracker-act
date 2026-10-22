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
                string api = "https://api.github.com/repos/" + Uri.EscapeDataString(UpdateConfiguration.GitHubOwner) + "/" + Uri.EscapeDataString(UpdateConfiguration.GitHubRepository) + "/releases/latest";
                byte[] bytes = await DownloadWithRedirectsAsync(new Uri(api), cancellationToken, true).ConfigureAwait(false);
                var release = UpdateValidation.ParseRelease(System.Text.Encoding.UTF8.GetString(bytes));
                if (release.Draft || release.Prerelease) throw new InvalidDataException("GitHub returned a draft or prerelease as latest stable release.");
                SemanticVersion current, latest;
                if (!SemanticVersion.TryParse(currentText, out current) || !SemanticVersion.TryParse(release.TagName, out latest)) throw new InvalidDataException("The release tag is not valid SemVer.");
                return new UpdateResult {
                    State = UpdateValidation.IsUpdateAvailable(currentText, release) ? UpdateState.Available : UpdateState.UpToDate,
                    CurrentVersion = current.ToString(), LatestVersion = latest.ToString(), Notes = Summarize(release.Body), Release = release
                };
            } catch (Exception ex) {
                return new UpdateResult { State = UpdateState.Failed, CurrentVersion = currentText, Error = FriendlyError(ex) };
            }
        }

        public async Task<UpdateResult> PreparePackageAsync(UpdateResult available, CancellationToken cancellationToken)
        {
            if (available == null || available.State != UpdateState.Available || available.Release == null) return new UpdateResult { State = UpdateState.Failed, Error = "No validated release is selected." };
            try {
                SemanticVersion version;
                if (!SemanticVersion.TryParse(available.Release.TagName, out version)) throw new InvalidDataException("Invalid release version.");
                string baseName = "Aether-Tracker-ACT-v" + version;
                var zipAsset = available.Release.Assets.FirstOrDefault(a => string.Equals(a.Name, baseName + ".zip", StringComparison.OrdinalIgnoreCase));
                var hashAsset = available.Release.Assets.FirstOrDefault(a => string.Equals(a.Name, baseName + ".sha256", StringComparison.OrdinalIgnoreCase));
                if (zipAsset == null || hashAsset == null) throw new InvalidDataException("Release assets or SHA-256 manifest are missing.");
                Uri zipUri = ValidateAssetUri(zipAsset.DownloadUrl);
                Uri hashUri = ValidateAssetUri(hashAsset.DownloadUrl);
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AetherTracker", "Updates", version.ToString());
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(folder);
                string zipPath = Path.Combine(folder, baseName + ".zip");
                byte[] package = await DownloadWithRedirectsAsync(zipUri, cancellationToken, false).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                WriteDownloadedPackage(zipPath, package);
                cancellationToken.ThrowIfCancellationRequested();
                string manifest = System.Text.Encoding.UTF8.GetString(await DownloadWithRedirectsAsync(hashUri, cancellationToken, false).ConfigureAwait(false));
                cancellationToken.ThrowIfCancellationRequested();
                if (!UpdateValidation.VerifySha256(zipPath, manifest, Path.GetFileName(zipPath))) throw new InvalidDataException("SHA-256 verification failed.");
                UpdateValidation.ValidatePackage(zipPath, version);
                return new UpdateResult { State = UpdateState.Ready, CurrentVersion = available.CurrentVersion, LatestVersion = available.LatestVersion, Notes = available.Notes };
            } catch (Exception ex) {
                return new UpdateResult { State = UpdateState.Failed, CurrentVersion = available.CurrentVersion, LatestVersion = available.LatestVersion, Error = FriendlyError(ex) };
            }
        }

        private static void WriteDownloadedPackage(string destination, byte[] bytes)
        {
            string temporary = destination + ".tmp." + Guid.NewGuid().ToString("N");
            try {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(destination)) File.Replace(temporary, destination, null);
                else File.Move(temporary, destination);
            } finally {
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
            }
        }

        private async Task<byte[]> DownloadWithRedirectsAsync(Uri uri, CancellationToken token, bool apiOnly)
        {
            Uri current = uri;
            for (int i = 0; i < 6; i++) {
                ValidateGitHubUri(current, apiOnly && i == 0);
                using (var response = await client.GetAsync(current, HttpCompletionOption.ResponseContentRead, token).ConfigureAwait(false)) {
                    if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400) {
                        Uri location = response.Headers.Location;
                        if (location == null) throw new InvalidDataException("GitHub redirect has no location.");
                        current = location.IsAbsoluteUri ? location : new Uri(current, location);
                        continue;
                    }
                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
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
            if (ex is TaskCanceledException) return "The request timed out.";
            if (ex is HttpRequestException) return ex.Message;
            return ex.Message;
        }

        public void Dispose() { client.Dispose(); }
    }
}
