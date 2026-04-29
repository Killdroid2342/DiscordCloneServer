using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DiscordCloneServer.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    [Authorize]
    public class VoiceConfigController : ControllerBase
    {
        private readonly IConfiguration _config;

        public VoiceConfigController(IConfiguration config)
        {
            _config = config;
        }

        [HttpGet]
        public IActionResult GetIceServers()
        {
            var configured = LoadIceServers();

            return Ok(new
            {
                iceServers = configured
                    .Where(server => !string.IsNullOrWhiteSpace(server.Urls))
                    .Select(server => new
                    {
                        urls = server.Urls,
                        username = string.IsNullOrWhiteSpace(server.Username) ? null : server.Username,
                        credential = string.IsNullOrWhiteSpace(server.Credential) ? null : server.Credential
                    })
            });
        }

        [HttpGet]
        public IActionResult GetDiagnostics()
        {
            var configured = LoadIceServers();
            var activeTurnServers = configured
                .Where(server => IsTurnServer(server.Urls))
                .Select(server => new
                {
                    urls = server.Urls,
                    hasUsername = !string.IsNullOrWhiteSpace(server.Username),
                    hasCredential = !string.IsNullOrWhiteSpace(server.Credential)
                })
                .ToArray();

            return Ok(new
            {
                generatedAt = DateTime.UtcNow,
                iceServerCount = configured.Count,
                stunServerCount = configured.Count(server => IsStunServer(server.Urls)),
                turnServerCount = activeTurnServers.Length,
                turnCredentialReady = activeTurnServers.Any(server => server.hasUsername && server.hasCredential),
                turnServers = activeTurnServers,
                websocketPath = "/voice-ws"
            });
        }

        private List<IceServerConfig> LoadIceServers()
        {
            var configured = _config
                .GetSection("WebRtc:IceServers")
                .Get<List<IceServerConfig>>() ?? new List<IceServerConfig>();

            var validServers = configured
                .Where(server => !string.IsNullOrWhiteSpace(server.Urls))
                .ToList();

            if (validServers.Count == 0)
            {
                validServers.Add(new IceServerConfig
                {
                    Urls = "stun:stun.l.google.com:19302"
                });
            }

            return validServers;
        }

        private static bool IsTurnServer(string? urls)
        {
            var value = urls?.Trim() ?? string.Empty;
            return value.StartsWith("turn:", StringComparison.OrdinalIgnoreCase) ||
                   value.StartsWith("turns:", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsStunServer(string? urls)
        {
            return (urls?.Trim() ?? string.Empty).StartsWith("stun:", StringComparison.OrdinalIgnoreCase);
        }

        private sealed class IceServerConfig
        {
            public string Urls { get; set; } = string.Empty;
            public string? Username { get; set; }
            public string? Credential { get; set; }
        }
    }
}
