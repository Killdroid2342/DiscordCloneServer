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
