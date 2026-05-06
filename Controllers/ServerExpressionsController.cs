using System.Text.RegularExpressions;
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
    [Authorize]
    public class ServerExpressionsController : ControllerBase
    {
        private const int MaxExpressionsPerServer = 50;
        private static readonly Regex ExpressionNamePattern = new(
            "^[a-z0-9_]{2,32}$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly ApiContext _context;

        public ServerExpressionsController(ApiContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetExpressionPack(string serverId)
        {
            var username = User.GetUsername();
            if (string.IsNullOrWhiteSpace(username))
                return Unauthorized(new { message = "Missing user identity." });

            serverId = NormalizeOptional(serverId) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(serverId))
                return BadRequest(new { message = "Server is required." });

            if (!await IsServerMember(serverId, username))
                return Forbid();

            var emojis = await _context.ServerEmojis
                .Where(emoji => emoji.ServerId == serverId)
                .OrderBy(emoji => emoji.Name)
                .ToListAsync();
            var stickers = await _context.ServerStickers
                .Where(sticker => sticker.ServerId == serverId)
                .OrderBy(sticker => sticker.Name)
                .ToListAsync();

            return Ok(new
            {
                Emojis = emojis.Select(BuildEmojiResponse),
                Stickers = stickers.Select(BuildStickerResponse),
                CanManage = await CanManageServer(serverId, username)
            });
        }

        [HttpPost]
        [EnableRateLimiting("abuse")]
        public async Task<IActionResult> SaveEmoji([FromBody] ServerExpressionMutationRequest request)
        {
            var result = await SaveExpression(
                request,
                "emoji",
                () => _context.ServerEmojis.CountAsync(emoji => emoji.ServerId == request.ServerId),
                async () => await _context.ServerEmojis.FirstOrDefaultAsync(emoji =>
                    emoji.Id == request.Id && emoji.ServerId == request.ServerId),
                async normalizedName => await _context.ServerEmojis.AnyAsync(emoji =>
                    emoji.ServerId == request.ServerId &&
                    emoji.Name == normalizedName &&
                    emoji.Id != request.Id),
                expression =>
                {
                    var emoji = expression as ServerEmoji ?? new ServerEmoji();
                    emoji.ServerId = request.ServerId;
                    emoji.Name = request.Name;
                    emoji.ImageUrl = request.ImageUrl;
                    return emoji;
                },
                expression => _context.ServerEmojis.Add((ServerEmoji)expression),
                BuildEmojiResponseFromObject);

            return result;
        }

        [HttpPost]
        [EnableRateLimiting("abuse")]
        public async Task<IActionResult> SaveSticker([FromBody] ServerExpressionMutationRequest request)
        {
            var result = await SaveExpression(
                request,
                "sticker",
                () => _context.ServerStickers.CountAsync(sticker => sticker.ServerId == request.ServerId),
                async () => await _context.ServerStickers.FirstOrDefaultAsync(sticker =>
                    sticker.Id == request.Id && sticker.ServerId == request.ServerId),
                async normalizedName => await _context.ServerStickers.AnyAsync(sticker =>
                    sticker.ServerId == request.ServerId &&
                    sticker.Name == normalizedName &&
                    sticker.Id != request.Id),
                expression =>
                {
                    var sticker = expression as ServerSticker ?? new ServerSticker();
                    sticker.ServerId = request.ServerId;
                    sticker.Name = request.Name;
                    sticker.ImageUrl = request.ImageUrl;
                    return sticker;
                },
                expression => _context.ServerStickers.Add((ServerSticker)expression),
                BuildStickerResponseFromObject);

            return result;
        }

        [HttpPost]
        [EnableRateLimiting("abuse")]
        public async Task<IActionResult> DeleteEmoji([FromBody] ServerExpressionDeleteRequest request)
        {
            var username = User.GetUsername();
            if (string.IsNullOrWhiteSpace(username))
                return Unauthorized(new { message = "Missing user identity." });

            request.ServerId = NormalizeOptional(request.ServerId) ?? string.Empty;
            request.Id = NormalizeOptional(request.Id) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(request.ServerId) || string.IsNullOrWhiteSpace(request.Id))
                return BadRequest(new { message = "Server and emoji are required." });

            if (!await CanManageServer(request.ServerId, username))
                return Forbid();

            var emoji = await _context.ServerEmojis.FirstOrDefaultAsync(item =>
                item.ServerId == request.ServerId && item.Id == request.Id);
            if (emoji == null)
                return NotFound(new { message = "Emoji not found." });

            _context.ServerEmojis.Remove(emoji);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Emoji deleted." });
        }

        [HttpPost]
        [EnableRateLimiting("abuse")]
        public async Task<IActionResult> DeleteSticker([FromBody] ServerExpressionDeleteRequest request)
        {
            var username = User.GetUsername();
            if (string.IsNullOrWhiteSpace(username))
                return Unauthorized(new { message = "Missing user identity." });

            request.ServerId = NormalizeOptional(request.ServerId) ?? string.Empty;
            request.Id = NormalizeOptional(request.Id) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(request.ServerId) || string.IsNullOrWhiteSpace(request.Id))
                return BadRequest(new { message = "Server and sticker are required." });

            if (!await CanManageServer(request.ServerId, username))
                return Forbid();

            var sticker = await _context.ServerStickers.FirstOrDefaultAsync(item =>
                item.ServerId == request.ServerId && item.Id == request.Id);
            if (sticker == null)
                return NotFound(new { message = "Sticker not found." });

            _context.ServerStickers.Remove(sticker);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Sticker deleted." });
        }

        private async Task<IActionResult> SaveExpression(
            ServerExpressionMutationRequest request,
            string label,
            Func<Task<int>> countExisting,
            Func<Task<object?>> findExisting,
            Func<string, Task<bool>> nameExists,
            Func<object?, object> applyRequest,
            Action<object> addExpression,
            Func<object, object> buildResponse)
        {
            var username = User.GetUsername();
            if (string.IsNullOrWhiteSpace(username))
                return Unauthorized(new { message = "Missing user identity." });

            request.ServerId = NormalizeOptional(request.ServerId) ?? string.Empty;
            request.Id = NormalizeOptional(request.Id);
            request.Name = NormalizeExpressionName(request.Name);
            request.ImageUrl = NormalizeImageUrl(request.ImageUrl) ?? string.Empty;

            if (string.IsNullOrWhiteSpace(request.ServerId))
                return BadRequest(new { message = "Server is required." });
            if (!ExpressionNamePattern.IsMatch(request.Name))
                return BadRequest(new { message = "Name must be 2-32 lowercase letters, numbers, or underscores." });
            if (string.IsNullOrWhiteSpace(request.ImageUrl))
                return BadRequest(new { message = "Image URL must be an http URL or uploaded file URL." });

            if (!await CanManageServer(request.ServerId, username))
                return Forbid();

            var isUpdate = !string.IsNullOrWhiteSpace(request.Id);
            var existing = isUpdate ? await findExisting() : null;
            if (isUpdate && existing == null)
                return NotFound(new { message = $"{label} not found." });
            if (existing == null && await countExisting() >= MaxExpressionsPerServer)
                return BadRequest(new { message = $"Servers can have up to {MaxExpressionsPerServer} {label}s." });
            if (await nameExists(request.Name))
                return Conflict(new { message = $"A {label} with that name already exists." });

            var expression = applyRequest(existing);
            if (existing == null)
            {
                switch (expression)
                {
                    case ServerEmoji emoji:
                        emoji.Id = Guid.NewGuid().ToString();
                        emoji.CreatedBy = username;
                        emoji.CreatedAt = DateTime.UtcNow;
                        emoji.UpdatedAt = DateTime.UtcNow;
                        break;
                    case ServerSticker sticker:
                        sticker.Id = Guid.NewGuid().ToString();
                        sticker.CreatedBy = username;
                        sticker.CreatedAt = DateTime.UtcNow;
                        sticker.UpdatedAt = DateTime.UtcNow;
                        break;
                }
                addExpression(expression);
            }
            else
            {
                switch (expression)
                {
                    case ServerEmoji emoji:
                        emoji.UpdatedAt = DateTime.UtcNow;
                        break;
                    case ServerSticker sticker:
                        sticker.UpdatedAt = DateTime.UtcNow;
                        break;
                }
            }

            await _context.SaveChangesAsync();
            return Ok(buildResponse(expression));
        }

        private async Task<bool> IsServerMember(string serverId, string username)
        {
            var server = await _context.CreateServers.FirstOrDefaultAsync(s => s.ServerID == serverId);
            if (server == null)
                return false;

            if (string.Equals(server.ServerOwner, username, StringComparison.OrdinalIgnoreCase))
                return true;

            return await _context.ServerMembers.AnyAsync(member =>
                member.ServerId == serverId &&
                member.Username == username);
        }

        private async Task<bool> CanManageServer(string serverId, string username)
        {
            var server = await _context.CreateServers.FirstOrDefaultAsync(s => s.ServerID == serverId);
            if (server == null)
                return false;

            if (string.Equals(server.ServerOwner, username, StringComparison.OrdinalIgnoreCase))
                return true;

            var member = await _context.ServerMembers.FirstOrDefaultAsync(m =>
                m.ServerId == serverId &&
                m.Username == username);
            if (member == null)
                return false;

            var roleName = NormalizeRoleName(member.Role);
            if (roleName == "owner")
                return true;

            var role = await _context.ServerRoles.FirstOrDefaultAsync(serverRole =>
                serverRole.ServerId == serverId &&
                serverRole.Name == roleName);

            return role?.CanManageServer ?? roleName == "admin";
        }

        private static object BuildEmojiResponse(ServerEmoji emoji)
        {
            return new
            {
                emoji.Id,
                emoji.ServerId,
                emoji.Name,
                emoji.ImageUrl,
                emoji.CreatedBy,
                emoji.CreatedAt,
                emoji.UpdatedAt
            };
        }

        private static object BuildStickerResponse(ServerSticker sticker)
        {
            return new
            {
                sticker.Id,
                sticker.ServerId,
                sticker.Name,
                sticker.ImageUrl,
                sticker.CreatedBy,
                sticker.CreatedAt,
                sticker.UpdatedAt
            };
        }

        private static object BuildEmojiResponseFromObject(object emoji)
        {
            return BuildEmojiResponse((ServerEmoji)emoji);
        }

        private static object BuildStickerResponseFromObject(object sticker)
        {
            return BuildStickerResponse((ServerSticker)sticker);
        }

        private static string NormalizeExpressionName(string? value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant();
        }

        private static string NormalizeRoleName(string? value)
        {
            return (value ?? "user").Trim().ToLowerInvariant();
        }

        private static string? NormalizeImageUrl(string? value)
        {
            var normalized = NormalizeOptional(value);
            if (normalized == null || normalized.Length > 2048)
                return null;

            if (normalized.StartsWith("/uploads/", StringComparison.Ordinal))
                return normalized;

            if (Uri.TryCreate(normalized, UriKind.Absolute, out var parsed) &&
                (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
            {
                return normalized;
            }

            return null;
        }

        private static string? NormalizeOptional(string? value)
        {
            var normalized = value?.Trim();
            return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
        }
    }

    public class ServerExpressionMutationRequest
    {
        public string? Id { get; set; }
        public string ServerId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
    }

    public class ServerExpressionDeleteRequest
    {
        public string ServerId { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
    }
}
