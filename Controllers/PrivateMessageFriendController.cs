using System.Collections.Concurrent;
using System.Globalization;
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
    public class PrivateMessageFriendController : ControllerBase
    {
        private readonly ApiContext _context;
        private readonly IConfiguration _config;
        private readonly IEmailNotificationSender? _emailNotificationSender;
        private readonly ISpamDetectionService? _spamDetectionService;
        private readonly IBackgroundJobQueue? _backgroundJobQueue;
        private readonly IMessageNotificationService? _messageNotificationService;

        private static readonly ConcurrentDictionary<string, WebSocket> ActiveSockets = new();

        public PrivateMessageFriendController(
            ApiContext context,
            IConfiguration config,
            IEmailNotificationSender? emailNotificationSender = null,
            ISpamDetectionService? spamDetectionService = null,
            IBackgroundJobQueue? backgroundJobQueue = null,
            IMessageNotificationService? messageNotificationService = null)
        {
            _context = context;
            _config = config;
            _emailNotificationSender = emailNotificationSender;
            _spamDetectionService = spamDetectionService;
            _backgroundJobQueue = backgroundJobQueue;
            _messageNotificationService = messageNotificationService;
        }

        [HttpPost]
        [EnableRateLimiting("abuse")]
        public async Task<IActionResult> SendPrivateMessage(PrivateMessageFriend privateMessageFriend)
        {
            var username = User.GetUsername();
            if (string.IsNullOrWhiteSpace(username))
                return Unauthorized(new { message = "Missing user identity." });

            privateMessageFriend.MessageUserReciver = privateMessageFriend.MessageUserReciver?.Trim() ?? string.Empty;
            privateMessageFriend.FriendMessagesData = privateMessageFriend.FriendMessagesData?.Trim() ?? string.Empty;
            privateMessageFriend.AttachmentUrl = NormalizeOptional(privateMessageFriend.AttachmentUrl);
            privateMessageFriend.AttachmentContentType = NormalizeOptional(privateMessageFriend.AttachmentContentType);
            privateMessageFriend.ReplyToMessageId = NormalizeOptional(privateMessageFriend.ReplyToMessageId);
            if (!MessagePollService.TryNormalizeDraft(privateMessageFriend.Poll, out var pollDraft, out var pollError))
                return BadRequest(new { message = pollError });

            if (!await CanSendDm(username, privateMessageFriend.MessageUserReciver))
                return Forbid();
            if (!IsValidPrivateMessage(privateMessageFriend.FriendMessagesData, privateMessageFriend.AttachmentUrl, pollDraft != null))
                return BadRequest(new { message = "Message must be 1-4000 characters, include an attachment, or include a poll." });
            if (!IsValidAttachment(privateMessageFriend.AttachmentUrl))
                return BadRequest(new { message = "Attachment must be blank, an http URL, or an uploaded file URL." });
            PrivateMessageFriend? replyTarget = null;
            if (!string.IsNullOrWhiteSpace(privateMessageFriend.ReplyToMessageId))
            {
                replyTarget = await _context.PrivateMessageFriends.FirstOrDefaultAsync(message =>
                    message.PrivateMessageID == privateMessageFriend.ReplyToMessageId &&
                    ((message.MessagesUserSender == username && message.MessageUserReciver == privateMessageFriend.MessageUserReciver) ||
                     (message.MessagesUserSender == privateMessageFriend.MessageUserReciver && message.MessageUserReciver == username)));
                if (replyTarget == null)
                    return BadRequest(new { message = "Reply target was not found in this conversation." });
            }

            privateMessageFriend.PrivateMessageID = string.IsNullOrWhiteSpace(privateMessageFriend.PrivateMessageID)
                ? Guid.NewGuid().ToString()
                : privateMessageFriend.PrivateMessageID.Trim();
            privateMessageFriend.MessagesUserSender = username;
            privateMessageFriend.Date = DateTime.UtcNow.ToString("O");

            if (await _context.PrivateMessageFriends.AnyAsync(pm => pm.PrivateMessageID == privateMessageFriend.PrivateMessageID))
            {
                return Conflict(new { message = "Duplicate PrivateMessageID" });
            }

            var spamBlock = await RejectSpamIfDetected(new SpamDetectionRequest(
                "dm",
                BuildDmScopeId(username, privateMessageFriend.MessageUserReciver),
                username,
                privateMessageFriend.FriendMessagesData,
                privateMessageFriend.AttachmentUrl,
                privateMessageFriend.MessageUserReciver));
            if (spamBlock != null)
                return spamBlock;

            _context.PrivateMessageFriends.Add(privateMessageFriend);
            if (pollDraft != null)
            {
                MessagePollService.AddPoll(_context, "dm", privateMessageFriend.PrivateMessageID, username, pollDraft);
            }

            await _context.SaveChangesAsync();
            await DispatchPrivateEmailNotification(privateMessageFriend);
            var pollLookup = await MessagePollService.GetPollLookupAsync(
                _context,
                "dm",
                new[] { privateMessageFriend.PrivateMessageID },
                username,
                HttpContext.RequestAborted);
            var response = BuildPrivateMessageResponse(
                privateMessageFriend,
                Array.Empty<MessageReaction>(),
                replyTarget,
                pollLookup.GetValueOrDefault(privateMessageFriend.PrivateMessageID));
            await SendPrivateSocketMessage(privateMessageFriend.MessageUserReciver, response);
            return Ok(response);
        }

        [HttpGet]
        public async Task<IActionResult> GetPrivateMessage(
            string targetUsername,
            int take = 50,
            string? beforeMessageId = null,
            bool includePageInfo = false)
        {
            var username = User.GetUsername();
            if (string.IsNullOrWhiteSpace(username))
                return Unauthorized(new { message = "Missing user identity." });

            targetUsername = targetUsername?.Trim() ?? string.Empty;
            if (!await CanSendDm(username, targetUsername))
                return Forbid();

            take = Math.Clamp(take, 1, 100);

            var messages = await _context.PrivateMessageFriends
                .Where(pm => (pm.MessagesUserSender == username && pm.MessageUserReciver == targetUsername) ||
                             (pm.MessagesUserSender == targetUsername && pm.MessageUserReciver == username))
                .ToListAsync();

            var orderedMessages = messages
                .OrderBy(pm =>
                {
                    if (DateTime.TryParse(pm.Date, out var dt)) return dt;
                    return DateTime.MinValue;
                })
                .ToList();

            var totalCount = orderedMessages.Count;
            var boundaryCount = orderedMessages.Count;
            if (!string.IsNullOrWhiteSpace(beforeMessageId))
            {
                var beforeIndex = orderedMessages.FindIndex(message => message.PrivateMessageID == beforeMessageId);
                if (beforeIndex >= 0)
                {
                    boundaryCount = beforeIndex;
                }
            }

            var pageWindow = orderedMessages.Take(boundaryCount).ToList();
            var page = pageWindow.TakeLast(take).ToList();
            var messageIds = page.Select(message => message.PrivateMessageID).ToList();
            var reactions = await _context.MessageReactions
                .Where(reaction => reaction.ScopeType == "dm" && messageIds.Contains(reaction.MessageId))
                .ToListAsync();
            var replyPreviews = GetPrivateReplyPreviewLookup(page, messages);
            var pollLookup = await MessagePollService.GetPollLookupAsync(
                _context,
                "dm",
                messageIds,
                username,
                HttpContext.RequestAborted);

            var responseMessages = page.Select(message => BuildPrivateMessageResponse(
                message,
                reactions.Where(reaction => reaction.MessageId == message.PrivateMessageID),
                GetPrivateReplyPreview(message, replyPreviews),
                pollLookup.GetValueOrDefault(message.PrivateMessageID))).ToList();

            if (!includePageInfo)
            {
                return Ok(responseMessages);
            }

            return Ok(new
            {
                messages = responseMessages,
                hasMore = boundaryCount > page.Count,
                nextBeforeMessageId = page.FirstOrDefault()?.PrivateMessageID,
                beforeMessageId,
                pageSize = take,
                returnedCount = page.Count,
                totalCount
            });
        }

        [HttpPost]
        public async Task<IActionResult> EditPrivateMessage([FromBody] EditPrivateMessageRequest request)
        {
            var username = User.GetUsername();
            if (string.IsNullOrWhiteSpace(username))
                return Unauthorized(new { message = "Missing user identity." });

            var message = await _context.PrivateMessageFriends.FirstOrDefaultAsync(pm => pm.PrivateMessageID == request.MessageId);
            if (message == null)
                return NotFound(new { message = "Message not found." });
            if (message.MessagesUserSender != username)
                return Forbid();

            var nextText = request.FriendMessagesData?.Trim() ?? string.Empty;
            var nextAttachmentUrl = NormalizeOptional(request.AttachmentUrl);
            var pollLookup = await MessagePollService.GetPollLookupAsync(
                _context,
                "dm",
                new[] { message.PrivateMessageID },
                username,
                HttpContext.RequestAborted);
            if (!IsValidPrivateMessage(nextText, nextAttachmentUrl, pollLookup.ContainsKey(message.PrivateMessageID)))
                return BadRequest(new { message = "Message must be 1-4000 characters, include an attachment, or include a poll." });
            if (!IsValidAttachment(nextAttachmentUrl))
                return BadRequest(new { message = "Attachment must be blank, an http URL, or an uploaded file URL." });

            message.FriendMessagesData = nextText;
            message.AttachmentUrl = nextAttachmentUrl;
            message.AttachmentContentType = NormalizeOptional(request.AttachmentContentType);
            message.EditedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(BuildPrivateMessageResponse(
                message,
                await GetReactionsForMessage("dm", message.PrivateMessageID),
                null,
                pollLookup.GetValueOrDefault(message.PrivateMessageID)));
        }

        [HttpPost]
        public async Task<IActionResult> DeletePrivateMessage([FromBody] DeletePrivateMessageRequest request)
        {
            var username = User.GetUsername();
            if (string.IsNullOrWhiteSpace(username))
                return Unauthorized(new { message = "Missing user identity." });

            var message = await _context.PrivateMessageFriends.FirstOrDefaultAsync(pm => pm.PrivateMessageID == request.MessageId);
            if (message == null)
                return NotFound(new { message = "Message not found." });
            if (message.MessagesUserSender != username)
                return Forbid();

            await MessagePollService.RemovePollsForMessagesAsync(
                _context,
                "dm",
                new[] { message.PrivateMessageID },
                HttpContext.RequestAborted);
            _context.PrivateMessageFriends.Remove(message);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Message deleted." });
        }

        [HttpGet]
        public async Task<IActionResult> SearchPrivateMessages(
            string targetUsername,
            string query = "",
            string? fromUser = null,
            string? mentions = null,
            string? after = null,
            string? before = null,
            bool? hasAttachment = null,
            string? attachmentType = null,
            bool? hasLink = null,
            int take = 50)
        {
            var username = User.GetUsername();
            if (string.IsNullOrWhiteSpace(username))
                return Unauthorized(new { message = "Missing user identity." });

            targetUsername = targetUsername?.Trim() ?? string.Empty;
            if (!await CanSendDm(username, targetUsername))
                return Forbid();

            query = query?.Trim() ?? string.Empty;
            fromUser = fromUser?.Trim();
            mentions = NormalizeMentionFilter(mentions);
            var afterDate = ParseSearchBoundary(after, endOfDay: false);
            var beforeDate = ParseSearchBoundary(before, endOfDay: true);
            take = Math.Clamp(take, 1, 100);

            var messages = await _context.PrivateMessageFriends
                .Where(pm => (pm.MessagesUserSender == username && pm.MessageUserReciver == targetUsername) ||
                             (pm.MessagesUserSender == targetUsername && pm.MessageUserReciver == username))
                .ToListAsync();

            var filtered = messages
                .Where(message => query == string.Empty ||
                                  message.FriendMessagesData.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Where(message => string.IsNullOrWhiteSpace(fromUser) ||
                                  string.Equals(message.MessagesUserSender, fromUser, StringComparison.OrdinalIgnoreCase))
                .Where(message => string.IsNullOrWhiteSpace(mentions) ||
                                  ExtractMentions(message.FriendMessagesData).Contains(mentions, StringComparer.OrdinalIgnoreCase))
                .Where(message => afterDate == null || ParseDate(message.Date) >= afterDate.Value)
                .Where(message => beforeDate == null || ParseDate(message.Date) <= beforeDate.Value)
                .Where(message => hasAttachment == null ||
                                  (!string.IsNullOrWhiteSpace(message.AttachmentUrl) == hasAttachment.Value))
                .Where(message => MatchesAttachmentType(
                    message.AttachmentContentType,
                    message.AttachmentUrl,
                    attachmentType))
                .Where(message => hasLink == null ||
                                  ContainsLink(message.FriendMessagesData) == hasLink.Value)
                .OrderByDescending(message => ParseDate(message.Date))
                .Take(take)
                .ToList();

            var messageIds = filtered.Select(message => message.PrivateMessageID).ToList();
            var reactions = await _context.MessageReactions
                .Where(reaction => reaction.ScopeType == "dm" && messageIds.Contains(reaction.MessageId))
                .ToListAsync();
            var replyPreviews = GetPrivateReplyPreviewLookup(filtered, messages);
            var pollLookup = await MessagePollService.GetPollLookupAsync(
                _context,
                "dm",
                messageIds,
                username,
                HttpContext.RequestAborted);

            return Ok(filtered.Select(message => BuildPrivateMessageResponse(
                message,
                reactions.Where(reaction => reaction.MessageId == message.PrivateMessageID),
                GetPrivateReplyPreview(message, replyPreviews),
                pollLookup.GetValueOrDefault(message.PrivateMessageID))));
        }

        [HttpPost]
        [EnableRateLimiting("abuse")]
        public async Task<IActionResult> AddReaction([FromBody] PrivateMessageReactionRequest request)
        {
            var username = User.GetUsername();
            if (string.IsNullOrWhiteSpace(username))
                return Unauthorized(new { message = "Missing user identity." });

            var message = await _context.PrivateMessageFriends.FirstOrDefaultAsync(pm => pm.PrivateMessageID == request.MessageId);
            if (message == null)
                return NotFound(new { message = "Message not found." });
            if (message.MessagesUserSender != username && message.MessageUserReciver != username)
                return Forbid();

            var emoji = NormalizeEmoji(request.Emoji);
            if (emoji == null)
                return BadRequest(new { message = "Emoji is required." });

            var existing = await _context.MessageReactions.FirstOrDefaultAsync(reaction =>
                reaction.ScopeType == "dm" &&
                reaction.MessageId == request.MessageId &&
                reaction.Emoji == emoji &&
                reaction.Username == username);
            if (existing == null)
            {
                _context.MessageReactions.Add(new MessageReaction
                {
                    Id = Guid.NewGuid().ToString(),
                    ScopeType = "dm",
                    MessageId = request.MessageId,
                    Emoji = emoji,
                    Username = username,
                    CreatedAt = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();
            }

            return Ok(await GetReactionSummary("dm", request.MessageId));
        }

        [HttpPost]
        public async Task<IActionResult> RemoveReaction([FromBody] PrivateMessageReactionRequest request)
        {
            var username = User.GetUsername();
            if (string.IsNullOrWhiteSpace(username))
                return Unauthorized(new { message = "Missing user identity." });

            var emoji = NormalizeEmoji(request.Emoji);
            if (emoji == null)
                return BadRequest(new { message = "Emoji is required." });

            var reactions = await _context.MessageReactions
                .Where(reaction => reaction.ScopeType == "dm" &&
                                   reaction.MessageId == request.MessageId &&
                                   reaction.Emoji == emoji &&
                                   reaction.Username == username)
                .ToListAsync();
            _context.MessageReactions.RemoveRange(reactions);
            await _context.SaveChangesAsync();
            return Ok(await GetReactionSummary("dm", request.MessageId));
        }

        [HttpPost]
        public async Task<IActionResult> MarkDmRead([FromBody] DmMarkReadRequest request)
        {
            var username = User.GetUsername();
            if (string.IsNullOrWhiteSpace(username))
                return Unauthorized(new { message = "Missing user identity." });

            var targetUsername = request.TargetUsername?.Trim() ?? string.Empty;
            if (!await CanSendDm(username, targetUsername))
                return Forbid();

            var scopeId = BuildDmScopeId(username, targetUsername);
            var state = await GetOrCreateUnreadState(username, "dm", scopeId);
            state.LastReadMessageId = NormalizeOptional(request.LastReadMessageId);
            state.LastReadAt = request.LastReadAt ?? DateTime.UtcNow;
            state.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return Ok(state);
        }

        [HttpGet]
        public async Task<IActionResult> GetUnreadBadges()
        {
            var username = User.GetUsername();
            if (string.IsNullOrWhiteSpace(username))
                return Unauthorized(new { message = "Missing user identity." });

            var messages = await _context.PrivateMessageFriends
                .Where(pm => pm.MessageUserReciver == username)
                .ToListAsync();
            var states = await _context.UnreadStates
                .Where(state => state.Username == username && state.ScopeType == "dm")
                .ToListAsync();

            var badges = messages
                .GroupBy(message => message.MessagesUserSender)
                .Select(group =>
                {
                    var scopeId = BuildDmScopeId(username, group.Key);
                    var state = states.FirstOrDefault(s => s.ScopeId == scopeId);
                    var lastReadAt = state?.LastReadAt ?? DateTime.MinValue;
                    var unreadMessages = group
                        .Where(message => ParseDate(message.Date) > lastReadAt)
                        .ToList();
                    return new
                    {
                        targetUsername = group.Key,
                        unread = unreadMessages.Count,
                        mentionCount = unreadMessages.Count(message => MentionsUser(message.FriendMessagesData, username)),
                        lastReadAt = state?.LastReadAt,
                        lastReadMessageId = state?.LastReadMessageId
                    };
                });

            return Ok(badges);
        }

        [HttpGet]
        public async Task HandlePrivateWebsocket()
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
                await ReplaceActiveSocket(currentUsername, webSocket);

                try
                {
                    await ListenForMessages(currentUsername, webSocket);
                }
                finally
                {
                    RemoveActiveSocket(currentUsername, webSocket);
                }
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
                    var privateMessage = JsonSerializer.Deserialize<PrivateMessageFriend>(messageJson);
                    if (privateMessage == null)
                    {
                        continue;
                    }

                    privateMessage.MessagesUserSender = username;
                    privateMessage.MessageUserReciver = privateMessage.MessageUserReciver?.Trim() ?? string.Empty;
                    privateMessage.FriendMessagesData = privateMessage.FriendMessagesData?.Trim() ?? string.Empty;
                    privateMessage.AttachmentUrl = NormalizeOptional(privateMessage.AttachmentUrl);
                    privateMessage.AttachmentContentType = NormalizeOptional(privateMessage.AttachmentContentType);
                    privateMessage.ReplyToMessageId = NormalizeOptional(privateMessage.ReplyToMessageId);
                    if (!MessagePollService.TryNormalizeDraft(privateMessage.Poll, out var pollDraft, out _))
                    {
                        continue;
                    }
                    privateMessage.Date = DateTime.UtcNow.ToString("O");
                    privateMessage.PrivateMessageID = string.IsNullOrWhiteSpace(privateMessage.PrivateMessageID)
                        ? Guid.NewGuid().ToString()
                        : privateMessage.PrivateMessageID.Trim();

                    var existingMessage = await _context.PrivateMessageFriends.FirstOrDefaultAsync(pm =>
                        pm.PrivateMessageID == privateMessage.PrivateMessageID &&
                        pm.MessagesUserSender == username &&
                        pm.MessageUserReciver == privateMessage.MessageUserReciver);
                    if (existingMessage != null)
                    {
                        var existingPollLookup = await MessagePollService.GetPollLookupAsync(
                            _context,
                            "dm",
                            new[] { existingMessage.PrivateMessageID },
                            username);
                        await SendPrivateSocketMessage(
                            privateMessage.MessageUserReciver,
                            BuildPrivateMessageResponse(
                                existingMessage,
                                await GetReactionsForMessage("dm", existingMessage.PrivateMessageID),
                                null,
                                existingPollLookup.GetValueOrDefault(existingMessage.PrivateMessageID)));
                        continue;
                    }

                    if (!IsValidPrivateMessage(privateMessage.FriendMessagesData, privateMessage.AttachmentUrl, pollDraft != null) ||
                        !IsValidAttachment(privateMessage.AttachmentUrl) ||
                        !await CanSendDm(username, privateMessage.MessageUserReciver) ||
                        (!string.IsNullOrWhiteSpace(privateMessage.ReplyToMessageId) &&
                         !await _context.PrivateMessageFriends.AnyAsync(message =>
                             message.PrivateMessageID == privateMessage.ReplyToMessageId &&
                             ((message.MessagesUserSender == username && message.MessageUserReciver == privateMessage.MessageUserReciver) ||
                              (message.MessagesUserSender == privateMessage.MessageUserReciver && message.MessageUserReciver == username)))) ||
                        await _context.PrivateMessageFriends.AnyAsync(pm => pm.PrivateMessageID == privateMessage.PrivateMessageID))
                    {
                        continue;
                    }

                    if (await IsSpamDetected(new SpamDetectionRequest(
                            "dm",
                            BuildDmScopeId(username, privateMessage.MessageUserReciver),
                            username,
                            privateMessage.FriendMessagesData,
                            privateMessage.AttachmentUrl,
                            privateMessage.MessageUserReciver)))
                    {
                        continue;
                    }

                    _context.PrivateMessageFriends.Add(privateMessage);
                    if (pollDraft != null)
                    {
                        MessagePollService.AddPoll(_context, "dm", privateMessage.PrivateMessageID, username, pollDraft);
                    }

                    await _context.SaveChangesAsync();
                    await DispatchPrivateEmailNotification(privateMessage);

                    var pollLookup = await MessagePollService.GetPollLookupAsync(
                        _context,
                        "dm",
                        new[] { privateMessage.PrivateMessageID },
                        username);
                    await SendPrivateSocketMessage(
                        privateMessage.MessageUserReciver,
                        BuildPrivateMessageResponse(
                            privateMessage,
                            Array.Empty<MessageReaction>(),
                            null,
                            pollLookup.GetValueOrDefault(privateMessage.PrivateMessageID)));
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Connection closed", CancellationToken.None);
                }
            }
        }

        private static async Task SendPrivateSocketMessage(string targetUsername, object privateMessage)
        {
            if (ActiveSockets.TryGetValue(targetUsername, out var receiverSocket) &&
                receiverSocket.State == WebSocketState.Open)
            {
                var responseJson = JsonSerializer.Serialize(privateMessage);
                var messageBuffer = Encoding.UTF8.GetBytes(responseJson);
                try
                {
                    await receiverSocket.SendAsync(new ArraySegment<byte>(messageBuffer), WebSocketMessageType.Text, true, CancellationToken.None);
                }
                catch (WebSocketException)
                {
                    RemoveActiveSocket(targetUsername, receiverSocket);
                    receiverSocket.Abort();
                }
            }
        }

        private static async Task ReplaceActiveSocket(string username, WebSocket webSocket)
        {
            if (ActiveSockets.TryGetValue(username, out var previousSocket) &&
                !ReferenceEquals(previousSocket, webSocket) &&
                previousSocket.State == WebSocketState.Open)
            {
                try
                {
                    await previousSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Replaced by a newer connection",
                        CancellationToken.None);
                }
                catch (WebSocketException)
                {
                    previousSocket.Abort();
                }
            }

            ActiveSockets[username] = webSocket;
        }

        private static bool RemoveActiveSocket(string username, WebSocket webSocket)
        {
            return ((ICollection<KeyValuePair<string, WebSocket>>)ActiveSockets)
                .Remove(new KeyValuePair<string, WebSocket>(username, webSocket));
        }

        private async Task<IActionResult?> RejectSpamIfDetected(SpamDetectionRequest request)
        {
            if (_spamDetectionService == null)
            {
                return null;
            }

            var result = await _spamDetectionService.CheckAsync(request, HttpContext.RequestAborted);
            if (result.Allowed)
            {
                return null;
            }

            if (result.RetryAfterSeconds > 0)
            {
                Response.Headers.RetryAfter = result.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
            }

            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                message = result.Message,
                reason = result.ReasonCode,
                retryAfterSeconds = result.RetryAfterSeconds
            });
        }

        private async Task<bool> IsSpamDetected(SpamDetectionRequest request)
        {
            if (_spamDetectionService == null)
            {
                return false;
            }

            var result = await _spamDetectionService.CheckAsync(request, HttpContext.RequestAborted);
            return !result.Allowed;
        }

        private async Task DispatchPrivateEmailNotification(PrivateMessageFriend privateMessage)
        {
            if (_messageNotificationService != null)
            {
                if (_backgroundJobQueue?.TryQueue(async (services, cancellationToken) =>
                {
                    var notifications = services.GetRequiredService<IMessageNotificationService>();
                    await notifications.SendPrivateEmailNotificationAsync(
                        privateMessage.PrivateMessageID,
                        cancellationToken);
                }) == true)
                {
                    return;
                }

                await _messageNotificationService.SendPrivateEmailNotificationAsync(
                    privateMessage.PrivateMessageID,
                    HttpContext.RequestAborted);
                return;
            }

            await SendPrivateEmailNotification(privateMessage);
        }

        private async Task SendPrivateEmailNotification(PrivateMessageFriend privateMessage)
        {
            if (_emailNotificationSender == null)
            {
                return;
            }

            var recipient = await _context.Accounts.FirstOrDefaultAsync(account =>
                account.UserName == privateMessage.MessageUserReciver && !account.IsDisabled);
            if (recipient == null)
            {
                return;
            }

            try
            {
                await _emailNotificationSender.SendToAccountAsync(
                    recipient,
                    new EmailNotificationRequest(
                        privateMessage.MessagesUserSender,
                        $"New message from {privateMessage.MessagesUserSender}",
                        EmailNotificationPreferences.BuildPreview(
                            privateMessage.FriendMessagesData,
                            privateMessage.AttachmentUrl),
                        "dm",
                        privateMessage.MessagesUserSender,
                        BuildDmScopeId(privateMessage.MessagesUserSender, privateMessage.MessageUserReciver),
                        privateMessage.PrivateMessageID,
                        ParseDate(privateMessage.Date)),
                    HttpContext.RequestAborted);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"couldnt send private email notification: {ex.Message}");
            }
        }

        private async Task<bool> CanSendDm(string username, string targetUsername)
        {
            if (string.IsNullOrWhiteSpace(targetUsername) ||
                string.Equals(username, targetUsername, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var user = await _context.Accounts.FirstOrDefaultAsync(account => account.UserName == username && !account.IsDisabled);
            var target = await _context.Accounts.FirstOrDefaultAsync(account => account.UserName == targetUsername && !account.IsDisabled);

            if (user == null || target == null)
            {
                return false;
            }

            if (ContainsValue(user.BlockedUsers, targetUsername) ||
                ContainsValue(target.BlockedUsers, username))
            {
                return false;
            }

            var areFriends = ContainsValue(user.Friends, targetUsername);
            return target.PrivacyDmPolicy?.Trim().ToLowerInvariant() switch
            {
                "everyone" => true,
                "none" => false,
                _ => areFriends
            };
        }

        private static bool IsValidPrivateMessage(string? message, string? attachmentUrl, bool hasPoll = false)
        {
            return (!string.IsNullOrWhiteSpace(message) && message.Length <= 4000) ||
                   !string.IsNullOrWhiteSpace(attachmentUrl) ||
                   hasPoll;
        }

        private static bool ContainsValue(string[]? values, string value)
        {
            return values?.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase)) == true;
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

        private static Dictionary<string, PrivateMessageFriend> GetPrivateReplyPreviewLookup(
            IEnumerable<PrivateMessageFriend> page,
            IEnumerable<PrivateMessageFriend> conversationMessages)
        {
            var replyIds = page
                .Select(message => message.ReplyToMessageId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (replyIds.Count == 0)
            {
                return new Dictionary<string, PrivateMessageFriend>(StringComparer.OrdinalIgnoreCase);
            }

            return conversationMessages
                .Where(message => replyIds.Contains(message.PrivateMessageID))
                .GroupBy(message => message.PrivateMessageID, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        }

        private static PrivateMessageFriend? GetPrivateReplyPreview(
            PrivateMessageFriend message,
            IReadOnlyDictionary<string, PrivateMessageFriend> replyPreviews)
        {
            return !string.IsNullOrWhiteSpace(message.ReplyToMessageId) &&
                   replyPreviews.TryGetValue(message.ReplyToMessageId, out var preview)
                ? preview
                : null;
        }

        private static object? BuildPrivateReplyPreview(PrivateMessageFriend? message)
        {
            if (message == null)
            {
                return null;
            }

            return new
            {
                MessageId = message.PrivateMessageID,
                Sender = message.MessagesUserSender,
                Date = message.Date,
                Text = message.FriendMessagesData,
                message.AttachmentUrl,
                message.AttachmentContentType
            };
        }

        private static object BuildPrivateMessageResponse(
            PrivateMessageFriend message,
            IEnumerable<MessageReaction> reactions,
            PrivateMessageFriend? replyPreview = null,
            MessagePollResponse? poll = null)
        {
            return new
            {
                message.PrivateMessageID,
                message.FriendMessagesData,
                message.MessageUserReciver,
                message.MessagesUserSender,
                message.Date,
                message.ReplyToMessageId,
                message.AttachmentUrl,
                message.AttachmentContentType,
                message.EditedAt,
                ReplyPreview = BuildPrivateReplyPreview(replyPreview),
                Mentions = ExtractMentions(message.FriendMessagesData),
                Reactions = SummarizeReactions(reactions),
                Poll = poll
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

        private static string BuildDmScopeId(string left, string right)
        {
            var users = new[] { left, right }
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return string.Join(":", users);
        }

        private static DateTime ParseDate(string? date)
        {
            return DateTime.TryParse(date, out var dt) ? dt : DateTime.MinValue;
        }

        private static DateTime? ParseSearchBoundary(string? value, bool endOfDay)
        {
            if (string.IsNullOrWhiteSpace(value) || !DateTime.TryParse(value, out var parsed))
            {
                return null;
            }

            var trimmed = value.Trim();
            var hasTime = trimmed.Contains(':') || trimmed.Contains("T", StringComparison.OrdinalIgnoreCase);
            if (endOfDay && !hasTime)
            {
                return parsed.Date.AddDays(1).AddTicks(-1);
            }

            return parsed;
        }

        private static string NormalizeMentionFilter(string? value)
        {
            return value?.Trim().TrimStart('@') ?? string.Empty;
        }

        private static bool ContainsLink(string? text)
        {
            return !string.IsNullOrWhiteSpace(text) &&
                   System.Text.RegularExpressions.Regex.IsMatch(
                       text,
                       @"(?:https?://|www\.)\S+",
                       System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        private static bool MatchesAttachmentType(string? contentType, string? attachmentUrl, string? attachmentType)
        {
            var normalized = attachmentType?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalized) || normalized == "any")
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(attachmentUrl))
            {
                return false;
            }

            var mediaType = contentType?.Trim().ToLowerInvariant() ?? string.Empty;
            var isImage = mediaType.StartsWith("image/", StringComparison.Ordinal);
            var isVideo = mediaType.StartsWith("video/", StringComparison.Ordinal);
            var isAudio = mediaType.StartsWith("audio/", StringComparison.Ordinal);

            return normalized switch
            {
                "image" => isImage,
                "video" => isVideo,
                "audio" => isAudio,
                "file" => !isImage && !isVideo && !isAudio,
                _ => true
            };
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

        private static bool MentionsUser(string? text, string username)
        {
            var mentions = ExtractMentions(text);
            return mentions.Contains(username, StringComparer.OrdinalIgnoreCase) ||
                   mentions.Contains("everyone", StringComparer.OrdinalIgnoreCase) ||
                   mentions.Contains("here", StringComparer.OrdinalIgnoreCase);
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

    public class EditPrivateMessageRequest
    {
        public string MessageId { get; set; } = string.Empty;
        public string FriendMessagesData { get; set; } = string.Empty;
        public string? AttachmentUrl { get; set; }
        public string? AttachmentContentType { get; set; }
    }

    public class DeletePrivateMessageRequest
    {
        public string MessageId { get; set; } = string.Empty;
    }

    public class PrivateMessageReactionRequest
    {
        public string MessageId { get; set; } = string.Empty;
        public string Emoji { get; set; } = string.Empty;
    }

    public class DmMarkReadRequest
    {
        public string TargetUsername { get; set; } = string.Empty;
        public string? LastReadMessageId { get; set; }
        public DateTime? LastReadAt { get; set; }
    }
}
