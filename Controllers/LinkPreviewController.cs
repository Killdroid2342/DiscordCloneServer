using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DiscordCloneServer.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    [Authorize]
    public class LinkPreviewController : ControllerBase
    {
        private static readonly Regex TitleRegex = new(
            @"<title[^>]*>(?<value>.*?)</title>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex MetaRegex = new(
            @"<meta\s+[^>]*(?:property|name)=[""'](?<key>og:title|og:description|og:image|description)[""'][^>]*content=[""'](?<value>[^""']*)[""'][^>]*>|<meta\s+[^>]*content=[""'](?<value>[^""']*)[""'][^>]*(?:property|name)=[""'](?<key>og:title|og:description|og:image|description)[""'][^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        private readonly IHttpClientFactory _httpClientFactory;

        public LinkPreviewController(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        [HttpGet]
        [EnableRateLimiting("abuse")]
        public async Task<IActionResult> Get(string url)
        {
            if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return BadRequest(new { message = "URL must be an http or https URL." });
            }

            if (!await IsPublicHttpUrl(uri))
            {
                return BadRequest(new { message = "URL is not available for previews." });
            }

            try
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(5);

                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.UserAgent.ParseAdd("MyDiscord-LinkPreview/1.0");
                using var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    HttpContext.RequestAborted);
                if (!response.IsSuccessStatusCode)
                {
                    return StatusCode((int)response.StatusCode, new { message = "Could not fetch link preview." });
                }

                var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                if (!mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
                {
                    return Ok(new
                    {
                        url = uri.ToString(),
                        siteName = uri.Host,
                        title = uri.Host,
                        description = mediaType,
                        image = (string?)null
                    });
                }

                await using var stream = await response.Content.ReadAsStreamAsync(HttpContext.RequestAborted);
                using var reader = new StreamReader(stream);
                var buffer = new char[262_144];
                var read = await reader.ReadBlockAsync(buffer, HttpContext.RequestAborted);
                var html = new string(buffer, 0, read);

                var meta = ExtractMeta(html);
                var title = meta.GetValueOrDefault("og:title") ??
                            Decode(TitleRegex.Match(html).Groups["value"].Value) ??
                            uri.Host;
                var description = meta.GetValueOrDefault("og:description") ??
                                  meta.GetValueOrDefault("description") ??
                                  string.Empty;
                var image = ResolvePreviewUrl(uri, meta.GetValueOrDefault("og:image"));

                return Ok(new
                {
                    url = uri.ToString(),
                    siteName = uri.Host,
                    title,
                    description,
                    image
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"link preview failed: {ex.Message}");
                return StatusCode(502, new { message = "Could not fetch link preview." });
            }
        }

        private static Dictionary<string, string> ExtractMeta(string html)
        {
            var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in MetaRegex.Matches(html))
            {
                var key = match.Groups["key"].Value;
                var value = Decode(match.Groups["value"].Value);
                if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
                {
                    meta[key] = value;
                }
            }

            return meta;
        }

        private static string? Decode(string value)
        {
            var decoded = WebUtility.HtmlDecode(value)?.Trim();
            if (string.IsNullOrWhiteSpace(decoded))
            {
                return null;
            }

            return decoded.Length > 500 ? decoded[..500] : decoded;
        }

        private static string? ResolvePreviewUrl(Uri pageUri, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return Uri.TryCreate(pageUri, value, out var resolved) ? resolved.ToString() : null;
        }

        private static async Task<bool> IsPublicHttpUrl(Uri uri)
        {
            if (uri.IsLoopback || uri.HostNameType == UriHostNameType.Unknown)
            {
                return false;
            }

            IPAddress[] addresses;
            try
            {
                addresses = await Dns.GetHostAddressesAsync(uri.Host);
            }
            catch
            {
                return false;
            }

            return addresses.Length > 0 && addresses.All(IsPublicAddress);
        }

        private static bool IsPublicAddress(IPAddress address)
        {
            if (IPAddress.IsLoopback(address))
            {
                return false;
            }

            var bytes = address.GetAddressBytes();
            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                return bytes[0] switch
                {
                    10 => false,
                    127 => false,
                    169 when bytes[1] == 254 => false,
                    172 when bytes[1] >= 16 && bytes[1] <= 31 => false,
                    192 when bytes[1] == 168 => false,
                    _ => true
                };
            }

            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                return !address.IsIPv6LinkLocal &&
                       !address.IsIPv6SiteLocal &&
                       !address.IsIPv6Multicast;
            }

            return false;
        }
    }
}
