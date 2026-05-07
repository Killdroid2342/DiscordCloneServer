using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DiscordCloneServer.Data;
using DiscordCloneServer.Models;
using DiscordCloneServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace DiscordCloneServer.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class OAuthAppsController : ControllerBase
    {
        private const int MaxNameLength = 80;
        private const int MaxDescriptionLength = 240;
        private static readonly string[] DefaultScopes = ["identify"];
        private static readonly HashSet<string> KnownScopes = new(StringComparer.OrdinalIgnoreCase)
        {
            "identify",
            "servers.read",
            "messages.read",
            "slash.commands",
            "bot",
            "webhooks.manage"
        };
        private static readonly HashSet<string> ServerAuthorizationScopes = new(StringComparer.OrdinalIgnoreCase)
        {
            "bot",
            "slash.commands",
            "webhooks.manage"
        };

        private readonly ApiContext _context;

        public OAuthAppsController(ApiContext context)
        {
            _context = context;
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> GetOAuthApplications()
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
                return Unauthorized(new { Message = "Missing user identity." });

            var apps = await _context.OAuthApplications
                .Where(application => application.OwnerUsername == currentUsername)
                .OrderBy(application => application.Name)
                .ToListAsync();

            return Ok(apps.Select(application => BuildApplicationResponse(application)));
        }

        [Authorize]
        [HttpPost]
        [EnableRateLimiting("abuse")]
        public async Task<IActionResult> CreateOAuthApplication([FromBody] OAuthApplicationMutationRequest request)
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
                return Unauthorized(new { Message = "Missing user identity." });

            var name = NormalizeName(request.Name);
            if (name == null)
                return BadRequest(new { Message = "Application name must be 1-80 characters." });

            var redirectUris = NormalizeRedirectUris(request.RedirectUris);
            if (redirectUris.Length == 0)
                return BadRequest(new { Message = "At least one http or https redirect URI is required." });

            if (!TryNormalizeScopes(request.AllowedScopes, out var scopes))
                return BadRequest(new { Message = "Application scopes include an unsupported value." });
            if (scopes.Length == 0)
                scopes = DefaultScopes;

            var iconUrl = NormalizeOptional(request.IconUrl);
            if (!IsValidMediaUrl(iconUrl))
                return BadRequest(new { Message = "Icon must be blank, an http URL, or an uploaded file URL." });

            var secret = GenerateToken();
            var now = DateTime.UtcNow;
            var application = new OAuthApplication
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                Description = NormalizeDescription(request.Description),
                IconUrl = iconUrl,
                OwnerUsername = currentUsername,
                ClientSecretHash = HashToken(secret),
                RedirectUrisJson = SerializeList(redirectUris),
                AllowedScopesJson = SerializeList(scopes),
                BotAccountId = NormalizeOptional(request.BotAccountId),
                CreatedAt = now,
                SecretLastRotatedAt = now,
                IsEnabled = request.IsEnabled ?? true
            };

            _context.OAuthApplications.Add(application);
            await _context.SaveChangesAsync();
            return Ok(BuildApplicationResponse(application, secret));
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> UpdateOAuthApplication([FromBody] OAuthApplicationMutationRequest request)
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
                return Unauthorized(new { Message = "Missing user identity." });

            var application = await _context.OAuthApplications.FirstOrDefaultAsync(item =>
                item.Id == request.ApplicationId && item.OwnerUsername == currentUsername);
            if (application == null)
                return NotFound(new { Message = "OAuth application not found." });

            var name = NormalizeName(request.Name);
            if (name == null)
                return BadRequest(new { Message = "Application name must be 1-80 characters." });

            var redirectUris = NormalizeRedirectUris(request.RedirectUris);
            if (redirectUris.Length == 0)
                return BadRequest(new { Message = "At least one http or https redirect URI is required." });

            if (!TryNormalizeScopes(request.AllowedScopes, out var scopes))
                return BadRequest(new { Message = "Application scopes include an unsupported value." });
            if (scopes.Length == 0)
                scopes = DefaultScopes;

            var iconUrl = NormalizeOptional(request.IconUrl);
            if (!IsValidMediaUrl(iconUrl))
                return BadRequest(new { Message = "Icon must be blank, an http URL, or an uploaded file URL." });

            application.Name = name;
            application.Description = NormalizeDescription(request.Description);
            application.IconUrl = iconUrl;
            application.RedirectUrisJson = SerializeList(redirectUris);
            application.AllowedScopesJson = SerializeList(scopes);
            application.BotAccountId = NormalizeOptional(request.BotAccountId);
            application.IsEnabled = request.IsEnabled ?? application.IsEnabled;
            application.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return Ok(BuildApplicationResponse(application));
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> RotateOAuthClientSecret([FromBody] OAuthAppActionRequest request)
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
                return Unauthorized(new { Message = "Missing user identity." });

            var application = await _context.OAuthApplications.FirstOrDefaultAsync(item =>
                item.Id == request.ApplicationId && item.OwnerUsername == currentUsername);
            if (application == null)
                return NotFound(new { Message = "OAuth application not found." });

            var secret = GenerateToken();
            application.ClientSecretHash = HashToken(secret);
            application.SecretLastRotatedAt = DateTime.UtcNow;
            application.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return Ok(BuildApplicationResponse(application, secret));
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> DeleteOAuthApplication([FromBody] OAuthAppActionRequest request)
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
                return Unauthorized(new { Message = "Missing user identity." });

            var application = await _context.OAuthApplications.FirstOrDefaultAsync(item =>
                item.Id == request.ApplicationId && item.OwnerUsername == currentUsername);
            if (application == null)
                return NotFound(new { Message = "OAuth application not found." });

            var authorizations = _context.OAuthAppAuthorizations.Where(item => item.ApplicationId == application.Id);
            var codes = _context.OAuthAuthorizationCodes.Where(item => item.ApplicationId == application.Id);
            var tokens = _context.OAuthAccessTokens.Where(item => item.ApplicationId == application.Id);
            _context.OAuthAppAuthorizations.RemoveRange(authorizations);
            _context.OAuthAuthorizationCodes.RemoveRange(codes);
            _context.OAuthAccessTokens.RemoveRange(tokens);
            _context.OAuthApplications.Remove(application);
            await _context.SaveChangesAsync();

            return Ok(new { Message = "OAuth application deleted." });
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> GetAuthorizationPreview(
            string clientId,
            string redirectUri,
            string scope = "",
            string? serverId = null)
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
                return Unauthorized(new { Message = "Missing user identity." });

            var application = await _context.OAuthApplications.FirstOrDefaultAsync(item => item.Id == clientId);
            if (application == null || !application.IsEnabled)
                return NotFound(new { Message = "OAuth application not found." });

            var requestedScopes = SplitScopes(scope);
            var validation = await ValidateAuthorizationRequest(
                application,
                redirectUri,
                requestedScopes,
                serverId,
                currentUsername);
            if (validation.Result != null)
                return validation.Result;

            return Ok(new
            {
                Application = BuildApplicationResponse(application),
                RequestedScopes = validation.Scopes,
                ServerId = NormalizeOptional(serverId),
                RequiresServer = RequiresServerAuthorization(validation.Scopes)
            });
        }

        [Authorize]
        [HttpPost]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> AuthorizeApp([FromBody] OAuthAuthorizeRequest request)
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
                return Unauthorized(new { Message = "Missing user identity." });

            var application = await _context.OAuthApplications.FirstOrDefaultAsync(item => item.Id == request.ClientId);
            if (application == null || !application.IsEnabled)
                return NotFound(new { Message = "OAuth application not found." });

            var requestedScopes = MergeScopes(request.Scopes, request.Scope);
            var validation = await ValidateAuthorizationRequest(
                application,
                request.RedirectUri,
                requestedScopes,
                request.ServerId,
                currentUsername);
            if (validation.Result != null)
                return validation.Result;

            var now = DateTime.UtcNow;
            var serverId = NormalizeOptional(request.ServerId);
            var authorization = await _context.OAuthAppAuthorizations.FirstOrDefaultAsync(item =>
                item.ApplicationId == application.Id &&
                item.Username == currentUsername &&
                item.ServerId == serverId &&
                item.RevokedAt == null);
            if (authorization == null)
            {
                authorization = new OAuthAppAuthorization
                {
                    Id = Guid.NewGuid().ToString(),
                    ApplicationId = application.Id,
                    Username = currentUsername,
                    ServerId = serverId,
                    CreatedAt = now
                };
                _context.OAuthAppAuthorizations.Add(authorization);
            }

            authorization.ScopesJson = SerializeList(validation.Scopes);
            authorization.LastUsedAt = now;

            var codeValue = GenerateToken();
            var code = new OAuthAuthorizationCode
            {
                Id = Guid.NewGuid().ToString(),
                ApplicationId = application.Id,
                AuthorizationId = authorization.Id,
                Username = currentUsername,
                ServerId = serverId,
                CodeHash = HashToken(codeValue),
                RedirectUri = request.RedirectUri.Trim(),
                ScopesJson = SerializeList(validation.Scopes),
                CreatedAt = now,
                ExpiresAt = now.AddMinutes(10)
            };
            _context.OAuthAuthorizationCodes.Add(code);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                Code = codeValue,
                RedirectUrl = BuildRedirectUrl(request.RedirectUri, codeValue, request.State),
                State = request.State,
                ExpiresAt = code.ExpiresAt,
                ExpiresIn = 600,
                Authorization = BuildAuthorizationResponse(authorization, application),
                Scopes = validation.Scopes
            });
        }

        [AllowAnonymous]
        [HttpPost]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> ExchangeCode([FromBody] OAuthTokenExchangeRequest request)
        {
            var application = await _context.OAuthApplications.FirstOrDefaultAsync(item =>
                item.Id == request.ClientId && item.IsEnabled);
            if (application == null || application.ClientSecretHash != HashToken(request.ClientSecret ?? string.Empty))
                return Unauthorized(new { Message = "Client credentials are invalid." });

            var codeHash = HashToken(request.Code ?? string.Empty);
            var code = await _context.OAuthAuthorizationCodes.FirstOrDefaultAsync(item =>
                item.ApplicationId == application.Id && item.CodeHash == codeHash);
            if (code == null ||
                code.ConsumedAt != null ||
                code.ExpiresAt <= DateTime.UtcNow ||
                !string.Equals(code.RedirectUri, request.RedirectUri?.Trim(), StringComparison.Ordinal))
            {
                return BadRequest(new { Message = "Authorization code is invalid or expired." });
            }

            var tokenValue = GenerateToken();
            var now = DateTime.UtcNow;
            var token = new OAuthAccessToken
            {
                Id = Guid.NewGuid().ToString(),
                ApplicationId = application.Id,
                AuthorizationId = code.AuthorizationId,
                Username = code.Username,
                ServerId = code.ServerId,
                TokenHash = HashToken(tokenValue),
                ScopesJson = code.ScopesJson,
                CreatedAt = now,
                ExpiresAt = now.AddHours(1)
            };
            code.ConsumedAt = now;
            _context.OAuthAccessTokens.Add(token);
            await _context.SaveChangesAsync();

            var scopes = DeserializeList(token.ScopesJson);
            return Ok(new
            {
                AccessToken = tokenValue,
                TokenType = "Bearer",
                ExpiresIn = 3600,
                ExpiresAt = token.ExpiresAt,
                Scope = string.Join(' ', scopes),
                Scopes = scopes
            });
        }

        [AllowAnonymous]
        [HttpPost]
        public async Task<IActionResult> IntrospectToken([FromBody] OAuthIntrospectionRequest request)
        {
            var tokenValue = GetProvidedBearerToken() ?? NormalizeOptional(request.Token);
            if (string.IsNullOrWhiteSpace(tokenValue))
                return Ok(new { Active = false });

            var tokenHash = HashToken(tokenValue);
            var token = await _context.OAuthAccessTokens.FirstOrDefaultAsync(item => item.TokenHash == tokenHash);
            if (token == null || token.RevokedAt != null || token.ExpiresAt <= DateTime.UtcNow)
                return Ok(new { Active = false });

            var application = await _context.OAuthApplications.FirstOrDefaultAsync(item => item.Id == token.ApplicationId);
            if (application == null || !application.IsEnabled)
                return Ok(new { Active = false });

            var authorization = await _context.OAuthAppAuthorizations.FirstOrDefaultAsync(item =>
                item.Id == token.AuthorizationId && item.RevokedAt == null);
            if (authorization == null)
                return Ok(new { Active = false });

            authorization.LastUsedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            var scopes = DeserializeList(token.ScopesJson);
            return Ok(new
            {
                Active = true,
                ClientId = token.ApplicationId,
                ApplicationName = application.Name,
                token.Username,
                token.ServerId,
                Scopes = scopes,
                Scope = string.Join(' ', scopes),
                token.ExpiresAt
            });
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> GetAuthorizedApps()
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
                return Unauthorized(new { Message = "Missing user identity." });

            var authorizations = await _context.OAuthAppAuthorizations
                .Where(authorization => authorization.Username == currentUsername && authorization.RevokedAt == null)
                .OrderByDescending(authorization => authorization.CreatedAt)
                .ToListAsync();
            var appIds = authorizations.Select(authorization => authorization.ApplicationId).Distinct().ToList();
            var applications = await _context.OAuthApplications
                .Where(application => appIds.Contains(application.Id))
                .ToDictionaryAsync(application => application.Id);

            return Ok(authorizations.Select(authorization =>
            {
                applications.TryGetValue(authorization.ApplicationId, out var application);
                return BuildAuthorizationResponse(authorization, application);
            }));
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> RevokeAuthorization([FromBody] OAuthAuthorizationActionRequest request)
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
                return Unauthorized(new { Message = "Missing user identity." });

            var authorization = await _context.OAuthAppAuthorizations.FirstOrDefaultAsync(item =>
                item.Id == request.AuthorizationId && item.Username == currentUsername && item.RevokedAt == null);
            if (authorization == null)
                return NotFound(new { Message = "OAuth authorization not found." });

            var now = DateTime.UtcNow;
            authorization.RevokedAt = now;
            var tokens = await _context.OAuthAccessTokens
                .Where(token => token.AuthorizationId == authorization.Id && token.RevokedAt == null)
                .ToListAsync();
            foreach (var token in tokens)
            {
                token.RevokedAt = now;
            }

            await _context.SaveChangesAsync();
            return Ok(new { Message = "OAuth authorization revoked." });
        }

        private async Task<(IActionResult? Result, string[] Scopes)> ValidateAuthorizationRequest(
            OAuthApplication application,
            string? redirectUri,
            IEnumerable<string> requestedScopes,
            string? serverId,
            string currentUsername)
        {
            var normalizedRedirectUri = redirectUri?.Trim() ?? string.Empty;
            if (!DeserializeList(application.RedirectUrisJson).Contains(normalizedRedirectUri, StringComparer.Ordinal))
            {
                return (BadRequest(new { Message = "Redirect URI is not registered for this application." }), Array.Empty<string>());
            }

            if (!TryNormalizeScopes(requestedScopes, out var scopes))
            {
                return (BadRequest(new { Message = "Requested scopes include an unsupported value." }), Array.Empty<string>());
            }
            if (scopes.Length == 0)
            {
                scopes = DefaultScopes;
            }

            var allowedScopes = DeserializeList(application.AllowedScopesJson);
            if (scopes.Any(scope => !allowedScopes.Contains(scope, StringComparer.OrdinalIgnoreCase)))
            {
                return (BadRequest(new { Message = "Requested scopes are not enabled for this application." }), Array.Empty<string>());
            }

            var normalizedServerId = NormalizeOptional(serverId);
            if (RequiresServerAuthorization(scopes))
            {
                if (normalizedServerId == null)
                {
                    return (BadRequest(new { Message = "Server authorization requires a server id." }), Array.Empty<string>());
                }

                if (!await CanManageServer(normalizedServerId, currentUsername))
                {
                    return (Forbid(), Array.Empty<string>());
                }
            }
            else if (normalizedServerId != null && !await IsServerMember(normalizedServerId, currentUsername))
            {
                return (Forbid(), Array.Empty<string>());
            }

            return (null, scopes);
        }

        private object BuildApplicationResponse(OAuthApplication application, string? clientSecret = null)
        {
            return new
            {
                application.Id,
                ClientId = application.Id,
                ClientSecret = clientSecret,
                application.Name,
                application.Description,
                application.IconUrl,
                application.OwnerUsername,
                RedirectUris = DeserializeList(application.RedirectUrisJson),
                AllowedScopes = DeserializeList(application.AllowedScopesJson),
                application.BotAccountId,
                application.CreatedAt,
                application.UpdatedAt,
                application.SecretLastRotatedAt,
                application.IsEnabled,
                AuthorizationUrl = BuildAuthorizationUrl(application)
            };
        }

        private static object BuildAuthorizationResponse(
            OAuthAppAuthorization authorization,
            OAuthApplication? application)
        {
            return new
            {
                authorization.Id,
                authorization.ApplicationId,
                ApplicationName = application?.Name,
                ApplicationIconUrl = application?.IconUrl,
                authorization.Username,
                authorization.ServerId,
                Scopes = DeserializeList(authorization.ScopesJson),
                authorization.CreatedAt,
                authorization.LastUsedAt,
                authorization.RevokedAt
            };
        }

        private string BuildAuthorizationUrl(OAuthApplication application)
        {
            var request = HttpContext?.Request;
            var baseUrl = request == null || !request.Host.HasValue
                ? string.Empty
                : $"{request.Scheme}://{request.Host}";
            return $"{baseUrl}/api/OAuthApps/GetAuthorizationPreview?clientId={Uri.EscapeDataString(application.Id)}";
        }

        private static string BuildRedirectUrl(string redirectUri, string code, string? state)
        {
            var separator = redirectUri.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            var url = $"{redirectUri}{separator}code={Uri.EscapeDataString(code)}";
            return string.IsNullOrWhiteSpace(state)
                ? url
                : $"{url}&state={Uri.EscapeDataString(state)}";
        }

        private async Task<bool> CanManageServer(string serverId, string username)
        {
            var server = await _context.CreateServers.FirstOrDefaultAsync(item => item.ServerID == serverId);
            if (server == null)
            {
                return false;
            }

            if (server.ServerOwner == username)
            {
                return true;
            }

            var member = await _context.ServerMembers.FirstOrDefaultAsync(item =>
                item.ServerId == serverId && item.Username == username);
            if (member == null)
            {
                return false;
            }

            var roleName = NormalizeRoleName(member.Role);
            var role = await _context.ServerRoles.FirstOrDefaultAsync(item =>
                item.ServerId == serverId && item.Name == roleName);
            role ??= BuildImplicitRole(serverId, roleName);
            if (role?.CanManageServer != true)
            {
                return false;
            }

            if (server.RequireTwoFactorForModerators && IsElevatedModeratorRole(role))
            {
                var account = await _context.Accounts.FirstOrDefaultAsync(account =>
                    account.UserName == username && !account.IsDisabled);
                return account?.TwoFactorEnabled == true;
            }

            return true;
        }

        private async Task<bool> IsServerMember(string serverId, string username)
        {
            var server = await _context.CreateServers.FirstOrDefaultAsync(item => item.ServerID == serverId);
            if (server == null)
            {
                return false;
            }

            return server.ServerOwner == username ||
                   await _context.ServerMembers.AnyAsync(member =>
                       member.ServerId == serverId && member.Username == username);
        }

        private static bool RequiresServerAuthorization(IEnumerable<string> scopes)
        {
            return scopes.Any(scope => ServerAuthorizationScopes.Contains(scope));
        }

        private static bool TryNormalizeScopes(IEnumerable<string>? scopes, out string[] normalizedScopes)
        {
            normalizedScopes = scopes?
                .SelectMany(SplitScopes)
                .Select(scope => scope.Trim().ToLowerInvariant())
                .Where(scope => !string.IsNullOrWhiteSpace(scope))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? Array.Empty<string>();

            return normalizedScopes.All(scope => KnownScopes.Contains(scope));
        }

        private static string[] MergeScopes(IEnumerable<string>? scopes, string? scope)
        {
            return (scopes ?? Array.Empty<string>())
                .Concat(SplitScopes(scope))
                .ToArray();
        }

        private static string[] SplitScopes(string? value)
        {
            return (value ?? string.Empty)
                .Split(new[] { ' ', ',', ';', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        private static string[] NormalizeRedirectUris(IEnumerable<string>? redirectUris)
        {
            return redirectUris?
                .Select(uri => uri.Trim())
                .Where(IsValidRedirectUri)
                .Distinct(StringComparer.Ordinal)
                .Take(20)
                .ToArray() ?? Array.Empty<string>();
        }

        private static bool IsValidRedirectUri(string uri)
        {
            return Uri.TryCreate(uri, UriKind.Absolute, out var parsed) &&
                   (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);
        }

        private static string SerializeList(IEnumerable<string> values)
        {
            return JsonSerializer.Serialize(values.ToArray());
        }

        private static string[] DeserializeList(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return Array.Empty<string>();
            }

            try
            {
                return JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>();
            }
            catch (JsonException)
            {
                return Array.Empty<string>();
            }
        }

        private static string? NormalizeName(string? value)
        {
            var normalized = System.Text.RegularExpressions.Regex
                .Replace(value?.Trim() ?? string.Empty, @"\s+", " ");
            return normalized.Length is > 0 and <= MaxNameLength ? normalized : null;
        }

        private static string? NormalizeDescription(string? value)
        {
            var normalized = System.Text.RegularExpressions.Regex
                .Replace(value?.Trim() ?? string.Empty, @"\s+", " ");
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            return normalized.Length <= MaxDescriptionLength ? normalized : normalized[..MaxDescriptionLength];
        }

        private static string? NormalizeOptional(string? value)
        {
            var normalized = value?.Trim();
            return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
        }

        private static bool IsValidMediaUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return true;
            }

            if (url.StartsWith("/uploads/", StringComparison.Ordinal))
            {
                return true;
            }

            return Uri.TryCreate(url, UriKind.Absolute, out var parsedUrl) &&
                   (parsedUrl.Scheme == Uri.UriSchemeHttp || parsedUrl.Scheme == Uri.UriSchemeHttps);
        }

        private static string GenerateToken()
        {
            var bytes = RandomNumberGenerator.GetBytes(32);
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static string HashToken(string token)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        private string? GetProvidedBearerToken()
        {
            var authorization = Request?.Headers.Authorization.ToString();
            if (!string.IsNullOrWhiteSpace(authorization) &&
                authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return NormalizeOptional(authorization["Bearer ".Length..]);
            }

            return null;
        }

        private static string NormalizeRoleName(string? value)
        {
            var normalized = (value ?? "user").Trim().ToLowerInvariant().Replace(' ', '-');
            return string.IsNullOrWhiteSpace(normalized) ? "user" : normalized;
        }

        private static ServerRole? BuildImplicitRole(string serverId, string roleName)
        {
            return roleName switch
            {
                "owner" => new ServerRole
                {
                    ServerId = serverId,
                    Name = "owner",
                    CanManageServer = true,
                    CanManageChannels = true,
                    CanManageMembers = true,
                    CanBanMembers = true,
                    CanCreateInvites = true,
                    CanSendMessages = true,
                    CanJoinVoice = true
                },
                "admin" => new ServerRole
                {
                    ServerId = serverId,
                    Name = "admin",
                    CanManageServer = true,
                    CanManageChannels = true,
                    CanManageMembers = true,
                    CanBanMembers = true,
                    CanCreateInvites = true,
                    CanSendMessages = true,
                    CanJoinVoice = true
                },
                "moderator" => new ServerRole
                {
                    ServerId = serverId,
                    Name = "moderator",
                    CanManageChannels = true,
                    CanManageMembers = true,
                    CanBanMembers = true,
                    CanCreateInvites = true,
                    CanSendMessages = true,
                    CanJoinVoice = true
                },
                "user" => new ServerRole
                {
                    ServerId = serverId,
                    Name = "user",
                    CanCreateInvites = true,
                    CanSendMessages = true,
                    CanJoinVoice = true
                },
                _ => null
            };
        }

        private static bool IsElevatedModeratorRole(ServerRole role)
        {
            var roleName = NormalizeRoleName(role.Name);
            return roleName is "owner" or "admin" or "moderator" ||
                   role.CanManageServer ||
                   role.CanManageChannels ||
                   role.CanManageMembers ||
                   role.CanBanMembers;
        }
    }

    public class OAuthApplicationMutationRequest
    {
        public string? ApplicationId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? IconUrl { get; set; }
        public string[] RedirectUris { get; set; } = Array.Empty<string>();
        public string[] AllowedScopes { get; set; } = Array.Empty<string>();
        public string? BotAccountId { get; set; }
        public bool? IsEnabled { get; set; }
    }

    public class OAuthAppActionRequest
    {
        public string ApplicationId { get; set; } = string.Empty;
    }

    public class OAuthAuthorizeRequest
    {
        public string ClientId { get; set; } = string.Empty;
        public string RedirectUri { get; set; } = string.Empty;
        public string[] Scopes { get; set; } = Array.Empty<string>();
        public string? Scope { get; set; }
        public string? State { get; set; }
        public string? ServerId { get; set; }
    }

    public class OAuthTokenExchangeRequest
    {
        public string ClientId { get; set; } = string.Empty;
        public string? ClientSecret { get; set; }
        public string? Code { get; set; }
        public string? RedirectUri { get; set; }
    }

    public class OAuthIntrospectionRequest
    {
        public string? Token { get; set; }
    }

    public class OAuthAuthorizationActionRequest
    {
        public string AuthorizationId { get; set; } = string.Empty;
    }
}
