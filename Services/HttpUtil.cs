using System.Net;
using System.Net.Http;

namespace TiviIPTVPlayer.Services
{
    /// <summary>Shared HTTP client factory for playlist / EPG downloads.</summary>
    public static class HttpUtil
    {
        public const string UserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36 TiviIPTVPlayer/1.0";

        public static HttpClient CreateClient(int timeoutSeconds = 90)
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10,
            };
            var client = new HttpClient(handler)
            {
                Timeout = System.TimeSpan.FromSeconds(timeoutSeconds),
            };
            client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
            return client;
        }
    }
}
