using DiscordCloneServer.Data;
using DiscordCloneServer.Models;
using DiscordCloneServer.Services;
using System.Globalization;
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
        private readonly IEmailNotificationSender? _emailNotificationSender;
        private readonly ISpamDetectionService? _spamDetectionService;
        private readonly IAutoModService? _autoModService;
        private readonly IBackgroundJobQueue? _backgroundJobQueue;
        private readonly IMessageNotificationService? _messageNotificationService;

        public ServerMessagesController(
            ApiContext context,
            IConfiguration config,
            IEmailNotificationSender? emailNotificationSender = null,
            ISpamDetectionService? spamDetectionService = null,
            IAutoModService? autoModService = null,
            IBackgroundJobQueue? backgroundJobQueue = null,
            IMessageNotificationService? messageNotificationService = null)
        {
            _context = context;
            _config = config;
            _emailNotificationSender = emailNotificationSender;
            _spamDetectionService = spamDetectionService;
            _autoModService = autoModService;
            _backgroundJobQueue = backgroundJobQueue;
            _messageNotificationService = messageNotificationService;
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
            if (!MessagePollService.TryNormalizeDraft(serverMessage.Poll, out var pollDraft, out var pollError))
                return BadRequest(new { message = pollError });

            if (string.IsNullOrWhiteSpace(serverMessage.ChannelId))
                return BadRequest(new { message = "Channel is required." });
            if (!IsValidMessageBody(serverMessage.userText, serverMessage.AttachmentUrl, pollDraft != null))
                return BadRequest(new { message = "Message must be 1-4000 characters, include an attachment, or include a poll." });
            if (!IsValidAttachment(serverMessage.AttachmentUrl))
                return BadRequest(new { message = "Attachment must be blank, an http URL, or an uploaded file URL." });

            var channel = await _context.Channels.FirstOrDefaultAsync(c => c.Id == serverMessage.ChannelId);
            if (channel == null)
                return NotFound(new { message = "Channel not found." });

            if (channel.Type != "text")
                return BadRequest(new { message = "Messages can only be sent to text channels." });

            if (!await CanViewChannel(channel, username))
                return Forbid();

            var communicationRestriction = await GetCommunicationRestriction(channel.ServerId, username);
            if (communicationRestriction != null)
                return StatusCode(StatusCodes.Status403Forbidden, new { message = communicationRestriction });

            if (!await CanSendMessages(channel, username))
                return Forbid();

            var verification = await GetServerVerificationResult(channel.ServerId, username);
            if (!verification.Allowed)
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    message = verification.Message,
                    verificationLevel = verification.Level
                });

            Models.ServerMessage? replyTarget = null;
            if (!string.IsNullOrWhiteSpace(serverMessage.ReplyToMessageId))
            {
                replyTarget = await _context.ServerMessages.FirstOrDefaultAsync(message =>
                    message.MessageID == serverMessage.ReplyToMessageId &&
                    message.ChannelId == serverMessage.ChannelId);
                if (replyTarget == null)
                    return BadRequest(new { message = "Reply target was not found in this channel." });
            }

            serverMessage.MessageID = string.IsNullOrWhiteSpace(serverMessage.MessageID)
                ? Guid.NewGuid().ToString()
                : serverMessage.MessageID.Trim();
            serverMessage.MessagesUserSender = username;
            serverMessage.Date = DateTime.UtcNow.ToString("O");

            if (await _context.ServerMessages.AnyAsync(message => message.MessageID == serverMessage.MessageID))
                return Conflict(new { message = "Duplicate message id." });

            var autoModBlock = await RejectAutoModIfDetected(new AutoModCheckRequest(
                channel.ServerId,
                "server_message",
                serverMessage.ChannelId,
                username,
                serverMessage.userText,
                serverMessage.AttachmentUrl));
            if (autoModBlock != null)
                return autoModBlock;

            var spamBlock = await RejectSpamIfDetected(new SpamDetectionRequest(
                "server",
                serverMessage.ChannelId,
                username,
                serverMessage.userText,
                serverMessage.AttachmentUrl));
            if (spamBlock != null)
                return spamBlock;

            _context.ServerMessages.Add(serverMessage);
            if (pollDraft != null)
            {
                MessagePollService.AddPoll(_context, "server", serverMessage.MessageID, username, pollDraft);
            }

            await _context.SaveChangesAsync();
            await DispatchServerMentionEmailNotifications(serverMessage, channel);
            var pollLookup = await MessagePollService.GetPollLookupAsync(
                _context,
                "server",
                new[] { serverMessage.MessageID },
                username,
                HttpContext.RequestAborted);
            return Ok(BuildMessageResponse(
                serverMessage,
                Array.Empty<MessageReaction>(),
                null,
                replyTarget,
                pollLookup.GetValueOrDefault(serverMessage.MessageID)));
        }

        [HttpGet]
        public async Task<IActionResult> GetServerMessages(
            string channelId,
            int take = 50,
            string? beforeMessageId = null,
            bool includePageInfo = false)
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
                if (!await CanViewChannel(channel, username))
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

                var totalCount = orderedMessages.Count;
                var boundaryCount = orderedMessages.Count;
                if (!string.IsNullOrWhiteSpace(beforeMessageId))
                {
                    var beforeIndex = orderedMessages.FindIndex(message => message.MessageID == beforeMessageId);
                    if (beforeIndex >= 0)
                    {
                        boundaryCount = beforeIndex;
                    }
                }

                var pageWindow = orderedMessages.Take(boundaryCount).ToList();
                var page = pageWindow.TakeLast(take).ToList();
                var messageIds = page.Select(message => message.MessageID).ToList();
                var reactions = await _context.MessageReactions
                    .Where(reaction => reaction.ScopeType == "server" && messageIds.Contains(reaction.MessageId))
                    .ToListAsync();
                var threadSummaries = await GetThreadSummariesForParentMessages(messageIds);
                var replyPreviews = await GetServerReplyPreviewLookup(page);
                var pollLookup = await MessagePollService.GetPollLookupAsync(
                    _context,
                    "server",
                    messageIds,
                    username,
                    HttpContext.RequestAborted);

                var responseMessages = page.Select(message => BuildMessageResponse(
                    message,
                    reactions.Where(reaction => reaction.MessageId == message.MessageID),
                    threadSummaries.GetValueOrDefault(message.MessageID),
                    GetServerReplyPreview(message, replyPreviews),
                    pollLookup.GetValueOrDefault(message.MessageID))).ToList();

                if (!includePageInfo)
                {
                    return Ok(responseMessages);
                }

                return Ok(new
                {
                    messages = responseMessages,
                    hasMore = boundaryCount > page.Count,
                    nextBeforeMessageId = page.FirstOrDefault()?.MessageID,
                    beforeMessageId,
                    pageSize = take,
                    returnedCount = page.Count,
                    totalCount
                });
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

            var channel = await _context.Channels.FirstOrDefaultAsync(c => c.Id == message.ChannelId);
            if (channel == null || !await CanViewChannel(channel, username))
                return Forbid();

            var nextText = request.UserText?.Trim() ?? string.Empty;
            var nextAttachmentUrl = NormalizeOptional(request.AttachmentUrl);
            var pollLookup = await MessagePollService.GetPollLookupAsync(
                _context,
                "server",
                new[] { message.MessageID },
                username,
                HttpContext.RequestAborted);
            if (!IsValidMessageBody(nextText, nextAttachmentUrl, pollLookup.ContainsKey(message.MessageID)))
                return BadRequest(new { message = "Message must be 1-4000 characters, include an attachment, or include a poll." });
            if (!IsValidAttachment(nextAttachmentUrl))
                return BadRequest(new { message = "Attachment must be blank, an http URL, or an uploaded file URL." });

            message.userText = nextText;
            message.AttachmentUrl = nextAttachmentUrl;
            message.AttachmentContentType = NormalizeOptional(request.AttachmentContentType);
            message.EditedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            var replyPreviews = await GetServerReplyPreviewLookup(new[] { message });
            return Ok(BuildMessageResponse(
                message,
                await GetReactionsForMessage("server", message.MessageID),
                null,
                GetServerReplyPreview(message, replyPreviews),
                pollLookup.GetValueOrDefault(message.MessageID)));
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

            if (channel == null || !await CanViewChannel(channel, username))
                return Forbid();

            if (message.MessagesUserSender != username && !canManage)
                return Forbid();

            var childThreads = await _context.ServerThreads
                .Where(thread => thread.ParentMessageId == message.MessageID)
                .ToListAsync();
            if (childThreads.Count > 0)
            {
                var childThreadIds = childThreads.Select(thread => thread.ThreadId).ToList();
                var childThreadMessages = await _context.ServerThreadMessages
                    .Where(threadMessage => childThreadIds.Contains(threadMessage.ThreadId))
                    .ToListAsync();
                _context.ServerThreadMessages.RemoveRange(childThreadMessages);
                _context.ServerThreads.RemoveRange(childThreads);
            }

            await MessagePollService.RemovePollsForMessagesAsync(
                _context,
                "server",
                new[] { message.MessageID },
                HttpContext.RequestAborted);
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
            string? after = null,
            string? before = null,
            bool? hasAttachment = null,
            string? attachmentType = null,
            bool? hasLink = null,
            bool? pinned = null,
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
            if (!await CanViewChannel(channel, username))
                return Forbid();

            query = query?.Trim() ?? string.Empty;
            fromUser = fromUser?.Trim();
            mentions = NormalizeMentionFilter(mentions);
            var afterDate = ParseSearchBoundary(after, endOfDay: false);
            var beforeDate = ParseSearchBoundary(before, endOfDay: true);
            take = Math.Clamp(take, 1, 100);

            var messages = await _context.ServerMessages
                .Where(message => message.ChannelId == channelId)
                .ToListAsync();

            var filtered = messages
                .Where(message => query == string.Empty ||
                                  message.userText.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                  message.MessagesUserSender.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                  (message.SenderDisplayName?.Contains(query, StringComparison.OrdinalIgnoreCase) == true))
                .Where(message => string.IsNullOrWhiteSpace(fromUser) ||
                                  string.Equals(message.MessagesUserSender, fromUser, StringComparison.OrdinalIgnoreCase))
                .Where(message => string.IsNullOrWhiteSpace(mentions) ||
                                  ExtractMentions(message.userText).Contains(mentions, StringComparer.OrdinalIgnoreCase))
                .Where(message => afterDate == null || ParseDate(message.Date) >= afterDate.Value)
                .Where(message => beforeDate == null || ParseDate(message.Date) <= beforeDate.Value)
                .Where(message => hasAttachment == null ||
                                  (!string.IsNullOrWhiteSpace(message.AttachmentUrl) == hasAttachment.Value))
                .Where(message => MatchesAttachmentType(
                    message.AttachmentContentType,
                    message.AttachmentUrl,
                    attachmentType))
                .Where(message => hasLink == null ||
                                  ContainsLink(message.userText) == hasLink.Value)
                .Where(message => pinned == null || message.IsPinned == pinned.Value)
                .OrderByDescending(message => ParseDate(message.Date))
                .Take(take)
                .ToList();

            var messageIds = filtered.Select(message => message.MessageID).ToList();
            var reactions = await _context.MessageReactions
                .Where(reaction => reaction.ScopeType == "server" && messageIds.Contains(reaction.MessageId))
                .ToListAsync();
            var replyPreviews = await GetServerReplyPreviewLookup(filtered);
            var pollLookup = await MessagePollService.GetPollLookupAsync(
                _context,
                "server",
                messageIds,
                username,
                HttpContext.RequestAborted);

            return Ok(filtered.Select(message => BuildMessageResponse(
                message,
                reactions.Where(reaction => reaction.MessageId == message.MessageID),
                null,
                GetServerReplyPreview(message, replyPreviews),
                pollLookup.GetValueOrDefault(message.MessageID))));
        }

        [HttpGet]
        public async Task<IActionResult> GetPinnedMessages(string channelId)
        {
            var username = User.GetUsername();
            if (string.IsNullOrWhiteSpace(username))
                return Unauthorized(new { message = "Missing user identity." });

            var channel = await _context.Channels.FirstOrDefaultAsync(c => c.Id == channelId);
            if (channel == null)
                return NotFound(new { message = "Channel not found." });
            if (!await IsServerMember(channel.ServerId, username))
                return Forbid();
            if (!await CanViewChannel(channel, username))
                return Forbid();

            var pinnedMessages = await _context.ServerMessages
                .Where(message => message.ChannelId == channelId && message.IsPinned)
                .OrderByDescending(message => message.PinnedAt ?? DateTime.MinValue)
                .ToListAsync();
            var messageIds = pinnedMessages.Select(message => message.MessageID).ToList();
            var reactions = await _context.MessageReactions
                .Where(reaction => reaction.ScopeType == "server" && messageIds.Contains(reaction.MessageId))
                .ToListAsync();
            var threadSummaries = await GetThreadSummariesForParentMessages(messageIds);
            var replyPreviews = await GetServerReplyPreviewLookup(pinnedMessages);
            var pollLookup = await MessagePollService.GetPollLookupAsync(
                _context,
                "server",
                messageIds,
                username,
                HttpContext.RequestAborted);

            return Ok(pinnedMessages.Select(message => BuildMessageResponse(
                message,
                reactions.Where(reaction => reaction.MessageId == message.MessageID),
                threadSummaries.GetValueOrDefault(message.MessageID),
                GetServerReplyPreview(message, replyPreviews),
                pollLookup.GetValueOrDefault(message.MessageID))));
        }

        [HttpPost]
        public async Task<IActionResult> SetPinnedMessage([FromBody] PinMessageRequest request)
        {
            var username = User.GetUsername();
            if (string.IsNullOrWhiteSpace(username))
                return Unauthorized(new { message = "Missing user identity." });

            var messageId = request.MessageId?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(messageId))
                return BadRequest(new { message = "Message id is required." });

            var message = await _context.ServerMessages.FirstOrDefaultAsync(m => m.MessageID == messageId);
            if (message == null)
                return NotFound(new { message = "Message not found." });

            var channel = await _context.Channels.FirstOrDefaultAsync(c => c.Id == message.ChannelId);
            if (channel == null || !await IsServerMember(channel.ServerId, username))
                return Forbid();
            if (!await CanViewChannel(channel, username))
                return Forbid();

            if (!await CanPinMessage(message, channel, username))
                return Forbid();

            if (request.IsPinned)
            {
                message.IsPinned = true;
                message.PinnedBy = username;
                message.PinnedAt ??= DateTime.UtcNow;
            }
            else
            {
                message.IsPinned = false;
                message.PinnedBy = null;
                message.PinnedAt = null;
            }

            await _context.SaveChangesAsync();
            var threadSummaries = await GetThreadSummariesForParentMessages(new[] { message.MessageID });
            var replyPreviews = await GetServerReplyPreviewLookup(new[] { message });
            var pollLookup = await MessagePollService.GetPollLookupAsync(
                _context,
                "server",
                new[] { message.MessageID },
                username,
                HttpContext.RequestAborted);
            return Ok(BuildMessageResponse(
                message,
                await GetReactionsForMessage("server", message.MessageID),
                threadSummaries.GetValueOrDefault(message.MessageID),
                GetServerReplyPreview(message, replyPreviews),
                pollLookup.GetValueOrDefault(message.MessageID)));
        }

        [HttpGet]
        public async Task<IActionResult> GetThreadsForChannel(string channelId)
        {
            var username = User.GetUsername();
            if (string.IsNullOrWhiteSpace(username))
                return Unauthorized(new { message = "Missing user identity." });

            var channel = await _context.Channels.FirstOrDefaultAsync(c => c.Id == channelId);
            if (channel == null)
                return NotFound(new { message = "Channel not found." });
            if (!await IsServerMember(channel.ServerId, username))
                return Forbid();
            if (!await CanViewChannel(channel, username))
                return Forbid();

            var threads = await _context.ServerThreads
                .Where(thread => thread.ChannelId == channelId)
                .OrderByDescending(thread => thread.LastActivityAt)
                .ToListAsync();
            var threadIds = threads.Select(thread => thread.ThreadId).ToList();
            var parentIds = threads.Select(thread => thread.ParentMessageId).ToList();
            var messageCounts = await GetThreadMessageCounts(threadIds);
            var parents = await _context.ServerMessages
                .Where(message => parentIds.Contains(message.MessageID))
                .ToDictionaryAsync(message => message.MessageID);

            return Ok(threads.Select(thread => BuildThreadResponse(
                thread,
                messageCounts.GetValueOrDefault(thread.ThreadId),
                parents.GetValueOrDefault(thread.ParentMessageId))));
        }

        [HttpPost]
        [EnableRateLimiting("abuse")]
        public async Task<IActionResult> CreateThread([FromBody] CreateThreadRequest request)
        {
            var username = User.GetUsername();
            if (string.IsNullOrWhiteSpace(username))
                return Unauthorized(new { message = "Missing user identity." });

            var parentMessageId = request.ParentMessageId?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(parentMessageId))
                return BadRequest(new { message = "Parent message id is required." });

            var parentMessage = await _context.ServerMessages.FirstOrDefaultAsync(m => m.MessageID == parentMessageId);
            if (parentMessage == null)
                return NotFound(new { message = "Parent message not found." });

            var channel = await _context.Channels.FirstOrDefaultAsync(c => c.Id == parentMessage.ChannelId);
            if (channel == null)
                return NotFound(new { message = "Channel not found." });
            if (channel.Type != "text")
                return BadRequest(new { message = "Threads can only be created in text channels." });
            if (!await IsServerMember(channel.ServerId, username))
                return Forbid();
            if (!await CanViewChannel(channel, username))
                return Forbid();

            var communicationRestriction = await GetCommunicationRestriction(channel.ServerId, username);
            if (communicationRestriction != null)
                return StatusCode(StatusCodes.Status403Forbidden, new { message = communicationRestriction });
            if (!await CanSendMessages(channel, username))
                return Forbid();

            var verification = await GetServerVerificationResult(channel.ServerId, username);
            if (!verification.Allowed)
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    message = verification.Message,
                    verificationLevel = verification.Level
                });

            var existingThread = await _context.ServerThreads
                .FirstOrDefaultAsync(thread => thread.ParentMessageId == parentMessageId);
            if (existingThread != null)
            {
                var existingCount = await _context.ServerThreadMessages.CountAsync(message =>
                    message.ThreadId == existingThread.ThreadId);
                return Ok(BuildThreadResponse(existingThread, existingCount, parentMessage));
            }

            var threadName = NormalizeThreadName(request.Name);
            if (threadName == null)
                return BadRequest(new { message = "Thread name must be 1-120 characters." });

            var now = DateTime.UtcNow;
            var thread = new ServerThread
            {
                ThreadId = string.IsNullOrWhiteSpace(request.ThreadId) ? Guid.NewGuid().ToString() : request.ThreadId.Trim(),
                ServerId = channel.ServerId,
                ChannelId = channel.Id,
                ParentMessageId = parentMessage.MessageID,
                Name = threadName,
                CreatedBy = username,
                CreatedAt = now,
                LastActivityAt = now
            };

            if (await _context.ServerThreads.AnyAsync(item => item.ThreadId == thread.ThreadId))
                return Conflict(new { message = "Duplicate thread id." });

            _context.ServerThreads.Add(thread);
            await _context.SaveChangesAsync();
            return Ok(BuildThreadResponse(thread, 0, parentMessage));
        }

        [HttpGet]
        public async Task<IActionResult> GetThread(string threadId)
        {
            var username = User.GetUsername();
            if (string.IsNullOrWhiteSpace(username))
                return Unauthorized(new { message = "Missing user identity." });

            var access = await GetThreadAccess(threadId, username);
            if (access.Error != null)
                return access.Error;

            var thread = access.Thread!;
            var count = await _context.ServerThreadMessages.CountAsync(message =>
                message.ThreadId == thread.ThreadId);
            var parent = await _context.ServerMessages.FirstOrDefaultAsync(message =>
                message.MessageID == thread.ParentMessageId);

            return Ok(BuildThreadResponse(thread, count, parent));
        }

        [HttpGet]
        public async Task<IActionResult> GetThreadMessages(
            string threadId,
            int take = 50,
            string? beforeMessageId = null,
            bool includePageInfo = false)
        {
            var username = User.GetUsername();
            if (string.IsNullOrWhiteSpace(username))
                return Unauthorized(new { message = "Missing user identity." });

            var access = await GetThreadAccess(threadId, username);
            if (access.Error != null)
                return access.Error;

            var thread = access.Thread!;
            take = Math.Clamp(take, 1, 100);
            var messages = await _context.ServerThreadMessages
                .Where(message => message.ThreadId == thread.ThreadId)
                .ToListAsync();

            var orderedMessages = messages
                .OrderBy(message => ParseDate(message.Date))
                .ToList();
            var totalCount = orderedMessages.Count;
            var boundaryCount = orderedMessages.Count;

            if (!string.IsNullOrWhiteSpace(beforeMessageId))
            {
                var beforeIndex = orderedMessages.FindIndex(message => message.ThreadMessageId == beforeMessageId);
                if (beforeIndex >= 0)
                {
                    boundaryCount = beforeIndex;
                }
            }

            var page = orderedMessages.Take(boundaryCount).TakeLast(take).ToList();
            var responseMessages = page.Select(BuildThreadMessageResponse).ToList();
            if (!includePageInfo)
            {
                return Ok(responseMessages);
            }

            return Ok(new
            {
                messages = responseMessages,
                hasMore = boundaryCount > page.Count,
                nextBeforeMessageId = page.FirstOrDefault()?.ThreadMessageId,
                beforeMessageId,
                pageSize = take,
                returnedCount = page.Count,
                totalCount
            });
        }

        [HttpPost]
        [EnableRateLimiting("abuse")]
        public async Task<IActionResult> SendThreadMessage([FromBody] SendThreadMessageRequest request)
        {
            var username = User.GetUsername();
            if (string.IsNullOrWhiteSpace(username))
                return Unauthorized(new { message = "Missing user identity." });

            var access = await GetThreadAccess(request.ThreadId, username);
            if (access.Error != null)
                return access.Error;

            var channel = access.Channel!;
            var communicationRestriction = await GetCommunicationRestriction(channel.ServerId, username);
            if (communicationRestriction != null)
                return StatusCode(StatusCodes.Status403Forbidden, new { message = communicationRestriction });
            if (!await CanSendMessages(channel, username))
                return Forbid();

            var verification = await GetServerVerificationResult(channel.ServerId, username);
            if (!verification.Allowed)
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    message = verification.Message,
                    verificationLevel = verification.Level
                });

            var text = request.UserText?.Trim() ?? string.Empty;
            var attachmentUrl = NormalizeOptional(request.AttachmentUrl);
            if (!IsValidMessageBody(text, attachmentUrl))
                return BadRequest(new { message = "Message must be 1-4000 characters or include an attachment." });
            if (!IsValidAttachment(attachmentUrl))
                return BadRequest(new { message = "Attachment must be blank, an http URL, or an uploaded file URL." });

            var now = DateTime.UtcNow;
            var threadMessage = new ServerThreadMessage
            {
                ThreadMessageId = string.IsNullOrWhiteSpace(request.ThreadMessageId)
                    ? Guid.NewGuid().ToString()
                    : request.ThreadMessageId.Trim(),
                ThreadId = access.Thread!.ThreadId,
                MessagesUserSender = username,
                Date = now.ToString("O"),
                userText = text,
                AttachmentUrl = attachmentUrl,
                AttachmentContentType = NormalizeOptional(request.AttachmentContentType)
            };

            if (await _context.ServerThreadMessages.AnyAsync(message =>
                    message.ThreadMessageId == threadMessage.ThreadMessageId))
                return Conflict(new { message = "Duplicate thread message id." });

            var autoModBlock = await RejectAutoModIfDetected(new AutoModCheckRequest(
                channel.ServerId,
                "thread_message",
                threadMessage.ThreadId,
                username,
                threadMessage.userText,
                threadMessage.AttachmentUrl));
            if (autoModBlock != null)
                return autoModBlock;

            var spamBlock = await RejectSpamIfDetected(new SpamDetectionRequest(
                "thread",
                threadMessage.ThreadId,
                username,
                threadMessage.userText,
                threadMessage.AttachmentUrl));
            if (spamBlock != null)
                return spamBlock;

            access.Thread!.LastActivityAt = now;
            _context.ServerThreadMessages.Add(threadMessage);
            await _context.SaveChangesAsync();
            return Ok(BuildThreadMessageResponse(threadMessage));
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
            if (!await CanViewChannel(channel, username))
                return Forbid();

            var communicationRestriction = await GetCommunicationRestriction(channel.ServerId, username);
            if (communicationRestriction != null)
                return StatusCode(StatusCodes.Status403Forbidden, new { message = communicationRestriction });

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
            if (!await CanViewChannel(channel, username))
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
            var visibleChannels = new List<Channel>();
            foreach (var channel in channels)
            {
                if (await CanViewChannel(channel, username))
                {
                    visibleChannels.Add(channel);
                }
            }
            var channelIds = visibleChannels.Select(channel => channel.Id).ToList();
            var states = await _context.UnreadStates
                .Where(state => state.Username == username &&
                                state.ScopeType == "server-channel" &&
                                channelIds.Contains(state.ScopeId))
                .ToListAsync();

            var response = visibleChannels.Select(channel =>
            {
                var state = states.FirstOrDefault(s => s.ScopeId == channel.Id);
                var lastRead = state?.LastReadAt ?? DateTime.MinValue;
                var unreadMessages = _context.ServerMessages
                    .Where(message => message.ChannelId == channel.Id && message.MessagesUserSender != username)
                    .AsEnumerable()
                    .Where(message => ParseDate(message.Date) > lastRead)
                    .ToList();

                return new
                {
                    channelId = channel.Id,
                    unread = unreadMessages.Count,
                    mentionCount = unreadMessages.Count(message => MentionsUser(message.userText, username)),
                    lastReadAt = state?.LastReadAt,
                    lastReadMessageId = state?.LastReadMessageId
                };
            });

            return Ok(response);
        }

        private sealed class ThreadSummaryData
        {
            public ServerThread Thread { get; init; } = new();
            public int MessageCount { get; init; }
        }

        private sealed class ThreadAccessResult
        {
            public ServerThread? Thread { get; init; }
            public Channel? Channel { get; init; }
            public IActionResult? Error { get; init; }
        }

        private async Task<Dictionary<string, ThreadSummaryData>> GetThreadSummariesForParentMessages(IEnumerable<string> parentMessageIds)
        {
            var parentIds = parentMessageIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToList();
            if (parentIds.Count == 0)
            {
                return new Dictionary<string, ThreadSummaryData>();
            }

            var threads = await _context.ServerThreads
                .Where(thread => parentIds.Contains(thread.ParentMessageId))
                .ToListAsync();
            var messageCounts = await GetThreadMessageCounts(threads.Select(thread => thread.ThreadId));

            return threads.ToDictionary(
                thread => thread.ParentMessageId,
                thread => new ThreadSummaryData
                {
                    Thread = thread,
                    MessageCount = messageCounts.GetValueOrDefault(thread.ThreadId)
                });
        }

        private async Task<Dictionary<string, int>> GetThreadMessageCounts(IEnumerable<string> threadIds)
        {
            var ids = threadIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToList();
            if (ids.Count == 0)
            {
                return new Dictionary<string, int>();
            }

            var matchingMessages = await _context.ServerThreadMessages
                .Where(message => ids.Contains(message.ThreadId))
                .ToListAsync();

            return matchingMessages
                .GroupBy(message => message.ThreadId)
                .ToDictionary(group => group.Key, group => group.Count());
        }

        private async Task<Dictionary<string, Models.ServerMessage>> GetServerReplyPreviewLookup(
            IEnumerable<Models.ServerMessage> messages)
        {
            var replyIds = messages
                .Select(message => message.ReplyToMessageId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (replyIds.Count == 0)
            {
                return new Dictionary<string, Models.ServerMessage>(StringComparer.OrdinalIgnoreCase);
            }

            var replyMessages = await _context.ServerMessages
                .Where(message => replyIds.Contains(message.MessageID))
                .ToListAsync();

            return replyMessages
                .GroupBy(message => message.MessageID, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        }

        private static Models.ServerMessage? GetServerReplyPreview(
            Models.ServerMessage message,
            IReadOnlyDictionary<string, Models.ServerMessage> replyPreviews)
        {
            return !string.IsNullOrWhiteSpace(message.ReplyToMessageId) &&
                   replyPreviews.TryGetValue(message.ReplyToMessageId, out var preview)
                ? preview
                : null;
        }

        private async Task<ThreadAccessResult> GetThreadAccess(string? threadId, string username)
        {
            var normalizedThreadId = threadId?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedThreadId))
            {
                return new ThreadAccessResult
                {
                    Error = BadRequest(new { message = "Thread id is required." })
                };
            }

            var thread = await _context.ServerThreads.FirstOrDefaultAsync(item => item.ThreadId == normalizedThreadId);
            if (thread == null)
            {
                return new ThreadAccessResult
                {
                    Error = NotFound(new { message = "Thread not found." })
                };
            }

            var channel = await _context.Channels.FirstOrDefaultAsync(item => item.Id == thread.ChannelId);
            if (channel == null)
            {
                return new ThreadAccessResult
                {
                    Error = NotFound(new { message = "Channel not found." })
                };
            }

            if (!await IsServerMember(channel.ServerId, username))
            {
                return new ThreadAccessResult
                {
                    Error = Forbid()
                };
            }

            if (!await CanViewChannel(channel, username))
            {
                return new ThreadAccessResult
                {
                    Error = Forbid()
                };
            }

            return new ThreadAccessResult
            {
                Thread = thread,
                Channel = channel
            };
        }

        private async Task<bool> CanPinMessage(ServerMessage message, Channel channel, string username)
        {
            if (string.Equals(message.MessagesUserSender, username, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var server = await _context.CreateServers.FirstOrDefaultAsync(s => s.ServerID == channel.ServerId);
            if (server?.ServerOwner == username)
            {
                return true;
            }

            var member = await _context.ServerMembers.FirstOrDefaultAsync(m =>
                m.ServerId == channel.ServerId && m.Username == username);
            if (member == null)
            {
                return false;
            }

            var roleName = member.Role?.Trim().ToLowerInvariant() ?? "user";
            if (roleName is "owner" or "admin" or "moderator")
            {
                return true;
            }

            var role = await _context.ServerRoles.FirstOrDefaultAsync(r => r.ServerId == channel.ServerId && r.Name == roleName);
            return role?.CanManageChannels ?? false;
        }

        private async Task<bool> IsServerMember(string serverId, string username)
        {
            return await _context.ServerMembers.AnyAsync(member =>
                member.ServerId == serverId && member.Username == username);
        }

        private async Task<(CreateServer? Server, ServerMember? Member, ServerRole? Role)> GetChannelPermissionContext(
            string serverId,
            string username)
        {
            var server = await _context.CreateServers.FirstOrDefaultAsync(s => s.ServerID == serverId);
            var member = await _context.ServerMembers.FirstOrDefaultAsync(m =>
                m.ServerId == serverId && m.Username == username);
            ServerRole? role = null;
            if (member != null)
            {
                var roleName = ChannelPermissionPolicy.NormalizeRoleName(member.Role);
                role = await _context.ServerRoles.FirstOrDefaultAsync(r =>
                    r.ServerId == serverId && r.Name == roleName);
            }

            return (server, member, role);
        }

        private async Task<bool> CanViewChannel(Channel channel, string username)
        {
            var (server, member, role) = await GetChannelPermissionContext(channel.ServerId, username);
            return ChannelPermissionPolicy.CanViewChannel(channel, server, member, role, username);
        }

        private async Task<bool> CanSendMessages(Channel channel, string username)
        {
            var (server, member, role) = await GetChannelPermissionContext(channel.ServerId, username);
            return ChannelPermissionPolicy.CanSendMessages(channel, server, member, role, username);
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

            if (IsMemberCommunicationRestricted(member, DateTime.UtcNow, includeMute: true))
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

        private async Task<string?> GetCommunicationRestriction(string serverId, string username)
        {
            var server = await _context.CreateServers.FirstOrDefaultAsync(s => s.ServerID == serverId);
            if (server?.ServerOwner == username)
            {
                return null;
            }

            var member = await _context.ServerMembers.FirstOrDefaultAsync(m =>
                m.ServerId == serverId && m.Username == username);
            if (member == null)
            {
                return null;
            }

            var now = DateTime.UtcNow;
            if (member.TimedOutUntil is { } timedOutUntil && timedOutUntil > now)
            {
                return $"You are timed out until {timedOutUntil:O}.";
            }

            if (member.IsMuted && (member.MutedUntil == null || member.MutedUntil > now))
            {
                return member.MutedUntil == null
                    ? "You are muted in this server."
                    : $"You are muted until {member.MutedUntil:O}.";
            }

            return null;
        }

        private static bool IsMemberCommunicationRestricted(ServerMember member, DateTime now, bool includeMute)
        {
            if (member.TimedOutUntil is { } timedOutUntil && timedOutUntil > now)
            {
                return true;
            }

            return includeMute && member.IsMuted && (member.MutedUntil == null || member.MutedUntil > now);
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

        private async Task<IActionResult?> RejectAutoModIfDetected(AutoModCheckRequest request)
        {
            if (_autoModService == null)
            {
                return null;
            }

            var result = await _autoModService.CheckAsync(request, HttpContext.RequestAborted);
            if (result.Allowed)
            {
                return null;
            }

            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                message = result.Message,
                reason = result.ReasonCode,
                ruleId = result.RuleId,
                ruleName = result.RuleName
            });
        }

        private async Task DispatchServerMentionEmailNotifications(
            Models.ServerMessage message,
            Channel channel)
        {
            if (_messageNotificationService != null)
            {
                if (_backgroundJobQueue?.TryQueue(async (services, cancellationToken) =>
                {
                    var notifications = services.GetRequiredService<IMessageNotificationService>();
                    await notifications.SendServerMentionEmailNotificationsAsync(message.MessageID, cancellationToken);
                }) == true)
                {
                    return;
                }

                await _messageNotificationService.SendServerMentionEmailNotificationsAsync(
                    message.MessageID,
                    HttpContext.RequestAborted);
                return;
            }

            await SendServerMentionEmailNotifications(message, channel);
        }

        private async Task SendServerMentionEmailNotifications(Models.ServerMessage message, Channel channel)
        {
            if (_emailNotificationSender == null)
            {
                return;
            }

            var mentionNames = ExtractMentions(message.userText)
                .Where(mention =>
                    !mention.Equals("everyone", StringComparison.OrdinalIgnoreCase) &&
                    !mention.Equals("here", StringComparison.OrdinalIgnoreCase) &&
                    !mention.Equals(message.MessagesUserSender, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (mentionNames.Length == 0)
            {
                return;
            }

            var serverMemberNames = await _context.ServerMembers
                .Where(member => member.ServerId == channel.ServerId)
                .Select(member => member.Username)
                .ToListAsync();
            var recipientNames = serverMemberNames
                .Where(member => mentionNames.Contains(member, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (recipientNames.Length == 0)
            {
                return;
            }

            var accounts = await _context.Accounts
                .Where(account => !account.IsDisabled)
                .ToListAsync();
            var recipients = accounts
                .Where(account => recipientNames.Contains(account.UserName, StringComparer.OrdinalIgnoreCase));

            foreach (var recipient in recipients)
            {
                try
                {
                    await _emailNotificationSender.SendToAccountAsync(
                        recipient,
                        new EmailNotificationRequest(
                            message.MessagesUserSender,
                            $"{message.MessagesUserSender} mentioned you in #{channel.Name}",
                            EmailNotificationPreferences.BuildPreview(message.userText, message.AttachmentUrl),
                            "server",
                            channel.Name,
                            channel.Id,
                            message.MessageID,
                            ParseDate(message.Date)),
                        HttpContext.RequestAborted);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"couldnt send server email notification: {ex.Message}");
                }
            }
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

        private static object BuildMessageResponse(
            Models.ServerMessage message,
            IEnumerable<MessageReaction> reactions,
            ThreadSummaryData? threadSummary = null,
            Models.ServerMessage? replyPreview = null,
            MessagePollResponse? poll = null)
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
                message.IsPinned,
                message.PinnedBy,
                message.PinnedAt,
                message.IsBot,
                message.BotAccountId,
                message.IsWebhook,
                message.WebhookId,
                message.SenderDisplayName,
                message.SenderAvatarUrl,
                ReplyPreview = BuildServerReplyPreview(replyPreview),
                Thread = threadSummary == null
                    ? null
                    : BuildThreadResponse(threadSummary.Thread, threadSummary.MessageCount, message),
                ThreadMessageCount = threadSummary?.MessageCount ?? 0,
                Mentions = ExtractMentions(message.userText),
                Reactions = SummarizeReactions(reactions),
                Poll = poll
            };
        }

        private static object? BuildServerReplyPreview(Models.ServerMessage? message)
        {
            if (message == null)
            {
                return null;
            }

            return new
            {
                MessageId = message.MessageID,
                Sender = message.MessagesUserSender,
                Date = message.Date,
                Text = message.userText,
                message.IsBot,
                message.BotAccountId,
                message.IsWebhook,
                message.WebhookId,
                message.SenderDisplayName,
                message.SenderAvatarUrl,
                message.AttachmentUrl,
                message.AttachmentContentType
            };
        }

        private static object BuildThreadResponse(ServerThread thread, int messageCount = 0, Models.ServerMessage? parentMessage = null)
        {
            return new
            {
                thread.ThreadId,
                thread.ServerId,
                thread.ChannelId,
                thread.ParentMessageId,
                thread.Name,
                thread.CreatedBy,
                thread.CreatedAt,
                thread.LastActivityAt,
                MessageCount = messageCount,
                ParentPreview = parentMessage == null
                    ? null
                    : new
                    {
                        parentMessage.MessageID,
                        parentMessage.MessagesUserSender,
                        parentMessage.Date,
                        parentMessage.userText
                    }
            };
        }

        private static object BuildThreadMessageResponse(ServerThreadMessage message)
        {
            return new
            {
                message.ThreadMessageId,
                message.ThreadId,
                message.MessagesUserSender,
                message.Date,
                message.userText,
                message.AttachmentUrl,
                message.AttachmentContentType,
                message.EditedAt,
                Mentions = ExtractMentions(message.userText)
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

        private static bool MentionsUser(string? text, string username)
        {
            var mentions = ExtractMentions(text);
            return mentions.Contains(username, StringComparer.OrdinalIgnoreCase) ||
                   mentions.Contains("everyone", StringComparer.OrdinalIgnoreCase) ||
                   mentions.Contains("here", StringComparer.OrdinalIgnoreCase);
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

        private static bool IsValidMessageBody(string? message, string? attachmentUrl, bool hasPoll = false)
        {
            return (!string.IsNullOrWhiteSpace(message) && message.Length <= 4000) ||
                   !string.IsNullOrWhiteSpace(attachmentUrl) ||
                   hasPoll;
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

        private static string? NormalizeThreadName(string? name)
        {
            var normalized = name?.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            return normalized.Length <= 120 ? normalized : normalized[..120];
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

    public class PinMessageRequest
    {
        public string MessageId { get; set; } = string.Empty;
        public bool IsPinned { get; set; }
    }

    public class CreateThreadRequest
    {
        public string? ThreadId { get; set; }
        public string ParentMessageId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    public class SendThreadMessageRequest
    {
        public string? ThreadMessageId { get; set; }
        public string ThreadId { get; set; } = string.Empty;
        public string UserText { get; set; } = string.Empty;
        public string? AttachmentUrl { get; set; }
        public string? AttachmentContentType { get; set; }
    }
}
