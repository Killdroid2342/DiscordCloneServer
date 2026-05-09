using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;

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
            @"<meta\s+[^>]*(?:property|name)=[""'](?<key>og:title|og:description|og:image|og:site_name|og:type|og:video|og:video:url|og:audio|twitter:title|twitter:description|twitter:image|twitter:player|description|theme-color)[""'][^>]*content=[""'](?<value>[^""']*)[""'][^>]*>|<meta\s+[^>]*content=[""'](?<value>[^""']*)[""'][^>]*(?:property|name)=[""'](?<key>og:title|og:description|og:image|og:site_name|og:type|og:video|og:video:url|og:audio|twitter:title|twitter:description|twitter:image|twitter:player|description|theme-color)[""'][^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex IconLinkRegex = new(
            @"<link\s+[^>]*rel=[""'][^""']*(?:icon|shortcut icon|apple-touch-icon)[^""']*[""'][^>]*href=[""'](?<value>[^""']+)[""'][^>]*>|<link\s+[^>]*href=[""'](?<value>[^""']+)[""'][^>]*rel=[""'][^""']*(?:icon|shortcut icon|apple-touch-icon)[^""']*[""'][^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMemoryCache? _cache;
        private readonly TimeSpan _cacheDuration;

        public LinkPreviewController(
            IHttpClientFactory httpClientFactory,
            IMemoryCache? cache = null,
            IConfiguration? configuration = null)
        {
            _httpClientFactory = httpClientFactory;
            _cache = cache;
            _cacheDuration = TimeSpan.FromMinutes(Math.Clamp(
                configuration?.GetValue<int?>("Caching:LinkPreviewMinutes") ?? 30,
                0,
                24 * 60));
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

            var cacheKey = $"link-preview:{uri.AbsoluteUri}";
            if (_cacheDuration > TimeSpan.Zero &&
                _cache != null &&
                _cache.TryGetValue<LinkPreviewResponse>(cacheKey, out var cachedPreview))
            {
                Response.Headers["X-Cache"] = "HIT";
                return Ok(cachedPreview);
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
                    var directPreviewType = GetPreviewTypeForMedia(mediaType);
                    return Ok(CachePreview(cacheKey, new LinkPreviewResponse
                    {
                        url = uri.ToString(),
                        siteName = uri.Host,
                        title = uri.Host,
                        description = mediaType,
                        image = directPreviewType == "image" ? uri.ToString() : null,
                        type = directPreviewType,
                        mediaUrl = uri.ToString(),
                        mediaContentType = mediaType,
                        accentColor = null,
                        icon = null
                    }));
                }

                await using var stream = await response.Content.ReadAsStreamAsync(HttpContext.RequestAborted);
                using var reader = new StreamReader(stream);
                var buffer = new char[262_144];
                var read = await reader.ReadBlockAsync(buffer, HttpContext.RequestAborted);
                var html = new string(buffer, 0, read);

                var meta = ExtractMeta(html);
                var title = meta.GetValueOrDefault("og:title") ??
                            meta.GetValueOrDefault("twitter:title") ??
                            Decode(TitleRegex.Match(html).Groups["value"].Value) ??
                            uri.Host;
                var description = meta.GetValueOrDefault("og:description") ??
                                  meta.GetValueOrDefault("twitter:description") ??
                                  meta.GetValueOrDefault("description") ??
                                  string.Empty;
                var image = ResolvePreviewUrl(
                    uri,
                    meta.GetValueOrDefault("og:image") ?? meta.GetValueOrDefault("twitter:image"));
                var mediaUrl = ResolvePreviewUrl(
                    uri,
                    meta.GetValueOrDefault("og:video") ??
                    meta.GetValueOrDefault("og:video:url") ??
                    meta.GetValueOrDefault("twitter:player") ??
                    meta.GetValueOrDefault("og:audio"));
                var icon = ResolvePreviewUrl(uri, Decode(IconLinkRegex.Match(html).Groups["value"].Value));
                var previewType = GetPreviewType(meta.GetValueOrDefault("og:type"), mediaUrl);

                return Ok(CachePreview(cacheKey, new LinkPreviewResponse
                {
                    url = uri.ToString(),
                    siteName = meta.GetValueOrDefault("og:site_name") ?? uri.Host,
                    title = title,
                    description = description,
                    image = image,
                    type = previewType,
                    mediaUrl = mediaUrl,
                    mediaContentType = null,
                    accentColor = NormalizeColor(meta.GetValueOrDefault("theme-color")),
                    icon = icon
                }));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"link preview failed: {ex.Message}");
                return StatusCode(502, new { message = "Could not fetch link preview." });
            }
        }

        private LinkPreviewResponse CachePreview(string cacheKey, LinkPreviewResponse preview)
        {
            if (_cacheDuration <= TimeSpan.Zero || _cache == null)
            {
                return preview;
            }

            _cache.Set(
                cacheKey,
                preview,
                new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = _cacheDuration
                });
            Response.Headers["X-Cache"] = "MISS";
            return preview;
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

            if (!Uri.TryCreate(pageUri, value, out var resolved))
            {
                return null;
            }

            return resolved.Scheme == Uri.UriSchemeHttp || resolved.Scheme == Uri.UriSchemeHttps
                ? resolved.ToString()
                : null;
        }

        private static string GetPreviewTypeForMedia(string? mediaType)
        {
            if (string.IsNullOrWhiteSpace(mediaType))
            {
                return "file";
            }

            if (mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                return "image";
            }

            if (mediaType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            {
                return "video";
            }

            if (mediaType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
            {
                return "audio";
            }

            return "file";
        }

        private static string GetPreviewType(string? ogType, string? mediaUrl)
        {
            var normalized = ogType?.Trim().ToLowerInvariant() ?? string.Empty;
            if (normalized.Contains("video") || LooksLikeMediaUrl(mediaUrl, "video"))
            {
                return "video";
            }

            if (normalized.Contains("audio") || LooksLikeMediaUrl(mediaUrl, "audio"))
            {
                return "audio";
            }

            if (normalized.Contains("image"))
            {
                return "image";
            }

            return "article";
        }

        private static bool LooksLikeMediaUrl(string? value, string kind)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var extensionPattern = kind == "video"
                ? @"\.(mp4|webm|mov)(?:[?#].*)?$"
                : @"\.(mp3|wav|ogg|m4a)(?:[?#].*)?$";
            return Regex.IsMatch(value, extensionPattern, RegexOptions.IgnoreCase);
        }

        private static string? NormalizeColor(string? value)
        {
            var color = value?.Trim();
            if (string.IsNullOrWhiteSpace(color))
            {
                return null;
            }

            if (Regex.IsMatch(color, "^#?[0-9a-fA-F]{6}$"))
            {
                return color.StartsWith('#') ? color : $"#{color}";
            }

            return null;
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

        private sealed class LinkPreviewResponse
        {
            public string url { get; init; } = string.Empty;
            public string siteName { get; init; } = string.Empty;
            public string title { get; init; } = string.Empty;
            public string description { get; init; } = string.Empty;
            public string? image { get; init; }
            public string type { get; init; } = "article";
            public string? mediaUrl { get; init; }
            public string? mediaContentType { get; init; }
            public string? accentColor { get; init; }
            public string? icon { get; init; }
        }
    }
}
