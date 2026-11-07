#nullable enable
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace HearthStoner
{
    /// <summary>
    /// The one HttpClient for all downloads (card data, meta stats, art, update checks). One shared client reuses
    /// connections; each call gets its own timeout. Headers are set once here, before any request.
    /// </summary>
    public static class Web
    {
        private static readonly HttpClient Client = Make();

        private static HttpClient Make()
        {
            var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate };
            var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("HearthStoner/1.0 (personal Hearthstone tracker)");
            return http;
        }

        public static async Task<byte[]> GetBytesAsync(string url, int timeoutSeconds)
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds)))
                return await Client.GetByteArrayAsync(url, cts.Token).ConfigureAwait(false);
        }

        public static byte[] GetBytes(string url, int timeoutSeconds) => GetBytesAsync(url, timeoutSeconds).GetAwaiter().GetResult();

        public static string GetString(string url, int timeoutSeconds)
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds)))
                return Client.GetStringAsync(url, cts.Token).GetAwaiter().GetResult();
        }
    }
}
