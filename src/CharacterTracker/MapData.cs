using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CharacterTracker
{
    internal sealed class MapData : IDisposable
    {
        public int MapId { get; set; }
        public string AssetId { get; set; }
        public double SizeFactor { get; set; }
        public double OffsetX { get; set; }
        public double OffsetY { get; set; }
        public Image Image { get; set; }

        public PointF WorldToImage(float worldX, float worldY)
        {
            double scale = SizeFactor / 100.0;
            double px = ((worldX + OffsetX) * scale + 1024.0) / 2048.0 * Image.Width;
            double py = ((worldY + OffsetY) * scale + 1024.0) / 2048.0 * Image.Height;
            return new PointF((float)px, (float)py);
        }

        public void Dispose() { if (Image != null) { Image.Dispose(); Image = null; } }
    }

    internal interface IMapDataSource
    {
        Task<string> DownloadStringAsync(string url, CancellationToken cancellationToken);
        Task<byte[]> DownloadBytesAsync(string url, CancellationToken cancellationToken);
    }

    internal sealed class WebMapDataSource : IMapDataSource
    {
        private const int RequestTimeoutMilliseconds = 20000;
        private const int MaximumSheetResponseBytes = 256 * 1024;
        internal const int MaximumMapImageBytes = 16 * 1024 * 1024;

        public Task<string> DownloadStringAsync(string url, CancellationToken cancellationToken)
        {
            return DownloadStringCoreAsync(url, cancellationToken);
        }

        public Task<byte[]> DownloadBytesAsync(string url, CancellationToken cancellationToken)
        {
            return DownloadBytesCoreAsync(url, cancellationToken);
        }

        private static async Task<string> DownloadStringCoreAsync(string url, CancellationToken cancellationToken)
        {
            byte[] bytes = await DownloadBoundedAsync(url, cancellationToken, MaximumSheetResponseBytes).ConfigureAwait(false);
            return Encoding.UTF8.GetString(bytes);
        }

        private static async Task<byte[]> DownloadBytesCoreAsync(string url, CancellationToken cancellationToken)
        {
            return await DownloadBoundedAsync(url, cancellationToken, MaximumMapImageBytes).ConfigureAwait(false);
        }

        private static async Task<byte[]> DownloadBoundedAsync(string url, CancellationToken cancellationToken, int maximumBytes)
        {
            Uri current = new Uri(url, UriKind.Absolute);
            if (current.Scheme != Uri.UriSchemeHttps) throw new InvalidDataException("Map data must be requested over HTTPS.");
            for (int redirects = 0; redirects <= 5; redirects++) {
                cancellationToken.ThrowIfCancellationRequested();
                var request = (HttpWebRequest)WebRequest.Create(current);
                request.AllowAutoRedirect = false;
                request.Timeout = RequestTimeoutMilliseconds;
                request.ReadWriteTimeout = RequestTimeoutMilliseconds;
                request.UserAgent = "Aether-Tracker-ACT/1.6";
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)) {
                  timeout.CancelAfter(RequestTimeoutMilliseconds);
                  using (timeout.Token.Register(() => { try { request.Abort(); } catch { } })) {
                    HttpWebResponse response = null;
                    try { response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false); }
                    catch (WebException ex) {
                        response = ex.Response as HttpWebResponse;
                        if (response == null) {
                            if (timeout.IsCancellationRequested) throw new OperationCanceledException(timeout.Token);
                            throw;
                        }
                    }
                    using (response) {
                        int status = (int)response.StatusCode;
                        if (status >= 300 && status < 400) {
                            string location = response.Headers[HttpResponseHeader.Location];
                            if (string.IsNullOrWhiteSpace(location) || redirects == 5) throw new InvalidDataException("Map request has an invalid redirect.");
                            current = new Uri(current, location);
                            if (current.Scheme != Uri.UriSchemeHttps) throw new InvalidDataException("Map redirects must remain on HTTPS.");
                            continue;
                        }
                        if (status < 200 || status >= 300) throw new WebException("Map server returned HTTP " + status.ToString(CultureInfo.InvariantCulture));
                        if (response.ContentLength > maximumBytes) throw new InvalidDataException("Map response exceeds the configured size limit.");
                        using (Stream input = response.GetResponseStream())
                            return await UpdateValidation.ReadBoundedAsync(input, maximumBytes, timeout.Token).ConfigureAwait(false);
                    }
                  }
                }
            }
            throw new InvalidDataException("Too many map redirects.");
        }
    }

    internal interface IMapCacheStore
    {
        void WriteAtomically(string path, byte[] contents);
    }

    internal sealed class AtomicMapCacheStore : IMapCacheStore
    {
        public void WriteAtomically(string path, byte[] contents)
        {
            string temporary = path + ".tmp." + Guid.NewGuid().ToString("N");
            try {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                    stream.Write(contents, 0, contents.Length);
                    stream.Flush(true);
                }
                if (File.Exists(path)) {
                    string backup = path + ".bak." + Guid.NewGuid().ToString("N");
                    File.Replace(temporary, path, backup);
                    try { File.Delete(backup); } catch { }
                }
                else File.Move(temporary, path);
            } finally {
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
            }
        }
    }

    internal sealed class MapImageService : IDisposable
    {
        private const string CacheSchema = "AETHER-MAP-CACHE/1";
        private const int MaximumCachedMaps = 64;
        private const int MaximumTerritoryMappings = 512;
        private const long MaximumCacheBytes = 256L * 1024 * 1024;
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromDays(180);
        private const int MaximumMapDimension = 4096;
        private const long MaximumMapPixels = 16L * 1024 * 1024;
        private readonly object gate = new object();
        private readonly string cacheDirectory;
        private readonly IMapDataSource dataSource;
        private readonly IMapCacheStore cacheStore;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly HashSet<Task> inFlight = new HashSet<Task>();
        private readonly HashSet<CallbackTicket> pendingCallbacks = new HashSet<CallbackTicket>();
        private CancellationTokenSource activeRequest;
        private int requestSerial;
        private bool stopped;
        private Task stopTask;
        private long cacheWriteBytes;
        private int cacheWriteCount;
        private string lastCacheWriteError;

        internal long CacheWriteBytes { get { lock (gate) return cacheWriteBytes; } }
        internal int CacheWriteCount { get { lock (gate) return cacheWriteCount; } }
        internal string LastCacheWriteError { get { lock (gate) return lastCacheWriteError; } }

        public MapImageService()
            : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Advanced Combat Tracker", "CharacterTracker", "Maps"), new WebMapDataSource(), new AtomicMapCacheStore()) { }

        internal MapImageService(string directory, IMapDataSource source, IMapCacheStore store)
        {
            cacheDirectory = directory ?? throw new ArgumentNullException("directory");
            dataSource = source ?? throw new ArgumentNullException("source");
            cacheStore = store ?? throw new ArgumentNullException("store");
        }

        public Task BeginLoad(int mapId, Action<MapData, string> completed)
        {
            return StartRequest(token => LoadMapAsync(mapId, token), completed);
        }

        public Task BeginLoadForTerritory(uint territoryId, Action<MapData, string> completed)
        {
            return StartRequest(token => LoadTerritoryAsync(territoryId, token), completed);
        }

        private Task StartRequest(Func<CancellationToken, Task<MapData>> loader, Action<MapData, string> completed)
        {
            if (loader == null) throw new ArgumentNullException("loader");
            if (completed == null) throw new ArgumentNullException("completed");
            CancellationTokenSource request;
            int serial;
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            SynchronizationContext context = SynchronizationContext.Current;
            lock (gate) {
                if (stopped) return Task.FromResult(false);
                if (activeRequest != null) activeRequest.Cancel();
                request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                activeRequest = request;
                serial = ++requestSerial;
                inFlight.Add(completion.Task);
            }
            Task requestTask = RunRequestAsync(serial, request, loader, completed, context, completion);
            requestTask.ContinueWith(task => { if (task.IsFaulted) { var ignored = task.Exception; } },
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return completion.Task;
        }

        private async Task RunRequestAsync(int serial, CancellationTokenSource request,
            Func<CancellationToken, Task<MapData>> loader, Action<MapData, string> completed,
            SynchronizationContext context, TaskCompletionSource<bool> completion)
        {
            MapData data = null;
            try {
                data = await loader(request.Token).ConfigureAwait(false);
                request.Token.ThrowIfCancellationRequested();
                DispatchIfCurrent(serial, request.Token, context, completed, data, null);
                data = null; // Ownership transferred to the callback ticket.
            } catch (OperationCanceledException) {
                if (data != null) data.Dispose();
            } catch (Exception ex) {
                if (data != null) data.Dispose();
                DispatchIfCurrent(serial, request.Token, context, completed, null, ex.GetBaseException().Message);
            } finally {
                lock (gate) {
                    inFlight.Remove(completion.Task);
                    if (ReferenceEquals(activeRequest, request)) activeRequest = null;
                }
                request.Dispose();
                completion.TrySetResult(true);
            }
        }

        private void DispatchIfCurrent(int serial, CancellationToken token, SynchronizationContext context,
            Action<MapData, string> completed, MapData data, string error)
        {
            var ticket = new CallbackTicket(data, error, completed);
            lock (gate) {
                if (stopped || token.IsCancellationRequested || serial != requestSerial) {
                    ticket.Cancel();
                    return;
                }
                pendingCallbacks.Add(ticket);
            }
            if (context == null) {
                DeliverTicket(serial, token, ticket);
                return;
            }
            try { context.Post(_ => DeliverTicket(serial, token, ticket), null); }
            catch {
                lock (gate) pendingCallbacks.Remove(ticket);
                ticket.Cancel();
            }
        }

        private void DeliverTicket(int serial, CancellationToken token, CallbackTicket ticket)
        {
            bool deliver;
            lock (gate) {
                pendingCallbacks.Remove(ticket);
                deliver = !stopped && !token.IsCancellationRequested && serial == requestSerial;
            }
            if (deliver) ticket.Deliver(); else ticket.Cancel();
        }

        private async Task<MapData> LoadTerritoryAsync(uint territoryId, CancellationToken token)
        {
            if (territoryId == 0) throw new InvalidDataException(Localization.T("error.noMap"));
            EnsureCacheDirectory(token);
            string mappingPath = Path.Combine(cacheDirectory, "territory-" + territoryId + ".txt");
            int mapId;
            if (!TryReadMapId(mappingPath, out mapId)) {
                string url = "https://v2.xivapi.com/api/sheet/TerritoryType/" + territoryId + "?fields=Map%40as(raw)";
                string json = await dataSource.DownloadStringAsync(url, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                mapId = (int)ReadNumber(json, "Map@as(raw)");
                if (mapId <= 0) throw new InvalidDataException(Localization.T("error.noMap"));
                WriteCacheFile(mappingPath, Encoding.UTF8.GetBytes(mapId.ToString(CultureInfo.InvariantCulture)), token);
                PruneCache(0, 0, token);
            }
            return await LoadMapAsync(mapId, token).ConfigureAwait(false);
        }

        private async Task<MapData> LoadMapAsync(int mapId, CancellationToken token)
        {
            if (mapId <= 0) throw new InvalidDataException(Localization.T("error.noMap"));
            EnsureCacheDirectory(token);
            token.ThrowIfCancellationRequested();
            MapData cached = TryLoadCache(mapId);
            if (cached != null) return cached;
            return await DownloadMapAsync(mapId, token).ConfigureAwait(false);
        }

        private bool TryReadMapId(string path, out int mapId)
        {
            mapId = 0;
            if (!File.Exists(path)) return false;
            if (new FileInfo(path).Length > 64) return false;
            return int.TryParse(File.ReadAllText(path, Encoding.UTF8), NumberStyles.Integer, CultureInfo.InvariantCulture, out mapId) && mapId > 0;
        }

        private MapData TryLoadCache(int mapId)
        {
            try {
                string manifestPath = Path.Combine(cacheDirectory, "map-" + mapId.ToString(CultureInfo.InvariantCulture) + ".cache");
                if (File.Exists(manifestPath)) {
                    var info = new FileInfo(manifestPath);
                    if (info.Length > 8192) return null;
                    string[] lines = File.ReadAllLines(manifestPath, Encoding.UTF8);
                    if (lines.Length != 7 || lines[0] != CacheSchema) return null;
                    string hash = lines[6];
                    string expectedImage = "map-" + mapId.ToString(CultureInfo.InvariantCulture) + "-" + hash + ".png";
                    if (!IsSha256(hash) || !string.Equals(lines[5], expectedImage, StringComparison.Ordinal)) return null;
                    string imagePath = Path.Combine(cacheDirectory, expectedImage);
                    byte[] bytes = ReadImageBytes(imagePath);
                    if (!string.Equals(ComputeSha256(bytes), hash, StringComparison.OrdinalIgnoreCase)) return null;
                    MapData current;
                    if (!TryCreateMapData(mapId, lines[1], lines[2], lines[3], lines[4], bytes, out current)) return null;
                    return current;
                }

                // Legacy four-line mapId.txt/mapId.png entries remain readable as schema 0.
                string legacyImage = Path.Combine(cacheDirectory, mapId.ToString(CultureInfo.InvariantCulture) + ".png");
                string legacyMetadata = Path.Combine(cacheDirectory, mapId.ToString(CultureInfo.InvariantCulture) + ".txt");
                if (!File.Exists(legacyImage) || !File.Exists(legacyMetadata) || new FileInfo(legacyMetadata).Length > 8192) return null;
                string[] legacy = File.ReadAllLines(legacyMetadata, Encoding.UTF8);
                if (legacy.Length < 4) return null;
                return TryCreateMapData(mapId, legacy[0], legacy[1], legacy[2], legacy[3], ReadImageBytes(legacyImage), out MapData legacyData) ? legacyData : null;
            } catch { return null; }
        }

        private async Task<MapData> DownloadMapAsync(int mapId, CancellationToken token)
        {
            string sheetUrl = "https://v2.xivapi.com/api/sheet/Map/" + mapId + "?fields=Id,SizeFactor,OffsetX,OffsetY";
            string json = await dataSource.DownloadStringAsync(sheetUrl, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            string assetId = ReadString(json, "Id");
            if (assetId.Length > 512 || assetId.IndexOf('\r') >= 0 || assetId.IndexOf('\n') >= 0)
                throw new InvalidDataException("Map asset identifier is invalid.");
            string assetUrl = "https://v2.xivapi.com/api/asset/map/" + Uri.EscapeDataString(assetId);
            byte[] imageBytes = await dataSource.DownloadBytesAsync(assetUrl, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (imageBytes == null || imageBytes.Length == 0 || imageBytes.Length > WebMapDataSource.MaximumMapImageBytes)
                throw new InvalidDataException("Map image exceeds the configured size limit.");

            var data = new MapData {
                MapId = mapId, AssetId = assetId,
                SizeFactor = ReadNumber(json, "SizeFactor"), OffsetX = ReadNumber(json, "OffsetX"), OffsetY = ReadNumber(json, "OffsetY"),
                Image = LoadImage(imageBytes)
            };
            string hash = ComputeSha256(imageBytes);
            byte[] metadata = Encoding.UTF8.GetBytes(string.Join(Environment.NewLine, new[] {
                CacheSchema,
                data.AssetId,
                data.SizeFactor.ToString(CultureInfo.InvariantCulture),
                data.OffsetX.ToString(CultureInfo.InvariantCulture),
                data.OffsetY.ToString(CultureInfo.InvariantCulture),
                "map-" + mapId.ToString(CultureInfo.InvariantCulture) + "-" + hash + ".png",
                hash
            }));
            try {
                try { WriteMapCache(mapId, imageBytes, metadata, token); lock (gate) lastCacheWriteError = null; }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { lock (gate) lastCacheWriteError = ex.GetBaseException().Message; /* A full or unavailable cache must not prevent displaying this map. */ }
                return data;
            } catch { data.Dispose(); throw; }
        }

        private void WriteMapCache(int mapId, byte[] imageBytes, byte[] metadata, CancellationToken token)
        {
            string hash = ComputeSha256(imageBytes);
            string imageName = "map-" + mapId.ToString(CultureInfo.InvariantCulture) + "-" + hash + ".png";
            string imagePath = Path.Combine(cacheDirectory, imageName);
            string manifestPath = Path.Combine(cacheDirectory, "map-" + mapId.ToString(CultureInfo.InvariantCulture) + ".cache");
            PruneCache(mapId, imageBytes.Length + metadata.Length, token);
            token.ThrowIfCancellationRequested();
            if (!File.Exists(imagePath)) WriteCacheFile(imagePath, imageBytes, token);
            else if (!string.Equals(ComputeSha256(ReadImageBytes(imagePath)), hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("A map cache image failed its content hash check.");
            token.ThrowIfCancellationRequested();
            // Publish the metadata pointer last. Its atomic replacement keeps the previous
            // image/metadata pair usable if writing the new image or pointer fails.
            WriteCacheFile(manifestPath, metadata, token);
            PruneCache(mapId, 0, token);
        }

        private static bool TryCreateMapData(int mapId, string assetId, string scaleText, string xText, string yText,
            byte[] imageBytes, out MapData data)
        {
            data = null;
            double scale, offsetX, offsetY;
            if (string.IsNullOrWhiteSpace(assetId) || assetId.Length > 512 ||
                !double.TryParse(scaleText, NumberStyles.Float, CultureInfo.InvariantCulture, out scale) ||
                !double.TryParse(xText, NumberStyles.Float, CultureInfo.InvariantCulture, out offsetX) ||
                !double.TryParse(yText, NumberStyles.Float, CultureInfo.InvariantCulture, out offsetY) ||
                double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0 || scale > 1000 ||
                double.IsNaN(offsetX) || double.IsInfinity(offsetX) || Math.Abs(offsetX) > 100000 ||
                double.IsNaN(offsetY) || double.IsInfinity(offsetY) || Math.Abs(offsetY) > 100000) return false;
            data = new MapData { MapId = mapId, AssetId = assetId, SizeFactor = scale, OffsetX = offsetX, OffsetY = offsetY, Image = LoadImage(imageBytes) };
            return true;
        }

        private static byte[] ReadImageBytes(string path)
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length <= 0 || info.Length > WebMapDataSource.MaximumMapImageBytes)
                throw new InvalidDataException("Cached map image is missing or oversized.");
            return File.ReadAllBytes(path);
        }

        private static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64) return false;
            for (int i = 0; i < value.Length; i++)
                if (!((value[i] >= '0' && value[i] <= '9') || (value[i] >= 'a' && value[i] <= 'f') || (value[i] >= 'A' && value[i] <= 'F'))) return false;
            return true;
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
        }

        private void EnsureCacheDirectory(CancellationToken token)
        {
            lock (gate) {
                token.ThrowIfCancellationRequested();
                if (stopped) throw new OperationCanceledException(token);
                if (!Directory.Exists(cacheDirectory)) Directory.CreateDirectory(cacheDirectory);
            }
        }

        private void WriteCacheFile(string path, byte[] data, CancellationToken token)
        {
            lock (gate) {
                token.ThrowIfCancellationRequested();
                if (stopped) throw new OperationCanceledException(token);
                cacheStore.WriteAtomically(path, data);
                cacheWriteBytes += data.LongLength;
                cacheWriteCount++;
            }
        }

        private void PruneCache(int preserveMapId, long reserveBytes, CancellationToken token)
        {
            if (!Directory.Exists(cacheDirectory)) return;
            var files = new DirectoryInfo(cacheDirectory).GetFiles();
            var mapGroups = new Dictionary<int, List<FileInfo>>();
            var mappings = new List<FileInfo>();
            long total = 0;
            foreach (FileInfo file in files) {
                token.ThrowIfCancellationRequested();
                if (file.Name.StartsWith("territory-", StringComparison.Ordinal) && file.Extension == ".txt") { mappings.Add(file); total += file.Length; continue; }
                Match match = Regex.Match(file.Name, "^map-(\\d+)(?:-[0-9A-Fa-f]{64}\\.png|\\.cache)$");
                if (!match.Success) match = Regex.Match(file.Name, "^(\\d+)\\.(?:png|txt)$"); // Bound legacy cache while keeping it readable.
                if (!match.Success) continue;
                int id; if (!int.TryParse(match.Groups[1].Value, out id)) continue;
                List<FileInfo> group; if (!mapGroups.TryGetValue(id, out group)) mapGroups[id] = group = new List<FileInfo>();
                group.Add(file); total += file.Length;
            }
            mappings.Sort((a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
            for (int i = MaximumTerritoryMappings; i < mappings.Count; i++) { token.ThrowIfCancellationRequested(); if (mappings[i].Exists) total -= mappings[i].Length; TryDelete(mappings[i]); }
            var ordered = new List<KeyValuePair<int, List<FileInfo>>>(mapGroups);
            ordered.Sort((a, b) => GroupTime(b.Value).CompareTo(GroupTime(a.Value)));
            int retained = 0;
            foreach (var item in ordered) {
                token.ThrowIfCancellationRequested();
                bool preserve = item.Key == preserveMapId;
                long groupBytes = 0; foreach (FileInfo file in item.Value) groupBytes += file.Exists ? file.Length : 0;
                bool expired = DateTime.UtcNow - GroupTime(item.Value) > CacheLifetime;
                if (!preserve && (expired || retained >= MaximumCachedMaps || total + reserveBytes > MaximumCacheBytes)) {
                    foreach (FileInfo file in item.Value) { if (file.Exists) total -= file.Length; TryDelete(file); }
                } else { retained++; }
            }
        }

        private static DateTime GroupTime(List<FileInfo> files)
        {
            DateTime value = DateTime.MinValue;
            foreach (FileInfo file in files) if (file.Exists && file.LastWriteTimeUtc > value) value = file.LastWriteTimeUtc;
            return value;
        }

        private static void TryDelete(FileInfo file) { try { file.Delete(); } catch { } }

        private static string ReadString(string json, string name)
        {
            var match = Regex.Match(json, "\\\"" + Regex.Escape(name) + "\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"");
            if (!match.Success) throw new InvalidDataException(Localization.F("error.mapField", name));
            return Regex.Unescape(match.Groups[1].Value);
        }

        private static double ReadNumber(string json, string name)
        {
            var match = Regex.Match(json, "\\\"" + Regex.Escape(name) + "\\\"\\s*:\\s*(-?[0-9]+(?:\\.[0-9]+)?)");
            if (!match.Success) throw new InvalidDataException(Localization.F("error.mapField", name));
            return double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        private static Image LoadImage(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 24 || bytes.Length > WebMapDataSource.MaximumMapImageBytes ||
                bytes[0] != 137 || bytes[1] != 80 || bytes[2] != 78 || bytes[3] != 71 || bytes[4] != 13 || bytes[5] != 10 || bytes[6] != 26 || bytes[7] != 10 ||
                bytes[12] != 73 || bytes[13] != 72 || bytes[14] != 68 || bytes[15] != 82) throw new InvalidDataException("Map image is not a valid PNG.");
            uint width = ReadBigEndianUInt32(bytes, 16), height = ReadBigEndianUInt32(bytes, 20);
            if (width == 0 || height == 0 || width > MaximumMapDimension || height > MaximumMapDimension || (long)width * height > MaximumMapPixels)
                throw new InvalidDataException("Map image dimensions exceed the configured limit.");
            using (var stream = new MemoryStream(bytes)) using (var source = Image.FromStream(stream)) {
                if (source.Width != width || source.Height != height) throw new InvalidDataException("Map image dimensions are inconsistent.");
                return new Bitmap(source);
            }
        }

        private static uint ReadBigEndianUInt32(byte[] bytes, int offset)
        { return ((uint)bytes[offset] << 24) | ((uint)bytes[offset + 1] << 16) | ((uint)bytes[offset + 2] << 8) | bytes[offset + 3]; }

        public Task StopAsync()
        {
            lock (gate) {
                if (stopTask != null) return stopTask;
                stopped = true;
                lifetime.Cancel();
                if (activeRequest != null) activeRequest.Cancel();
                foreach (var ticket in pendingCallbacks) ticket.Cancel();
                pendingCallbacks.Clear();
                Task[] pending = new Task[inFlight.Count];
                inFlight.CopyTo(pending);
                stopTask = WaitForRequestsAsync(pending);
                return stopTask;
            }
        }

        private async Task WaitForRequestsAsync(Task[] pending)
        {
            try { await Task.WhenAll(pending).ConfigureAwait(false); }
            finally { lifetime.Dispose(); }
        }

        public void Dispose() { StopAsync().GetAwaiter().GetResult(); }

        private sealed class CallbackTicket
        {
            private MapData data;
            private readonly string error;
            private readonly Action<MapData, string> callback;
            private int completed;

            public CallbackTicket(MapData value, string message, Action<MapData, string> action)
            {
                data = value; error = message; callback = action;
            }

            public void Deliver()
            {
                if (Interlocked.Exchange(ref completed, 1) != 0) return;
                MapData value = Interlocked.Exchange(ref data, null);
                callback(value, error);
            }

            public void Cancel()
            {
                if (Interlocked.Exchange(ref completed, 1) != 0) return;
                MapData value = Interlocked.Exchange(ref data, null);
                if (value != null) value.Dispose();
            }
        }
    }
}
