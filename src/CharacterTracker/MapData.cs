using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

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
            // FFXIV Map sheet: SizeFactorは百分率、Offsetはワールド座標原点補正。
            double scale = SizeFactor / 100.0;
            double px = ((worldX + OffsetX) * scale + 1024.0) / 2048.0 * Image.Width;
            double py = ((worldY + OffsetY) * scale + 1024.0) / 2048.0 * Image.Height;
            return new PointF((float)px, (float)py);
        }

        public void Dispose() { if (Image != null) { Image.Dispose(); Image = null; } }
    }

    internal sealed class MapImageService
    {
        private readonly string cacheDirectory;
        private int requestSerial;

        public MapImageService()
        {
            cacheDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Advanced Combat Tracker", "CharacterTracker", "Maps");
        }

        public async void BeginLoad(int mapId, Action<MapData, string> completed)
        {
            int serial = Interlocked.Increment(ref requestSerial);
            var context = SynchronizationContext.Current;
            try {
                Directory.CreateDirectory(cacheDirectory);
                MapData data = TryLoadCache(mapId);
                if (data == null) data = await Download(mapId);
                if (serial != requestSerial) { data.Dispose(); return; }
                if (context != null) context.Post(_ => completed(data, null), null); else completed(data, null);
            } catch (Exception ex) {
                string message = ex.GetBaseException().Message;
                if (serial != requestSerial) return;
                if (context != null) context.Post(_ => completed(null, message), null); else completed(null, message);
            }
        }

        public async void BeginLoadForTerritory(uint territoryId, Action<MapData, string> completed)
        {
            int serial = Interlocked.Increment(ref requestSerial);
            var context = SynchronizationContext.Current;
            try {
                int mapId;
                string mappingPath = Path.Combine(cacheDirectory, "territory-" + territoryId + ".txt");
                Directory.CreateDirectory(cacheDirectory);
                if (File.Exists(mappingPath) && int.TryParse(File.ReadAllText(mappingPath, Encoding.UTF8), out mapId) && mapId > 0) {
                    // キャッシュ済み
                } else {
                    string url = "https://v2.xivapi.com/api/sheet/TerritoryType/" + territoryId + "?fields=Map%40as(raw)";
                    string json;
                    using (var client = new WebClient()) {
                        client.Headers[HttpRequestHeader.UserAgent] = "Aether-Tracker-ACT/1.5";
                        client.Encoding = Encoding.UTF8;
                        json = await client.DownloadStringTaskAsync(url);
                    }
                    mapId = (int)ReadNumber(json, "Map@as(raw)");
                    if (mapId <= 0) throw new InvalidDataException(Localization.T("error.noMap"));
                    File.WriteAllText(mappingPath, mapId.ToString(CultureInfo.InvariantCulture), Encoding.UTF8);
                }
                MapData data = TryLoadCache(mapId);
                if (data == null) data = await Download(mapId);
                if (serial != requestSerial) { data.Dispose(); return; }
                if (context != null) context.Post(_ => completed(data, null), null); else completed(data, null);
            } catch (Exception ex) {
                string message = ex.GetBaseException().Message;
                if (serial != requestSerial) return;
                if (context != null) context.Post(_ => completed(null, message), null); else completed(null, message);
            }
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

        private async System.Threading.Tasks.Task<MapData> Download(int mapId)
        {
            string sheetUrl = "https://v2.xivapi.com/api/sheet/Map/" + mapId + "?fields=Id,SizeFactor,OffsetX,OffsetY";
            string json;
            byte[] imageBytes;
            using (var client = new WebClient()) {
                client.Headers[HttpRequestHeader.UserAgent] = "Aether-Tracker-ACT/1.5";
                client.Encoding = Encoding.UTF8;
                json = await client.DownloadStringTaskAsync(sheetUrl);
                string assetId = ReadString(json, "Id");
                string assetUrl = "https://v2.xivapi.com/api/asset/map/" + assetId;
                imageBytes = await client.DownloadDataTaskAsync(assetUrl);
            }
            var data = new MapData {
                MapId = mapId, AssetId = ReadString(json, "Id"),
                SizeFactor = ReadNumber(json, "SizeFactor"), OffsetX = ReadNumber(json, "OffsetX"), OffsetY = ReadNumber(json, "OffsetY")
            };
            string imagePath = Path.Combine(cacheDirectory, mapId + ".png");
            string metadataPath = Path.Combine(cacheDirectory, mapId + ".txt");
            File.WriteAllBytes(imagePath, imageBytes);
            File.WriteAllLines(metadataPath, new[] { data.AssetId, data.SizeFactor.ToString(CultureInfo.InvariantCulture),
                data.OffsetX.ToString(CultureInfo.InvariantCulture), data.OffsetY.ToString(CultureInfo.InvariantCulture) }, Encoding.UTF8);
            data.Image = LoadUnlocked(imagePath);
            return data;
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

        private static Image LoadUnlocked(string path)
        {
            using (var stream = File.OpenRead(path)) using (var source = Image.FromStream(stream)) return new Bitmap(source);
        }
    }
}
