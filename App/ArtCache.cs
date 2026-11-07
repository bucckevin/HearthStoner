using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HearthStoner
{
    /// <summary>
    /// Card artwork (the painting, not the card frame) from HearthstoneJSON, 512 px, cached in
    /// %LocalAppData%\HearthStoner\art. The images come with a white border, which is trimmed off when loaded.
    /// Get() returns null while a download runs; <see cref="Arrived"/> fires (on a worker thread) when one finishes.
    /// </summary>
    public static class ArtCache
    {
        private const string Url = "https://art.hearthstonejson.com/v1/512x/{0}.jpg";
        private static readonly Dictionary<string, ImageSource> Loaded = new Dictionary<string, ImageSource>();
        private static readonly HashSet<string> Pending = new HashSet<string>();
        private static readonly HashSet<string> Failed = new HashSet<string>();
        private static readonly object Lock = new object();

        /// <summary>True if the art for this card couldn't be downloaded (e.g. it doesn't exist for a hero skin).</summary>
        public static bool HasFailed(string cardId) { lock (Lock) return cardId != null && Failed.Contains(cardId); }

        public static event Action Arrived;

        public static string Dir
        {
            get
            {
                var d = AppPaths.Data("art");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        /// <summary>The trimmed artwork, or null if it isn't downloaded yet (a download is started).</summary>
        public static ImageSource Get(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return null;
            lock (Lock) if (Loaded.TryGetValue(cardId, out var img)) return img;
            var file = Path.Combine(Dir, cardId + ".jpg");
            if (File.Exists(file))
            {
                try
                {
                    var img = Trim(file);
                    lock (Lock) Loaded[cardId] = img;
                    return img;
                }
                catch { try { File.Delete(file); } catch { } }
            }
            lock (Lock) { if (Failed.Contains(cardId) || !Pending.Add(cardId)) return null; }
            Task.Run(() => Download(cardId, file));
            return null;
        }

        /// <summary>Downloads the art for these cards and waits (up to the timeout) for them; for rendering offscreen.</summary>
        public static void Prefetch(IEnumerable<string> cardIds, TimeSpan timeout)
        {
            var until = DateTime.UtcNow + timeout;
            var ids = new List<string>(cardIds);
            foreach (var id in ids) Get(id);
            while (DateTime.UtcNow < until)
            {
                lock (Lock) if (Pending.Count == 0) break;
                Thread.Sleep(200);
            }
        }

        private static async Task Download(string cardId, string file)
        {
            try
            {
                var bytes = await Web.GetBytesAsync(string.Format(Url, cardId), 30).ConfigureAwait(false);
                File.WriteAllBytes(file + ".tmp", bytes);
                if (File.Exists(file)) File.Delete(file);
                File.Move(file + ".tmp", file);
            }
            catch { lock (Lock) Failed.Add(cardId); }
            finally { lock (Lock) Pending.Remove(cardId); }
            Arrived?.Invoke();
        }

        /// <summary>Loads the image and cuts away the white border around the painting.</summary>
        private static ImageSource Trim(string file)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(file);
            bmp.EndInit();
            bmp.Freeze();
            var conv = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
            int w = conv.PixelWidth, h = conv.PixelHeight;
            var px = new byte[w * h * 4];
            conv.CopyPixels(px, w * 4, 0);
            bool White(int x, int y) { var i = (y * w + x) * 4; return px[i] + px[i + 1] + px[i + 2] > 640; }   // some borders are light gray (233)
            int left = 0, right = w - 1, top = 0, bottom = h - 1;
            while (left < w / 3 && White(left, h / 2)) left++;
            while (right > w * 2 / 3 && White(right, h / 2)) right--;
            while (top < h / 3 && White(w / 2, top)) top++;
            while (bottom > h * 2 / 3 && White(w / 2, bottom)) bottom--;
            const int pad = 6;   // the border fades in over a few pixels
            var rect = new Int32Rect(left + pad, top + pad, Math.Max(1, right - left - 2 * pad), Math.Max(1, bottom - top - 2 * pad));
            var cropped = new CroppedBitmap(bmp, rect);
            cropped.Freeze();
            return cropped;
        }
    }
}
