using System.Globalization;
using System.Text.RegularExpressions;
using DiscordCloneServer.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DiscordCloneServer.Services
{
    public sealed record SpamDetectionRequest(
        string ScopeType,
        string ScopeId,
        string SenderUsername,
        string MessageText,
        string? AttachmentUrl,
        string? RecipientUsername = null);

    public sealed record SpamDetectionResult(
        bool Allowed,
        string ReasonCode,
        string Message,
        int RetryAfterSeconds)
    {
        public static SpamDetectionResult Allow { get; } =
            new(true, "allowed", string.Empty, 0);
    }

    public interface ISpamDetectionService
    {
        Task<SpamDetectionResult> CheckAsync(
            SpamDetectionRequest request,
            CancellationToken cancellationToken = default);
    }

    public sealed class SpamDetectionService : ISpamDetectionService
    {
        private static readonly Regex LinkRegex =
            new(@"(?:https?://|www\.)\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex MentionRegex =
            new(@"@([A-Za-z0-9_.-]{3,32}|everyone|here)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex WordRegex =
            new(@"[A-Za-z0-9_.-]+", RegexOptions.Compiled);

        private readonly ApiContext _context;
        private readonly SpamDetectionOptions _options;

        public SpamDetectionService(
            ApiContext context,
            IOptions<SpamDetectionOptions> options)
        {
            _context = context;
            _options = options.Value;
        }

        public async Task<SpamDetectionResult> CheckAsync(
            SpamDetectionRequest request,
            CancellationToken cancellationToken = default)
        {
            if (!_options.Enabled)
            {
                return SpamDetectionResult.Allow;
            }

            var text = request.MessageText?.Trim() ?? string.Empty;
            var mentionCount = MentionRegex.Matches(text).Count;
            if (mentionCount > Math.Max(1, _options.MaxMentionsPerMessage))
            {
                return Block(
                    "mention-flood",
                    "That message mentions too many people at once. Please trim it down.",
                    Math.Max(15, _options.BurstWindowSeconds));
            }

            if (HasRepeatedCharacterRun(text, Math.Max(4, _options.MaxRepeatedCharacterRun)) ||
                HasRepeatedWordRun(text, Math.Max(4, _options.MaxRepeatedWordRun)))
            {
                return Block(
                    "repeated-content",
                    "That message looks repetitive. Please rewrite it before sending.",
                    Math.Max(15, _options.BurstWindowSeconds));
            }

            var now = DateTime.UtcNow;
            var lookbackSeconds = new[]
            {
                _options.BurstWindowSeconds,
                _options.DuplicateWindowSeconds,
                _options.LinkWindowSeconds
            }.Max();
            var lookbackCutoff = now.AddSeconds(-Math.Max(1, lookbackSeconds));
            var recentMessages = (await GetRecentMessagesAsync(request, cancellationToken))
                .Where(message => message.CreatedAt >= lookbackCutoff)
                .ToList();

            var burstCutoff = now.AddSeconds(-Math.Max(1, _options.BurstWindowSeconds));
            var burstCount = recentMessages.Count(message => message.CreatedAt >= burstCutoff) + 1;
            if (burstCount > Math.Max(1, _options.BurstMessageLimit))
            {
                return Block(
                    "message-burst",
                    "You are sending messages too quickly. Please slow down.",
                    Math.Max(1, _options.BurstWindowSeconds));
            }

            var contentKey = NormalizeContentKey(text, request.AttachmentUrl);
            if (!string.IsNullOrWhiteSpace(contentKey))
            {
                var duplicateCutoff = now.AddSeconds(-Math.Max(1, _options.DuplicateWindowSeconds));
                var duplicateCount = recentMessages.Count(message =>
                    message.CreatedAt >= duplicateCutoff &&
                    NormalizeContentKey(message.Text, message.AttachmentUrl) == contentKey) + 1;
                if (duplicateCount >= Math.Max(2, _options.DuplicateMessageLimit))
                {
                    return Block(
                        "duplicate-message",
                        "That message has been repeated too many times. Please wait before sending it again.",
                        Math.Max(1, _options.DuplicateWindowSeconds));
                }
            }

            var linkCutoff = now.AddSeconds(-Math.Max(1, _options.LinkWindowSeconds));
            var linkCount = recentMessages.Count(message =>
                message.CreatedAt >= linkCutoff &&
                ContainsLink(message.Text, message.AttachmentUrl)) +
                (ContainsLink(text, request.AttachmentUrl) ? 1 : 0);
            if (linkCount > Math.Max(1, _options.LinkMessageLimit))
            {
                return Block(
                    "link-burst",
                    "You are sharing links too quickly. Please wait before posting another link.",
                    Math.Max(1, _options.LinkWindowSeconds));
            }

            return SpamDetectionResult.Allow;
        }

        private async Task<IReadOnlyList<SpamMessageSnapshot>> GetRecentMessagesAsync(
            SpamDetectionRequest request,
            CancellationToken cancellationToken)
        {
            const int maxMessages = 100;
            var scopeType = request.ScopeType.Trim().ToLowerInvariant();
            var sender = request.SenderUsername.Trim();

            if (scopeType == "server")
            {
                var messages = await _context.ServerMessages
                    .Where(message => message.ChannelId == request.ScopeId && message.MessagesUserSender == sender)
                    .OrderByDescending(message => message.Date)
                    .Take(maxMessages)
                    .Select(message => new { Text = message.userText, message.AttachmentUrl, message.Date })
                    .ToListAsync(cancellationToken);

                return messages
                    .Select(message => new SpamMessageSnapshot(
                        message.Text,
                        message.AttachmentUrl,
                        ParseDate(message.Date)))
                    .ToList();
            }

            if (scopeType == "thread")
            {
                var messages = await _context.ServerThreadMessages
                    .Where(message => message.ThreadId == request.ScopeId && message.MessagesUserSender == sender)
                    .OrderByDescending(message => message.Date)
                    .Take(maxMessages)
                    .Select(message => new { Text = message.userText, message.AttachmentUrl, message.Date })
                    .ToListAsync(cancellationToken);

                return messages
                    .Select(message => new SpamMessageSnapshot(
                        message.Text,
                        message.AttachmentUrl,
                        ParseDate(message.Date)))
                    .ToList();
            }

            if (scopeType == "dm")
            {
                var recipient = request.RecipientUsername?.Trim();
                var query = _context.PrivateMessageFriends
                    .Where(message => message.MessagesUserSender == sender);
                if (!string.IsNullOrWhiteSpace(recipient))
                {
                    query = query.Where(message => message.MessageUserReciver == recipient);
                }

                var messages = await query
                    .OrderByDescending(message => message.Date)
                    .Take(maxMessages)
                    .Select(message => new { Text = message.FriendMessagesData, message.AttachmentUrl, message.Date })
                    .ToListAsync(cancellationToken);

                return messages
                    .Select(message => new SpamMessageSnapshot(
                        message.Text,
                        message.AttachmentUrl,
                        ParseDate(message.Date)))
                    .ToList();
            }

            if (scopeType == "group" && Guid.TryParse(request.ScopeId, out var groupId))
            {
                var messages = await _context.GroupMessages
                    .Where(message => message.GroupId == groupId && message.Sender == sender)
                    .OrderByDescending(message => message.Date)
                    .Take(maxMessages)
                    .Select(message => new { Text = message.Content, message.AttachmentUrl, message.Date })
                    .ToListAsync(cancellationToken);

                return messages
                    .Select(message => new SpamMessageSnapshot(
                        message.Text,
                        message.AttachmentUrl,
                        ParseDate(message.Date)))
                    .ToList();
            }

            return Array.Empty<SpamMessageSnapshot>();
        }

        private static SpamDetectionResult Block(string reasonCode, string message, int retryAfterSeconds)
        {
            return new SpamDetectionResult(false, reasonCode, message, retryAfterSeconds);
        }

        private static bool ContainsLink(string? text, string? attachmentUrl)
        {
            return LinkRegex.IsMatch(text ?? string.Empty) ||
                   (!string.IsNullOrWhiteSpace(attachmentUrl) &&
                    Uri.TryCreate(attachmentUrl, UriKind.Absolute, out var parsedUrl) &&
                    (parsedUrl.Scheme == Uri.UriSchemeHttp || parsedUrl.Scheme == Uri.UriSchemeHttps));
        }

        private static bool HasRepeatedCharacterRun(string text, int maxRunLength)
        {
            var current = '\0';
            var runLength = 0;
            foreach (var character in text)
            {
                if (char.IsWhiteSpace(character))
                {
                    current = '\0';
                    runLength = 0;
                    continue;
                }

                var normalized = char.ToUpperInvariant(character);
                if (normalized == current)
                {
                    runLength += 1;
                    if (runLength >= maxRunLength)
                    {
                        return true;
                    }
                }
                else
                {
                    current = normalized;
                    runLength = 1;
                }
            }

            return false;
        }

        private static bool HasRepeatedWordRun(string text, int maxRunLength)
        {
            var previous = string.Empty;
            var runLength = 0;
            foreach (Match match in WordRegex.Matches(text))
            {
                var word = match.Value.ToLowerInvariant();
                if (word == previous)
                {
                    runLength += 1;
                    if (runLength >= maxRunLength)
                    {
                        return true;
                    }
                }
                else
                {
                    previous = word;
                    runLength = 1;
                }
            }

            return false;
        }

        private static string NormalizeContentKey(string? text, string? attachmentUrl)
        {
            var normalizedText = NormalizeWhitespace(text).ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(normalizedText))
            {
                return normalizedText;
            }

            return NormalizeWhitespace(attachmentUrl).ToLowerInvariant();
        }

        private static string NormalizeWhitespace(string? value)
        {
            return string.Join(
                " ",
                (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        }

        private static DateTime ParseDate(string? value)
        {
            return DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed)
                ? parsed
                : DateTime.MinValue;
        }

        private sealed record SpamMessageSnapshot(
            string Text,
            string? AttachmentUrl,
            DateTime CreatedAt);
    }
}
