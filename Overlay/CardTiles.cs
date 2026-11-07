using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HearthStoner.Overlay
{
    /// <summary>
    /// Card "tile" art (the wide strip of a card's artwork that deck trackers show) from HearthstoneJSON's art
    /// service, downloaded once per card and cached in %LocalAppData%\HearthStoner\tiles. Get() must be called on the
    /// overlay's UI thread; downloads happen in the background and bump <see cref="Version"/> when one finishes,
    /// so the overlay knows to redraw.
    /// </summary>
    public static class CardTiles
    {
        private const string TileUrl = "https://art.hearthstonejson.com/v1/tiles/{0}.png";
        private const string RenderUrl = "https://art.hearthstonejson.com/v1/render/latest/enUS/256x/{0}.png";
        private static readonly Dictionary<string, ImageSource> Loaded = new Dictionary<string, ImageSource>();
        private static readonly HashSet<string> Pending = new HashSet<string>();
        private static readonly HashSet<string> Failed = new HashSet<string>();
        private static readonly object Lock = new object();
        private static readonly SemaphoreSlim Throttle = new SemaphoreSlim(4);
        private static int _version;

        /// <summary>Increases every time a new tile becomes available.</summary>
        public static int Version => _version;

        private static string Dir
        {
            get
            {
                var d = AppPaths.Data("tiles");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        // Keys: "tile:CARD_ID" or "render:CARD_ID".
        private static string FileFor(string key)
        {
            var parts = key.Split(new[] { ':' }, 2);
            var dir = parts[0] == "render" ? Path.Combine(Dir, "render") : Dir;
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, parts[1] + ".png");
        }

        private static string UrlFor(string key)
        {
            var parts = key.Split(new[] { ':' }, 2);
            return string.Format(parts[0] == "render" ? RenderUrl : TileUrl, parts[1]);
        }

        /// <summary>The tile image, or null if it isn't downloaded yet (a download is started).</summary>
        public static ImageSource Get(string cardId) => string.IsNullOrEmpty(cardId) ? null : Load("tile:" + cardId);

        /// <summary>The full card picture (for hover tooltips), or null if not downloaded yet.</summary>
        public static ImageSource GetRender(string cardId) => string.IsNullOrEmpty(cardId) ? null : Load("render:" + cardId);

        private static ImageSource Load(string cardId)
        {
            if (Loaded.TryGetValue(cardId, out var img)) return img;

            var file = FileFor(cardId);
            if (File.Exists(file))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;   // read now so the file isn't kept open
                    bmp.UriSource = new Uri(file);
                    bmp.EndInit();
                    bmp.Freeze();
                    Loaded[cardId] = bmp;
                    return bmp;
                }
                catch
                {
                    try { File.Delete(file); } catch (Exception swallowed) { Diag.Ignored(swallowed, "CardTiles"); }       // corrupt download: fetch it again
                }
            }

            lock (Lock)
            {
                if (Failed.Contains(cardId) || !Pending.Add(cardId)) return null;
            }
            Task.Run(() => Download(cardId));
            return null;
        }

        private static async Task Download(string cardId)
        {
            await Throttle.WaitAsync().ConfigureAwait(false);
            try
            {
                var bytes = await Web.GetBytesAsync(UrlFor(cardId), 30).ConfigureAwait(false);
                var tmp = FileFor(cardId) + ".tmp";
                File.WriteAllBytes(tmp, bytes);
                if (File.Exists(FileFor(cardId))) File.Delete(FileFor(cardId));
                File.Move(tmp, FileFor(cardId));
                Interlocked.Increment(ref _version);
            }
            catch
            {
                lock (Lock) Failed.Add(cardId);   // e.g. no tile for this card; don't retry every redraw
            }
            finally
            {
                lock (Lock) Pending.Remove(cardId);
                Throttle.Release();
            }
        }
    }
}
