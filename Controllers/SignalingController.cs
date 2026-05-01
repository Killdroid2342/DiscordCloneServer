using System.Collections.Concurrent;
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
    public class SignalingController : ControllerBase
    {
        private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentQueue<SignalMessage>>> ServerMessages = new();
        private readonly ApiContext _context;

        public SignalingController(ApiContext context)
        {
            _context = context;
        }

        [HttpPost]
        [EnableRateLimiting("abuse")]
        public async Task<IActionResult> SendMessage([FromBody] SignalMessage message, string serverId, string toUser, string? channelId = null)
        {
            var username = User.GetUsername();
            if (string.IsNullOrWhiteSpace(username))
                return Unauthorized(new { message = "Missing user identity." });
            if (!await CanJoinVoice(serverId, username, channelId) || !await CanJoinVoice(serverId, toUser, channelId))
                return Forbid();

            message.From = username;
            var server = ServerMessages.GetOrAdd(serverId, _ => new ConcurrentDictionary<string, ConcurrentQueue<SignalMessage>>());
            var queue = server.GetOrAdd(toUser, _ => new ConcurrentQueue<SignalMessage>());
            queue.Enqueue(message);
            return Ok();
        }

        [HttpGet]
        public async Task<IActionResult> ReceiveMessages(string serverId, string? channelId = null)
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
                return Unauthorized(new { message = "Missing user identity." });
            if (!await CanJoinVoice(serverId, currentUsername, channelId))
                return Forbid();

            if (ServerMessages.TryGetValue(serverId, out var users) &&
                users.TryGetValue(currentUsername, out var queue))
            {
                var messages = queue.ToArray();
                queue.Clear();
                return Ok(messages);
            }

            return Ok(new List<SignalMessage>());
        }

        [HttpPost]
        public async Task<IActionResult> JoinVoice(string serverId, string? channelId = null)
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
                return Unauthorized(new { message = "Missing user identity." });
            if (!await CanJoinVoice(serverId, currentUsername, channelId))
                return Forbid();

            return Ok(VoiceWebSocketController.GetActiveUsersForServer(serverId));
        }

        [HttpPost]
        public async Task<IActionResult> LeaveVoice(string serverId)
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
                return Unauthorized(new { message = "Missing user identity." });
            if (!await CanJoinVoice(serverId, currentUsername))
                return Forbid();

            return Ok();
        }

        [HttpGet]
        public async Task<IActionResult> GetActiveUsers(string serverId, string? channelId = null)
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
                return Unauthorized(new { message = "Missing user identity." });
            if (!await CanJoinVoice(serverId, currentUsername, channelId))
                return Forbid();

            return Ok(VoiceWebSocketController.GetActiveUsersForServer(serverId));
        }

        private async Task<bool> IsServerMember(string serverId, string username)
        {
            return await _context.ServerMembers.AnyAsync(member =>
                member.ServerId == serverId && member.Username == username);
        }

        private async Task<bool> CanJoinVoice(string serverId, string username, string? channelId = null)
        {
            var server = await _context.CreateServers.FirstOrDefaultAsync(s => s.ServerID == serverId);
            if (server == null)
            {
                return false;
            }

            var channel = string.IsNullOrWhiteSpace(channelId)
                ? null
                : await _context.Channels.FirstOrDefaultAsync(channel =>
                    channel.Id == channelId && channel.ServerId == serverId);
            if (!string.IsNullOrWhiteSpace(channelId) &&
                (channel == null || !IsVoiceLikeChannelType(channel.Type)))
            {
                return false;
            }

            if (server.ServerOwner == username)
            {
                return true;
            }

            var member = await _context.ServerMembers.FirstOrDefaultAsync(m =>
                m.ServerId == serverId && m.Username == username);
            if (member == null)
            {
                return false;
            }

            if (IsMemberTimedOut(member, DateTime.UtcNow))
            {
                return false;
            }

            var roleName = member.Role?.Trim().ToLowerInvariant() ?? "user";
            if (roleName is "owner" or "admin" or "moderator")
            {
                return true;
            }

            var role = await _context.ServerRoles.FirstOrDefaultAsync(r => r.ServerId == serverId && r.Name == roleName);
            if (role?.CanJoinVoice == false)
            {
                return false;
            }

            var account = await _context.Accounts.FirstOrDefaultAsync(a => a.UserName == username && !a.IsDisabled);
            if (!ServerVerificationPolicy.Evaluate(server.VerificationLevel, account, member).Allowed)
            {
                return false;
            }

            if (channel is { VoiceAccessRestricted: true })
            {
                var allowedRoles = DeserializeRoleNames(channel.VoiceAllowedRolesJson);
                return allowedRoles.Contains(roleName, StringComparer.OrdinalIgnoreCase);
            }

            return true;
        }

        private static bool IsMemberTimedOut(ServerMember member, DateTime now)
        {
            return member.TimedOutUntil is { } timedOutUntil && timedOutUntil > now;
        }

        private static bool IsVoiceLikeChannelType(string? value)
        {
            var type = value?.Trim().ToLowerInvariant();
            return type is "voice" or "stage";
        }

        private static string[] DeserializeRoleNames(string? rolesJson)
        {
            if (string.IsNullOrWhiteSpace(rolesJson))
            {
                return Array.Empty<string>();
            }

            try
            {
                return (System.Text.Json.JsonSerializer.Deserialize<string[]>(rolesJson) ?? Array.Empty<string>())
                    .Select(role => role.Trim().ToLowerInvariant().Replace(' ', '-'))
                    .Where(role => role.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (System.Text.Json.JsonException)
            {
                return Array.Empty<string>();
            }
        }
    }
}
