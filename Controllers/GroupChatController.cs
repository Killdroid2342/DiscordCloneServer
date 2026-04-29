using System.Collections.Concurrent;
using System.Net.WebSockets;
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
    [Authorize]
    public class GroupChatController : ControllerBase
    {
        private readonly ApiContext _context;
        private static readonly ConcurrentDictionary<string, WebSocket> ActiveSockets = new();

        public GroupChatController(ApiContext context)
        {
            _context = context;
        }

        [HttpPost]
        public async Task<IActionResult> CreateGroup([FromBody] CreateGroupRequest request)
        {
            try
            {
                var owner = User.GetUsername();
                if (string.IsNullOrWhiteSpace(owner))
                    return Unauthorized(new { message = "Missing user identity." });

                var groupName = request.Name?.Trim() ?? string.Empty;
                if (groupName.Length is < 1 or > 80)
                    return BadRequest(new { message = "Group name must be 1-80 characters." });

                var members = (request.Members ?? Array.Empty<string>())
                    .Append(owner)
                    .Select(member => member.Trim())
                    .Where(member => !string.IsNullOrWhiteSpace(member))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (members.Length < 2)
                    return BadRequest(new { message = "A group DM needs at least two members." });
                if (members.Length > 50)
                    return BadRequest(new { message = "Group DMs can have at most 50 members." });

                var existingMembers = await _context.Accounts
                    .Where(account => members.Contains(account.UserName) && !account.IsDisabled)
                    .Select(account => account.UserName)
                    .ToListAsync();

                if (existingMembers.Count != members.Length)
                    return BadRequest(new { message = "One or more members do not exist." });

                var group = new GroupChat
                {
                    Id = Guid.NewGuid(),
                    Name = groupName,
                    Owner = owner,
                    AvatarUrl = NormalizeOptional(request.AvatarUrl),
                    Members = members
                };

                _context.GroupChats.Add(group);

                foreach (var memberName in members)
                {
                    var user = await _context.Accounts.FirstOrDefaultAsync(a => a.UserName == memberName);
                    if (user != null)
                    {
                        user.Groups = AddUnique(user.Groups, group.Id.ToString());
                    }
                }

                await _context.SaveChangesAsync();
                return Ok(group);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"create group failed: {ex}");
                return StatusCode(500, new { message = "Could not create group." });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetGroups()
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
                return Unauthorized(new { message = "Missing user identity." });

            var user = await _context.Accounts.FirstOrDefaultAsync(a => a.UserName == currentUsername && !a.IsDisabled);
            if (user == null || user.Groups == null) return Ok(new List<GroupChat>());

            var groupIds = user.Groups
                .Where(g => Guid.TryParse(g, out _))
                .Select(Guid.Parse)
                .ToList();
            var groups = await _context.GroupChats
                .Where(g => groupIds.Contains(g.Id))
                .ToListAsync();

            return Ok(groups.Where(g => ContainsValue(g.Members, currentUsername)).ToList());
        }

        [HttpGet]
        public async Task<IActionResult> GetGroupMessages(Guid groupId, int take = 50, Guid? beforeMessageId = null)
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
                return Unauthorized(new { message = "Missing user identity." });
            if (!await IsGroupMember(groupId, currentUsername))
                return Forbid();

            take = Math.Clamp(take, 1, 100);

            var messages = await _context.GroupMessages
                .Where(m => m.GroupId == groupId)
                .ToListAsync();

            var orderedMessages = messages
                .OrderBy(m =>
                {
                    if (DateTime.TryParse(m.Date, out var dt)) return dt;

                    if (DateTime.TryParseExact(m.Date, "dd/MM/yyyy, HH:mm:ss",
                           System.Globalization.CultureInfo.InvariantCulture,
                           System.Globalization.DateTimeStyles.None, out var dt2)) return dt2;
                    return DateTime.MinValue;
                })
                .ToList();

            if (beforeMessageId.HasValue)
            {
                var beforeIndex = orderedMessages.FindIndex(message => message.Id == beforeMessageId.Value);
                if (beforeIndex >= 0)
                {
                    orderedMessages = orderedMessages.Take(beforeIndex).ToList();
                }
            }

            return Ok(orderedMessages.TakeLast(take).ToList());
        }

        [HttpPost]
        public async Task<IActionResult> RenameGroup([FromBody] RenameGroupRequest request)
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
                return Unauthorized(new { message = "Missing user identity." });

            var group = await _context.GroupChats.FirstOrDefaultAsync(g => g.Id == request.GroupId);
            if (group == null)
                return NotFound(new { message = "Group not found." });
            if (group.Owner != currentUsername)
                return Forbid();

            var nextName = request.Name?.Trim() ?? string.Empty;
            if (nextName.Length is < 1 or > 80)
                return BadRequest(new { message = "Group name must be 1-80 characters." });

            group.Name = nextName;
            await _context.SaveChangesAsync();
            return Ok(group);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateGroupAvatar([FromBody] GroupAvatarRequest request)
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
                return Unauthorized(new { message = "Missing user identity." });

            var group = await _context.GroupChats.FirstOrDefaultAsync(g => g.Id == request.GroupId);
            if (group == null)
                return NotFound(new { message = "Group not found." });
            if (group.Owner != currentUsername)
                return Forbid();

            var avatarUrl = NormalizeOptional(request.AvatarUrl);
            if (!IsValidAttachment(avatarUrl))
                return BadRequest(new { message = "Group avatar must be blank, an http URL, or an uploaded file URL." });

            group.AvatarUrl = avatarUrl;
            await _context.SaveChangesAsync();
            return Ok(group);
        }

        [HttpPost]
        public async Task<IActionResult> AddGroupMembers([FromBody] GroupMembersRequest request)
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
                return Unauthorized(new { message = "Missing user identity." });

            var group = await _context.GroupChats.FirstOrDefaultAsync(g => g.Id == request.GroupId);
            if (group == null)
                return NotFound(new { message = "Group not found." });
            if (group.Owner != currentUsername)
                return Forbid();

            var nextMembers = (request.Members ?? Array.Empty<string>())
                .Select(member => member.Trim())
                .Where(member => !string.IsNullOrWhiteSpace(member))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if ((group.Members.Length + nextMembers.Length) > 50)
                return BadRequest(new { message = "Group DMs can have at most 50 members." });

            var existingMembers = await _context.Accounts
                .Where(account => nextMembers.Contains(account.UserName) && !account.IsDisabled)
                .Select(account => account.UserName)
                .ToListAsync();
            if (existingMembers.Count != nextMembers.Length)
                return BadRequest(new { message = "One or more members do not exist." });

            foreach (var member in nextMembers)
            {
                group.Members = AddUnique(group.Members, member);
                var account = await _context.Accounts.FirstOrDefaultAsync(a => a.UserName == member);
                if (account != null)
                {
                    account.Groups = AddUnique(account.Groups, group.Id.ToString());
                }
            }

            await _context.SaveChangesAsync();
            return Ok(group);
        }

        [HttpPost]
        public async Task<IActionResult> RemoveGroupMember([FromBody] GroupMemberActionRequest request)
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
                return Unauthorized(new { message = "Missing user identity." });

            var group = await _context.GroupChats.FirstOrDefaultAsync(g => g.Id == request.GroupId);
            if (group == null)
                return NotFound(new { message = "Group not found." });
            if (group.Owner != currentUsername && request.TargetUsername != currentUsername)
                return Forbid();

            var targetUsername = request.TargetUsername?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(targetUsername))
                return BadRequest(new { message = "Target username is required." });

            group.Members = RemoveValue(group.Members, targetUsername);
            var account = await _context.Accounts.FirstOrDefaultAsync(a => a.UserName == targetUsername);
            if (account != null)
            {
                account.Groups = RemoveValue(account.Groups, group.Id.ToString());
            }

            if (group.Members.Length == 0)
            {
                var messages = await _context.GroupMessages.Where(message => message.GroupId == group.Id).ToListAsync();
                _context.GroupMessages.RemoveRange(messages);
                _context.GroupChats.Remove(group);
            }
            else if (group.Owner == targetUsername)
            {
                group.Owner = group.Members[0];
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = "Group member removed." });
        }

        [HttpPost]
        [EnableRateLimiting("abuse")]
        public async Task<IActionResult> SendGroupMessage([FromBody] SendGroupMessageRequest request)
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
                return Unauthorized(new { message = "Missing user identity." });
            if (!await IsGroupMember(request.GroupId, currentUsername))
                return Forbid();

            var content = request.Content?.Trim() ?? string.Empty;
            var attachmentUrl = NormalizeOptional(request.AttachmentUrl);
            if (!IsValidGroupMessage(content, attachmentUrl))
                return BadRequest(new { message = "Message must be 1-4000 characters or include an attachment." });
            if (!IsValidAttachment(attachmentUrl))
                return BadRequest(new { message = "Attachment must be blank, an http URL, or an uploaded file URL." });
            if (request.ReplyToMessageId != null &&
                !await _context.GroupMessages.AnyAsync(message => message.GroupId == request.GroupId && message.Id == request.ReplyToMessageId.Value))
                return BadRequest(new { message = "Reply target was not found in this group." });

            var message = new GroupMessage
            {
                Id = Guid.NewGuid(),
                GroupId = request.GroupId,
                Sender = currentUsername,
                Content = content,
                Date = DateTime.UtcNow.ToString("O"),
                ReplyToMessageId = request.ReplyToMessageId,
                AttachmentUrl = attachmentUrl,
                AttachmentContentType = NormalizeOptional(request.AttachmentContentType)
            };
            _context.GroupMessages.Add(message);
            await _context.SaveChangesAsync();
            await BroadcastToGroup(message.GroupId, JsonSerializer.Serialize(message));
            return Ok(BuildGroupMessageResponse(message, Array.Empty<MessageReaction>()));
        }

        [HttpPost]
        public async Task<IActionResult> EditGroupMessage([FromBody] EditGroupMessageRequest request)
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
                return Unauthorized(new { message = "Missing user identity." });

            var message = await _context.GroupMessages.FirstOrDefaultAsync(m => m.Id == request.MessageId);
            if (message == null)
                return NotFound(new { message = "Message not found." });
            if (message.Sender != currentUsername)
                return Forbid();

            var content = request.Content?.Trim() ?? string.Empty;
            var attachmentUrl = NormalizeOptional(request.AttachmentUrl);
            if (!IsValidGroupMessage(content, attachmentUrl))
                return BadRequest(new { message = "Message must be 1-4000 characters or include an attachment." });
            if (!IsValidAttachment(attachmentUrl))
                return BadRequest(new { message = "Attachment must be blank, an http URL, or an uploaded file URL." });

            message.Content = content;
            message.AttachmentUrl = attachmentUrl;
            message.AttachmentContentType = NormalizeOptional(request.AttachmentContentType);
            message.EditedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return Ok(BuildGroupMessageResponse(message, await GetReactionsForMessage("group", message.Id.ToString())));
        }

        [HttpPost]
        public async Task<IActionResult> DeleteGroupMessage([FromBody] DeleteGroupMessageRequest request)
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
                return Unauthorized(new { message = "Missing user identity." });

            var message = await _context.GroupMessages.FirstOrDefaultAsync(m => m.Id == request.MessageId);
            if (message == null)
                return NotFound(new { message = "Message not found." });

            var group = await _context.GroupChats.FirstOrDefaultAsync(g => g.Id == message.GroupId);
            if (message.Sender != currentUsername && group?.Owner != currentUsername)
                return Forbid();

            _context.GroupMessages.Remove(message);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Message deleted." });
        }

        [HttpGet]
        public async Task<IActionResult> SearchGroupMessages(Guid groupId, string query = "", string? fromUser = null, bool? hasAttachment = null, int take = 50)
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
                return Unauthorized(new { message = "Missing user identity." });
            if (!await IsGroupMember(groupId, currentUsername))
                return Forbid();

            query = query?.Trim() ?? string.Empty;
            fromUser = fromUser?.Trim();
            take = Math.Clamp(take, 1, 100);

            var messages = await _context.GroupMessages.Where(m => m.GroupId == groupId).ToListAsync();
            var filtered = messages
                .Where(message => query == string.Empty || message.Content.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Where(message => string.IsNullOrWhiteSpace(fromUser) || string.Equals(message.Sender, fromUser, StringComparison.OrdinalIgnoreCase))
                .Where(message => hasAttachment == null || (!string.IsNullOrWhiteSpace(message.AttachmentUrl) == hasAttachment.Value))
                .OrderByDescending(message => ParseDate(message.Date))
                .Take(take)
                .ToList();

            var messageIds = filtered.Select(message => message.Id.ToString()).ToList();
            var reactions = await _context.MessageReactions
                .Where(reaction => reaction.ScopeType == "group" && messageIds.Contains(reaction.MessageId))
                .ToListAsync();

            return Ok(filtered.Select(message => BuildGroupMessageResponse(
                message,
                reactions.Where(reaction => reaction.MessageId == message.Id.ToString()))));
        }

        [HttpPost]
        [EnableRateLimiting("abuse")]
        public async Task<IActionResult> AddReaction([FromBody] GroupMessageReactionRequest request)
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
                return Unauthorized(new { message = "Missing user identity." });

            var message = await _context.GroupMessages.FirstOrDefaultAsync(m => m.Id == request.MessageId);
            if (message == null)
                return NotFound(new { message = "Message not found." });
            if (!await IsGroupMember(message.GroupId, currentUsername))
                return Forbid();

            var emoji = NormalizeEmoji(request.Emoji);
            if (emoji == null)
                return BadRequest(new { message = "Emoji is required." });

            var messageId = request.MessageId.ToString();
            var existing = await _context.MessageReactions.FirstOrDefaultAsync(reaction =>
                reaction.ScopeType == "group" &&
                reaction.MessageId == messageId &&
                reaction.Emoji == emoji &&
                reaction.Username == currentUsername);
            if (existing == null)
            {
                _context.MessageReactions.Add(new MessageReaction
                {
                    Id = Guid.NewGuid().ToString(),
                    ScopeType = "group",
                    MessageId = messageId,
                    Emoji = emoji,
                    Username = currentUsername,
                    CreatedAt = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();
            }

            return Ok(await GetReactionSummary("group", messageId));
        }

        [HttpPost]
        public async Task<IActionResult> RemoveReaction([FromBody] GroupMessageReactionRequest request)
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
                return Unauthorized(new { message = "Missing user identity." });

            var emoji = NormalizeEmoji(request.Emoji);
            if (emoji == null)
                return BadRequest(new { message = "Emoji is required." });

            var messageId = request.MessageId.ToString();
            var reactions = await _context.MessageReactions
                .Where(reaction => reaction.ScopeType == "group" &&
                                   reaction.MessageId == messageId &&
                                   reaction.Emoji == emoji &&
                                   reaction.Username == currentUsername)
                .ToListAsync();
            _context.MessageReactions.RemoveRange(reactions);
            await _context.SaveChangesAsync();
            return Ok(await GetReactionSummary("group", messageId));
        }

        [HttpPost]
        public async Task<IActionResult> MarkGroupRead([FromBody] GroupMarkReadRequest request)
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
                return Unauthorized(new { message = "Missing user identity." });
            if (!await IsGroupMember(request.GroupId, currentUsername))
                return Forbid();

            var state = await GetOrCreateUnreadState(currentUsername, "group", request.GroupId.ToString());
            state.LastReadMessageId = request.LastReadMessageId?.ToString();
            state.LastReadAt = request.LastReadAt ?? DateTime.UtcNow;
            state.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return Ok(state);
        }

        [HttpGet]
        public async Task<IActionResult> GetUnreadBadges()
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
                return Unauthorized(new { message = "Missing user identity." });

            var account = await _context.Accounts.FirstOrDefaultAsync(a => a.UserName == currentUsername && !a.IsDisabled);
            var accountGroupIds = (account?.Groups ?? Array.Empty<string>())
                .Where(id => Guid.TryParse(id, out _))
                .Select(Guid.Parse)
                .ToList();
            var groups = await _context.GroupChats
                .Where(group => accountGroupIds.Contains(group.Id))
                .ToListAsync();
            var groupIds = groups.Select(group => group.Id).ToList();
            var states = await _context.UnreadStates
                .Where(state => state.Username == currentUsername && state.ScopeType == "group")
                .ToListAsync();
            var messages = await _context.GroupMessages
                .Where(message => groupIds.Contains(message.GroupId))
                .ToListAsync();

            return Ok(groups.Select(group =>
            {
                var state = states.FirstOrDefault(s => s.ScopeId == group.Id.ToString());
                var lastReadAt = state?.LastReadAt ?? DateTime.MinValue;
                return new
                {
                    groupId = group.Id,
                    unread = messages.Count(message =>
                        message.GroupId == group.Id &&
                        message.Sender != currentUsername &&
                        ParseDate(message.Date) > lastReadAt),
                    lastReadAt = state?.LastReadAt,
                    lastReadMessageId = state?.LastReadMessageId
                };
            }));
        }

        [HttpPost]
        public async Task<IActionResult> LeaveGroup([FromBody] GroupActionRequest request)
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
                return Unauthorized(new { message = "Missing user identity." });

            var group = await _context.GroupChats.FirstOrDefaultAsync(g => g.Id == request.GroupId);
            if (group == null)
                return NotFound(new { message = "Group not found." });
            if (!ContainsValue(group.Members, currentUsername))
                return Forbid();

            group.Members = RemoveValue(group.Members, currentUsername);
            var user = await _context.Accounts.FirstOrDefaultAsync(a => a.UserName == currentUsername);
            if (user != null)
            {
                user.Groups = RemoveValue(user.Groups, group.Id.ToString());
            }

            if (group.Members.Length == 0)
            {
                var messages = await _context.GroupMessages.Where(message => message.GroupId == group.Id).ToListAsync();
                _context.GroupMessages.RemoveRange(messages);
                _context.GroupChats.Remove(group);
            }
            else if (group.Owner == currentUsername)
            {
                group.Owner = group.Members[0];
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = "Left group." });
        }

        [HttpGet]
        public async Task HandleGroupWebsocket()
        {
            var currentUsername = User.GetUsername();
            if (string.IsNullOrWhiteSpace(currentUsername))
            {
                HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            if (HttpContext.WebSockets.IsWebSocketRequest)
            {
                var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
                ActiveSockets[currentUsername] = webSocket;
                await ListenForMessages(currentUsername, webSocket);
                ActiveSockets.TryRemove(currentUsername, out _);
            }
            else
            {
                HttpContext.Response.StatusCode = 400;
            }
        }

        private async Task ListenForMessages(string username, WebSocket webSocket)
        {
            var buffer = new byte[1024 * 4];
            while (webSocket.State == WebSocketState.Open)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var messageJson = Encoding.UTF8.GetString(buffer, 0, result.Count);

                    using var doc = JsonDocument.Parse(messageJson);
                    var root = doc.RootElement;
                    var type = root.TryGetProperty("Type", out var typeProp) ? (typeProp.GetString() ?? "chat") : "chat";

                    if (type == "chat")
                    {
                        var groupMessage = JsonSerializer.Deserialize<GroupMessage>(messageJson);
                        if (groupMessage == null ||
                            !await IsGroupMember(groupMessage.GroupId, username) ||
                            !IsValidGroupMessage(groupMessage.Content))
                        {
                            continue;
                        }

                        groupMessage.Id = Guid.NewGuid();
                        groupMessage.Sender = username;
                        groupMessage.Content = groupMessage.Content.Trim();
                        groupMessage.Date = DateTime.UtcNow.ToString("O");
                        groupMessage.AttachmentUrl = NormalizeOptional(groupMessage.AttachmentUrl);
                        groupMessage.AttachmentContentType = NormalizeOptional(groupMessage.AttachmentContentType);
                        _context.GroupMessages.Add(groupMessage);
                        await _context.SaveChangesAsync();

                        await BroadcastToGroup(groupMessage.GroupId, JsonSerializer.Serialize(groupMessage));
                        continue;
                    }

                    var groupId = TryGetGuid(root, "GroupId");
                    if (groupId == null || !await IsGroupMember(groupId.Value, username))
                    {
                        continue;
                    }

                    var targetUser = root.TryGetProperty("TargetUser", out var targetProp) ? targetProp.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(targetUser) && !await IsGroupMember(groupId.Value, targetUser))
                    {
                        continue;
                    }

                    var forwardedJson = BuildForwardedSignalJson(root, username);
                    if (!string.IsNullOrWhiteSpace(targetUser))
                    {
                        await SendToUser(targetUser, forwardedJson);
                    }
                    else
                    {
                        await BroadcastToGroup(groupId.Value, forwardedJson);
                    }
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed", CancellationToken.None);
                }
            }
        }

        private async Task BroadcastToGroup(Guid groupId, string messageJson)
        {
            var group = await _context.GroupChats.FirstOrDefaultAsync(g => g.Id == groupId);
            if (group == null)
            {
                return;
            }

            foreach (var member in group.Members.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                await SendToUser(member, messageJson);
            }
        }

        private static async Task SendToUser(string targetUser, string messageJson)
        {
            if (ActiveSockets.TryGetValue(targetUser, out var socket) && socket.State == WebSocketState.Open)
            {
                var msgBuffer = Encoding.UTF8.GetBytes(messageJson);
                await socket.SendAsync(new ArraySegment<byte>(msgBuffer), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }

        private async Task<bool> IsGroupMember(Guid groupId, string username)
        {
            var group = await _context.GroupChats.FirstOrDefaultAsync(g => g.Id == groupId);
            return group?.Members.Any(member => string.Equals(member, username, StringComparison.OrdinalIgnoreCase)) == true;
        }

        private static bool IsValidGroupMessage(string? message, string? attachmentUrl = null)
        {
            return (!string.IsNullOrWhiteSpace(message) && message.Length <= 4000) ||
                   !string.IsNullOrWhiteSpace(attachmentUrl);
        }

        private static Guid? TryGetGuid(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var prop))
            {
                return null;
            }

            return Guid.TryParse(prop.GetString(), out var groupId) ? groupId : null;
        }

        private static string BuildForwardedSignalJson(JsonElement root, string username)
        {
            var payload = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
            {
                if (property.NameEquals("Sender"))
                {
                    continue;
                }

                payload[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Number => property.Value.GetRawText(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null,
                    _ => property.Value.GetRawText()
                };
            }

            payload["Sender"] = username;
            return JsonSerializer.Serialize(payload);
        }

        private static bool ContainsValue(string[]? values, string value)
        {
            return values?.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase)) == true;
        }

        private static string[] AddUnique(string[]? values, string value)
        {
            return (values ?? Array.Empty<string>())
                .Append(value)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string[] RemoveValue(string[]? values, string value)
        {
            return (values ?? Array.Empty<string>())
                .Where(item => !string.Equals(item, value, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
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

        private static object BuildGroupMessageResponse(GroupMessage message, IEnumerable<MessageReaction> reactions)
        {
            return new
            {
                message.Id,
                message.GroupId,
                message.Sender,
                message.Content,
                message.Date,
                message.ReplyToMessageId,
                message.AttachmentUrl,
                message.AttachmentContentType,
                message.EditedAt,
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

        private static DateTime ParseDate(string? date)
        {
            return DateTime.TryParse(date, out var dt) ? dt : DateTime.MinValue;
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

    public class CreateGroupRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Owner { get; set; } = string.Empty;
        public string[] Members { get; set; } = Array.Empty<string>();
        public string? AvatarUrl { get; set; }
    }

    public class RenameGroupRequest
    {
        public Guid GroupId { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class GroupActionRequest
    {
        public Guid GroupId { get; set; }
    }

    public class GroupAvatarRequest
    {
        public Guid GroupId { get; set; }
        public string? AvatarUrl { get; set; }
    }

    public class GroupMembersRequest
    {
        public Guid GroupId { get; set; }
        public string[] Members { get; set; } = Array.Empty<string>();
    }

    public class GroupMemberActionRequest
    {
        public Guid GroupId { get; set; }
        public string TargetUsername { get; set; } = string.Empty;
    }

    public class SendGroupMessageRequest
    {
        public Guid GroupId { get; set; }
        public string Content { get; set; } = string.Empty;
        public Guid? ReplyToMessageId { get; set; }
        public string? AttachmentUrl { get; set; }
        public string? AttachmentContentType { get; set; }
    }

    public class EditGroupMessageRequest
    {
        public Guid MessageId { get; set; }
        public string Content { get; set; } = string.Empty;
        public string? AttachmentUrl { get; set; }
        public string? AttachmentContentType { get; set; }
    }

    public class DeleteGroupMessageRequest
    {
        public Guid MessageId { get; set; }
    }

    public class GroupMessageReactionRequest
    {
        public Guid MessageId { get; set; }
        public string Emoji { get; set; } = string.Empty;
    }

    public class GroupMarkReadRequest
    {
        public Guid GroupId { get; set; }
        public Guid? LastReadMessageId { get; set; }
        public DateTime? LastReadAt { get; set; }
    }
}
