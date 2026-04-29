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
    public class ServerMessagesController : ControllerBase
    {
        private readonly ApiContext _context;
        private readonly IConfiguration _config;

        public ServerMessagesController(ApiContext context, IConfiguration config)
        {
            _context = context;
            _config = config;
        }

        [HttpPost]
        [EnableRateLimiting("abuse")]
        public async Task<IActionResult> ServerMessages(Models.ServerMessage serverMessage)
        {
            var username = User.GetUsername();
            if (string.IsNullOrWhiteSpace(username))
                return Unauthorized(new { message = "Missing user identity." });

            serverMessage.ChannelId = serverMessage.ChannelId?.Trim() ?? string.Empty;
            serverMessage.userText = serverMessage.userText?.Trim() ?? string.Empty;
            serverMessage.AttachmentUrl = NormalizeOptional(serverMessage.AttachmentUrl);
            serverMessage.AttachmentContentType = NormalizeOptional(serverMessage.AttachmentContentType);
            serverMessage.ReplyToMessageId = NormalizeOptional(serverMessage.ReplyToMessageId);

            if (string.IsNullOrWhiteSpace(serverMessage.ChannelId))
                return BadRequest(new { message = "Channel is required." });
            if (!IsValidMessageBody(serverMessage.userText, serverMessage.AttachmentUrl))
                return BadRequest(new { message = "Message must be 1-4000 characters or include an attachment." });
            if (!IsValidAttachment(serverMessage.AttachmentUrl))
                return BadRequest(new { message = "Attachment must be blank, an http URL, or an uploaded file URL." });

            var channel = await _context.Channels.FirstOrDefaultAsync(c => c.Id == serverMessage.ChannelId);
            if (channel == null)
                return NotFound(new { message = "Channel not found." });

            if (channel.Type != "text")
                return BadRequest(new { message = "Messages can only be sent to text channels." });

            if (!await CanSendMessages(channel.ServerId, username))
                return Forbid();

            var verification = await GetServerVerificationResult(channel.ServerId, username);
            if (!verification.Allowed)
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    message = verification.Message,
                    verificationLevel = verification.Level
                });

            if (!string.IsNullOrWhiteSpace(serverMessage.ReplyToMessageId) &&
                !await _context.ServerMessages.AnyAsync(message =>
                    message.MessageID == serverMessage.ReplyToMessageId &&
                    message.ChannelId == serverMessage.ChannelId))
                return BadRequest(new { message = "Reply target was not found in this channel." });

            serverMessage.MessageID = string.IsNullOrWhiteSpace(serverMessage.MessageID)
                ? Guid.NewGuid().ToString()
                : serverMessage.MessageID.Trim();
            serverMessage.MessagesUserSender = username;
            serverMessage.Date = DateTime.UtcNow.ToString("O");

            if (await _context.ServerMessages.AnyAsync(message => message.MessageID == serverMessage.MessageID))
                return Conflict(new { message = "Duplicate message id." });

            _context.ServerMessages.Add(serverMessage);
            await _context.SaveChangesAsync();
            return Ok(BuildMessageResponse(serverMessage, Array.Empty<MessageReaction>()));
        }

        [HttpGet]
        public async Task<IActionResult> GetServerMessages(string channelId, int take = 50, string? beforeMessageId = null)
        {
            try
            {
                var username = User.GetUsername();
                if (string.IsNullOrWhiteSpace(username))
                    return Unauthorized(new { message = "Missing user identity." });

                var channel = await _context.Channels.FirstOrDefaultAsync(c => c.Id == channelId);
                if (channel == null)
                    return NotFound(new { message = "Channel not found." });

                if (!await IsServerMember(channel.ServerId, username))
                    return Forbid();

                take = Math.Clamp(take, 1, 100);

                var messages = await _context.ServerMessages
                    .Where(msg => msg.ChannelId == channelId)
                    .ToListAsync();

                var orderedMessages = messages
                    .OrderBy(msg =>
                    {
                        if (DateTime.TryParse(msg.Date, out var dt)) return dt;
                        return DateTime.MinValue;
                    })
                    .ToList();

                if (!string.IsNullOrWhiteSpace(beforeMessageId))
                {
                    var beforeIndex = orderedMessages.FindIndex(message => message.MessageID == beforeMessageId);
                    if (beforeIndex >= 0)
                    {
                        orderedMessages = orderedMessages.Take(beforeIndex).ToList();
                    }
                }

                var page = orderedMessages.TakeLast(take).ToList();
                var messageIds = page.Select(message => message.MessageID).ToList();
                var reactions = await _context.MessageReactions
                    .Where(reaction => reaction.ScopeType == "server" && messageIds.Contains(reaction.MessageId))
                    .ToListAsync();

                return Ok(page.Select(message => BuildMessageResponse(
                    message,
                    reactions.Where(reaction => reaction.MessageId == message.MessageID))));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"get server messages failed: {ex}");
                return StatusCode(500, new { message = "Internal server error." });
            }
        }

        [HttpPost]
        public async Task<IActionResult> EditMessage([FromBody] EditServerMessageRequest request)
        {
            var username = User.GetUsername();
            if (string.IsNullOrWhiteSpace(username))
                return Unauthorized(new { message = "Missing user identity." });

            var message = await _context.ServerMessages.FirstOrDefaultAsync(m => m.MessageID == request.MessageId);
            if (message == null)
                return NotFound(new { message = "Message not found." });
            if (message.MessagesUserSender != username)
                return Forbid();

            var nextText = request.UserText?.Trim() ?? string.Empty;
            var nextAttachmentUrl = NormalizeOptional(request.AttachmentUrl);
            if (!IsValidMessageBody(nextText, nextAttachmentUrl))
                return BadRequest(new { message = "Message must be 1-4000 characters or include an attachment." });
            if (!IsValidAttachment(nextAttachmentUrl))
                return BadRequest(new { message = "Attachment must be blank, an http URL, or an uploaded file URL." });

            message.userText = nextText;
            message.AttachmentUrl = nextAttachmentUrl;
            message.AttachmentContentType = NormalizeOptional(request.AttachmentContentType);
            message.EditedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return Ok(BuildMessageResponse(message, await GetReactionsForMessage("server", message.MessageID)));
        }

        [HttpPost]
        public async Task<IActionResult> DeleteMessage([FromBody] DeleteServerMessageRequest request)
        {
            var username = User.GetUsername();
            if (string.IsNullOrWhiteSpace(username))
                return Unauthorized(new { message = "Missing user identity." });

            var message = await _context.ServerMessages.FirstOrDefaultAsync(m => m.MessageID == request.MessageId);
            if (message == null)
                return NotFound(new { message = "Message not found." });

            var channel = await _context.Channels.FirstOrDefaultAsync(c => c.Id == message.ChannelId);
            var canManage = channel != null && await _context.ServerMembers.AnyAsync(member =>
                member.ServerId == channel.ServerId &&
                member.Username == username &&
                member.Role == "owner");

            if (message.MessagesUserSender != username && !canManage)
                return Forbid();

            _context.ServerMessages.Remove(message);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Message deleted." });
        }

        [HttpGet]
        public async Task<IActionResult> SearchMessages(
            string channelId,
            string query = "",
            string? fromUser = null,
            string? mentions = null,
            bool? hasAttachment = null,
            int take = 50)
        {
            var username = User.GetUsername();
            if (string.IsNullOrWhiteSpace(username))
                return Unauthorized(new { message = "Missing user identity." });

            var channel = await _context.Channels.FirstOrDefaultAsync(c => c.Id == channelId);
            if (channel == null)
                return NotFound(new { message = "Channel not found." });
            if (!await IsServerMember(channel.ServerId, username))
                return Forbid();

            query = query?.Trim() ?? string.Empty;
            fromUser = fromUser?.Trim();
            mentions = mentions?.Trim();
            take = Math.Clamp(take, 1, 100);

            var messages = await _context.ServerMessages
                .Where(message => message.ChannelId == channelId)
                .ToListAsync();

            var filtered = messages
                .Where(message => query == string.Empty ||
                                  message.userText.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Where(message => string.IsNullOrWhiteSpace(fromUser) ||
                                  string.Equals(message.MessagesUserSender, fromUser, StringComparison.OrdinalIgnoreCase))
                .Where(message => string.IsNullOrWhiteSpace(mentions) ||
                                  ExtractMentions(message.userText).Contains(mentions, StringComparer.OrdinalIgnoreCase))
                .Where(message => hasAttachment == null ||
                                  (!string.IsNullOrWhiteSpace(message.AttachmentUrl) == hasAttachment.Value))
                .OrderByDescending(message => ParseDate(message.Date))
                .Take(take)
                .ToList();

            var messageIds = filtered.Select(message => message.MessageID).ToList();
            var reactions = await _context.MessageReactions
                .Where(reaction => reaction.ScopeType == "server" && messageIds.Contains(reaction.MessageId))
                .ToListAsync();

            return Ok(filtered.Select(message => BuildMessageResponse(
                message,
                reactions.Where(reaction => reaction.MessageId == message.MessageID))));
        }

        [HttpPost]
        [EnableRateLimiting("abuse")]
        public async Task<IActionResult> AddReaction([FromBody] MessageReactionRequest request)
        {
            var username = User.GetUsername();
            if (string.IsNullOrWhiteSpace(username))
                return Unauthorized(new { message = "Missing user identity." });

            var message = await _context.ServerMessages.FirstOrDefaultAsync(m => m.MessageID == request.MessageId);
            if (message == null)
                return NotFound(new { message = "Message not found." });

            var channel = await _context.Channels.FirstOrDefaultAsync(c => c.Id == message.ChannelId);
            if (channel == null || !await IsServerMember(channel.ServerId, username))
                return Forbid();

            var emoji = NormalizeEmoji(request.Emoji);
            if (emoji == null)
                return BadRequest(new { message = "Emoji is required." });

            var existing = await _context.MessageReactions.FirstOrDefaultAsync(reaction =>
                reaction.ScopeType == "server" &&
                reaction.MessageId == request.MessageId &&
                reaction.Emoji == emoji &&
                reaction.Username == username);

            if (existing == null)
            {
                _context.MessageReactions.Add(new MessageReaction
                {
                    Id = Guid.NewGuid().ToString(),
                    ScopeType = "server",
                    MessageId = request.MessageId,
                    Emoji = emoji,
                    Username = username,
                    CreatedAt = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();
            }

            return Ok(await GetReactionSummary("server", request.MessageId));
        }

        [HttpPost]
        public async Task<IActionResult> RemoveReaction([FromBody] MessageReactionRequest request)
        {
            var username = User.GetUsername();
            if (string.IsNullOrWhiteSpace(username))
                return Unauthorized(new { message = "Missing user identity." });

            var emoji = NormalizeEmoji(request.Emoji);
            if (emoji == null)
                return BadRequest(new { message = "Emoji is required." });

            var reactions = await _context.MessageReactions
                .Where(reaction => reaction.ScopeType == "server" &&
                                   reaction.MessageId == request.MessageId &&
                                   reaction.Emoji == emoji &&
                                   reaction.Username == username)
                .ToListAsync();
            _context.MessageReactions.RemoveRange(reactions);
            await _context.SaveChangesAsync();
            return Ok(await GetReactionSummary("server", request.MessageId));
        }

        [HttpPost]
        public async Task<IActionResult> MarkChannelRead([FromBody] MarkReadRequest request)
        {
            var username = User.GetUsername();
            if (string.IsNullOrWhiteSpace(username))
                return Unauthorized(new { message = "Missing user identity." });

            var channel = await _context.Channels.FirstOrDefaultAsync(c => c.Id == request.ScopeId);
            if (channel == null)
                return NotFound(new { message = "Channel not found." });
            if (!await IsServerMember(channel.ServerId, username))
                return Forbid();

            var state = await GetOrCreateUnreadState(username, "server-channel", request.ScopeId);
            state.LastReadMessageId = NormalizeOptional(request.LastReadMessageId);
            state.LastReadAt = request.LastReadAt ?? DateTime.UtcNow;
            state.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return Ok(state);
        }

        [HttpGet]
        public async Task<IActionResult> GetUnreadState(string serverId)
        {
            var username = User.GetUsername();
            if (string.IsNullOrWhiteSpace(username))
                return Unauthorized(new { message = "Missing user identity." });
            if (!await IsServerMember(serverId, username))
                return Forbid();

            var channels = await _context.Channels
                .Where(channel => channel.ServerId == serverId)
                .ToListAsync();
            var channelIds = channels.Select(channel => channel.Id).ToList();
            var states = await _context.UnreadStates
                .Where(state => state.Username == username &&
                                state.ScopeType == "server-channel" &&
                                channelIds.Contains(state.ScopeId))
                .ToListAsync();

            var response = channels.Select(channel =>
            {
                var state = states.FirstOrDefault(s => s.ScopeId == channel.Id);
                var lastRead = state?.LastReadAt ?? DateTime.MinValue;
                var unread = _context.ServerMessages
                    .Where(message => message.ChannelId == channel.Id && message.MessagesUserSender != username)
                    .AsEnumerable()
                    .Count(message => ParseDate(message.Date) > lastRead);

                return new
                {
                    channelId = channel.Id,
                    unread,
                    lastReadAt = state?.LastReadAt,
                    lastReadMessageId = state?.LastReadMessageId
                };
            });

            return Ok(response);
        }

        private async Task<bool> IsServerMember(string serverId, string username)
        {
            return await _context.ServerMembers.AnyAsync(member =>
                member.ServerId == serverId && member.Username == username);
        }

        private async Task<bool> CanSendMessages(string serverId, string username)
        {
            var server = await _context.CreateServers.FirstOrDefaultAsync(s => s.ServerID == serverId);
            if (server?.ServerOwner == username)
            {
                return true;
            }

            var member = await _context.ServerMembers.FirstOrDefaultAsync(m =>
                m.ServerId == serverId && m.Username == username);
            if (member == null)
            {
                return false;
            }

            var roleName = member.Role?.Trim().ToLowerInvariant() ?? "user";
            if (roleName is "owner" or "admin" or "moderator")
            {
                return true;
            }

            var role = await _context.ServerRoles.FirstOrDefaultAsync(r => r.ServerId == serverId && r.Name == roleName);
            return role?.CanSendMessages ?? true;
        }

        private async Task<ServerVerificationResult> GetServerVerificationResult(string serverId, string username)
        {
            var server = await _context.CreateServers.FirstOrDefaultAsync(s => s.ServerID == serverId);
            if (server == null)
            {
                return new ServerVerificationResult(false, ServerVerificationPolicy.None, "Server not found.");
            }

            if (server.ServerOwner == username)
            {
                return ServerVerificationPolicy.Evaluate(ServerVerificationPolicy.None, null, null);
            }

            var member = await _context.ServerMembers.FirstOrDefaultAsync(m =>
                m.ServerId == serverId && m.Username == username);
            var roleName = member?.Role?.Trim().ToLowerInvariant() ?? "user";
            if (roleName is "owner" or "admin" or "moderator")
            {
                return ServerVerificationPolicy.Evaluate(ServerVerificationPolicy.None, null, null);
            }

            var account = await _context.Accounts.FirstOrDefaultAsync(a => a.UserName == username && !a.IsDisabled);
            return ServerVerificationPolicy.EvaluatePosting(server, account, member);
        }

        private async Task<IEnumerable<MessageReaction>> GetReactionsForMessage(string scopeType, string messageId)
        {
            return await _context.MessageReactions
                .Where(reaction => reaction.ScopeType == scopeType && reaction.MessageId == messageId)
                .ToListAsync();
        }

        private async Task<object> GetReactionSummary(string scopeType, string messageId)
        {
            var reactions = await GetReactionsForMessage(scopeType, messageId);
            return SummarizeReactions(reactions);
        }

        private async Task<UnreadState> GetOrCreateUnreadState(string username, string scopeType, string scopeId)
        {
            var state = await _context.UnreadStates.FirstOrDefaultAsync(s =>
                s.Username == username && s.ScopeType == scopeType && s.ScopeId == scopeId);
            if (state != null)
            {
                return state;
            }

            state = new UnreadState
            {
                Id = Guid.NewGuid().ToString(),
                Username = username,
                ScopeType = scopeType,
                ScopeId = scopeId,
                LastReadAt = DateTime.MinValue,
                UpdatedAt = DateTime.UtcNow
            };
            _context.UnreadStates.Add(state);
            return state;
        }

        private static object BuildMessageResponse(Models.ServerMessage message, IEnumerable<MessageReaction> reactions)
        {
            return new
            {
                message.MessageID,
                message.ChannelId,
                message.MessagesUserSender,
                message.Date,
                message.userText,
                message.ReplyToMessageId,
                message.AttachmentUrl,
                message.AttachmentContentType,
                message.EditedAt,
                Mentions = ExtractMentions(message.userText),
                Reactions = SummarizeReactions(reactions)
            };
        }

        private static object SummarizeReactions(IEnumerable<MessageReaction> reactions)
        {
            return reactions
                .GroupBy(reaction => reaction.Emoji)
                .Select(group => new
                {
                    emoji = group.Key,
                    count = group.Count(),
                    users = group.Select(reaction => reaction.Username).Distinct().ToArray()
                })
                .ToArray();
        }

        private static string[] ExtractMentions(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return Array.Empty<string>();
            }

            return System.Text.RegularExpressions.Regex
                .Matches(text, @"@([A-Za-z0-9_.-]{3,32})")
                .Select(match => match.Groups[1].Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static DateTime ParseDate(string? date)
        {
            return DateTime.TryParse(date, out var dt) ? dt : DateTime.MinValue;
        }

        private static bool IsValidMessageBody(string? message, string? attachmentUrl)
        {
            return (!string.IsNullOrWhiteSpace(message) && message.Length <= 4000) ||
                   !string.IsNullOrWhiteSpace(attachmentUrl);
        }

        private static bool IsValidAttachment(string? attachmentUrl)
        {
            if (string.IsNullOrWhiteSpace(attachmentUrl))
            {
                return true;
            }

            if (attachmentUrl.StartsWith("/uploads/", StringComparison.Ordinal))
            {
                return true;
            }

            return Uri.TryCreate(attachmentUrl, UriKind.Absolute, out var parsedUrl) &&
                   (parsedUrl.Scheme == Uri.UriSchemeHttp || parsedUrl.Scheme == Uri.UriSchemeHttps);
        }

        private static string? NormalizeOptional(string? value)
        {
            var normalized = value?.Trim();
            return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
        }

        private static string? NormalizeEmoji(string? emoji)
        {
            var normalized = emoji?.Trim();
            return string.IsNullOrWhiteSpace(normalized) || normalized.Length > 64 ? null : normalized;
        }
    }

    public class EditServerMessageRequest
    {
        public string MessageId { get; set; } = string.Empty;
        public string UserText { get; set; } = string.Empty;
        public string? AttachmentUrl { get; set; }
        public string? AttachmentContentType { get; set; }
    }

    public class DeleteServerMessageRequest
    {
        public string MessageId { get; set; } = string.Empty;
    }

    public class MessageReactionRequest
    {
        public string MessageId { get; set; } = string.Empty;
        public string Emoji { get; set; } = string.Empty;
    }

    public class MarkReadRequest
    {
        public string ScopeId { get; set; } = string.Empty;
        public string? LastReadMessageId { get; set; }
        public DateTime? LastReadAt { get; set; }
    }
}
