using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
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
            cancellationToken.ThrowIfCancellationRequested();
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)) {
                timeout.CancelAfter(RequestTimeoutMilliseconds);
                using (var client = new WebClient())
                using (timeout.Token.Register(() => { try { client.CancelAsync(); } catch { } })) {
                    client.Headers[HttpRequestHeader.UserAgent] = "Aether-Tracker-ACT/1.6";
                    client.Encoding = Encoding.UTF8;
                    try {
                        return await client.DownloadStringTaskAsync(new Uri(url)).ConfigureAwait(false);
                    } catch (WebException) when (timeout.IsCancellationRequested) {
                        throw new OperationCanceledException(timeout.Token);
                    }
                }
            }
        }

        private static async Task<byte[]> DownloadBytesCoreAsync(string url, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)) {
                timeout.CancelAfter(RequestTimeoutMilliseconds);
                using (var client = new WebClient())
                using (timeout.Token.Register(() => { try { client.CancelAsync(); } catch { } })) {
                    client.Headers[HttpRequestHeader.UserAgent] = "Aether-Tracker-ACT/1.6";
                    try { return await client.DownloadDataTaskAsync(new Uri(url)).ConfigureAwait(false); }
                    catch (WebException) when (timeout.IsCancellationRequested) { throw new OperationCanceledException(timeout.Token); }
                }
            }
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
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            } finally {
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
            }
        }
    }

    internal sealed class MapImageService : IDisposable
    {
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
            return int.TryParse(File.ReadAllText(path, Encoding.UTF8), NumberStyles.Integer, CultureInfo.InvariantCulture, out mapId) && mapId > 0;
        }

        private MapData TryLoadCache(int mapId)
        {
            string imagePath = Path.Combine(cacheDirectory, mapId + ".png");
            string metadataPath = Path.Combine(cacheDirectory, mapId + ".txt");
            if (!File.Exists(imagePath) || !File.Exists(metadataPath)) return null;
            string[] lines = File.ReadAllLines(metadataPath, Encoding.UTF8);
            if (lines.Length < 4) return null;
            return new MapData {
                MapId = mapId, AssetId = lines[0],
                SizeFactor = double.Parse(lines[1], CultureInfo.InvariantCulture),
                OffsetX = double.Parse(lines[2], CultureInfo.InvariantCulture),
                OffsetY = double.Parse(lines[3], CultureInfo.InvariantCulture),
                Image = LoadUnlocked(imagePath)
            };
        }

        private async Task<MapData> DownloadMapAsync(int mapId, CancellationToken token)
        {
            string sheetUrl = "https://v2.xivapi.com/api/sheet/Map/" + mapId + "?fields=Id,SizeFactor,OffsetX,OffsetY";
            string json = await dataSource.DownloadStringAsync(sheetUrl, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            string assetId = ReadString(json, "Id");
            string assetUrl = "https://v2.xivapi.com/api/asset/map/" + Uri.EscapeDataString(assetId);
            byte[] imageBytes = await dataSource.DownloadBytesAsync(assetUrl, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();

            var data = new MapData {
                MapId = mapId, AssetId = assetId,
                SizeFactor = ReadNumber(json, "SizeFactor"), OffsetX = ReadNumber(json, "OffsetX"), OffsetY = ReadNumber(json, "OffsetY"),
                Image = LoadImage(imageBytes)
            };
            string imagePath = Path.Combine(cacheDirectory, mapId + ".png");
            string metadataPath = Path.Combine(cacheDirectory, mapId + ".txt");
            byte[] metadata = Encoding.UTF8.GetBytes(string.Join(Environment.NewLine, new[] {
                data.AssetId,
                data.SizeFactor.ToString(CultureInfo.InvariantCulture),
                data.OffsetX.ToString(CultureInfo.InvariantCulture),
                data.OffsetY.ToString(CultureInfo.InvariantCulture)
            }));
            try {
                WriteCacheFile(imagePath, imageBytes, token);
                WriteCacheFile(metadataPath, metadata, token);
                return data;
            } catch { data.Dispose(); throw; }
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
            }
        }

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
            using (var stream = new MemoryStream(bytes)) using (var source = Image.FromStream(stream)) return new Bitmap(source);
        }

        private static Image LoadUnlocked(string path)
        {
            using (var stream = File.OpenRead(path)) using (var source = Image.FromStream(stream)) return new Bitmap(source);
        }

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
