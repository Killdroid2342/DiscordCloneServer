using System.Text.Json;
using System.Text.RegularExpressions;
using DiscordCloneServer.Data;
using DiscordCloneServer.Models;
using Microsoft.EntityFrameworkCore;

namespace DiscordCloneServer.Services
{
    public sealed record AutoModCheckRequest(
        string ServerId,
        string ScopeType,
        string ScopeId,
        string SenderUsername,
        string MessageText,
        string? AttachmentUrl);

    public sealed record AutoModCheckResult(
        bool Allowed,
        string ReasonCode,
        string Message,
        string? RuleId = null,
        string? RuleName = null)
    {
        public static AutoModCheckResult Allow { get; } =
            new(true, "allowed", string.Empty);
    }

    public interface IAutoModService
    {
        Task<AutoModCheckResult> CheckAsync(
            AutoModCheckRequest request,
            CancellationToken cancellationToken = default);
    }

    public sealed class AutoModService : IAutoModService
    {
        public const string TriggerKeyword = "keyword";
        public const string TriggerInviteLink = "invite_link";
        public const string TriggerMentionSpam = "mention_spam";
        public const string TriggerLink = "link";
        public const string ActionBlockMessage = "block_message";
        public const string ActionFlag = "flag";

        private static readonly Regex InviteRegex =
            new(@"(?:discord(?:app)?\.com/invite|discord\.gg|/invite/)\S+",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex LinkRegex =
            new(@"(?:https?://|www\.)\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex MentionRegex =
            new(@"@([A-Za-z0-9_.-]{3,32}|everyone|here)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly ApiContext _context;

        public AutoModService(ApiContext context)
        {
            _context = context;
        }

        public async Task<AutoModCheckResult> CheckAsync(
            AutoModCheckRequest request,
            CancellationToken cancellationToken = default)
        {
            var rules = await _context.ServerAutoModRules
                .Where(rule => rule.ServerId == request.ServerId && rule.IsEnabled)
                .OrderBy(rule => rule.CreatedAt)
                .ToListAsync(cancellationToken);

            foreach (var rule in rules)
            {
                if (!MatchesRule(rule, request, out var reasonCode))
                {
                    continue;
                }

                var now = DateTime.UtcNow;
                var actionType = NormalizeActionType(rule.ActionType);
                rule.TimesTriggered += 1;
                rule.LastTriggeredAt = now;
                rule.UpdatedAt = now;

                _context.ServerAuditLogs.Add(new ServerAuditLog
                {
                    Id = Guid.NewGuid().ToString(),
                    ServerId = request.ServerId,
                    ActionType = actionType == ActionFlag
                        ? "automod_message_flagged"
                        : "automod_message_blocked",
                    ActorUsername = "automod",
                    TargetType = request.ScopeType,
                    TargetId = request.ScopeId,
                    TargetUsername = request.SenderUsername,
                    DetailsJson = JsonSerializer.Serialize(new
                    {
                        RuleId = rule.Id,
                        RuleName = rule.Name,
                        rule.TriggerType,
                        ReasonCode = reasonCode
                    }),
                    CreatedAt = now
                });

                await _context.SaveChangesAsync(cancellationToken);

                if (actionType == ActionFlag)
                {
                    continue;
                }

                return new AutoModCheckResult(
                    false,
                    reasonCode,
                    $"AutoMod blocked this message because it matched \"{rule.Name}\".",
                    rule.Id,
                    rule.Name);
            }

            return AutoModCheckResult.Allow;
        }

        public static string NormalizeTriggerType(string? value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                TriggerInviteLink => TriggerInviteLink,
                TriggerMentionSpam => TriggerMentionSpam,
                TriggerLink => TriggerLink,
                _ => TriggerKeyword
            };
        }

        public static string NormalizeActionType(string? value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant() == ActionFlag
                ? ActionFlag
                : ActionBlockMessage;
        }

        public static bool IsKnownActionType(string? value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant() is ActionBlockMessage or ActionFlag;
        }

        public static bool IsKnownTriggerType(string? value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant() is
                TriggerKeyword or TriggerInviteLink or TriggerMentionSpam or TriggerLink;
        }

        private static bool MatchesRule(
            ServerAutoModRule rule,
            AutoModCheckRequest request,
            out string reasonCode)
        {
            var text = request.MessageText ?? string.Empty;
            var triggerType = NormalizeTriggerType(rule.TriggerType);
            reasonCode = $"automod-{triggerType}";

            return triggerType switch
            {
                TriggerInviteLink => InviteRegex.IsMatch(text),
                TriggerMentionSpam => CountMentions(text) > ParseMentionLimit(rule.TriggerValue),
                TriggerLink => ContainsLink(text, request.AttachmentUrl),
                _ => MatchesKeywordRule(text, rule.TriggerValue)
            };
        }

        private static bool MatchesKeywordRule(string text, string triggerValue)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            return ParseKeywordTerms(triggerValue).Any(term =>
                text.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        private static IReadOnlyList<string> ParseKeywordTerms(string? triggerValue)
        {
            return (triggerValue ?? string.Empty)
                .Split(new[] { '\n', '\r', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(term => term.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static int ParseMentionLimit(string? triggerValue)
        {
            return int.TryParse(triggerValue, out var limit)
                ? Math.Clamp(limit, 1, 100)
                : 5;
        }

        private static int CountMentions(string text)
        {
            return MentionRegex.Matches(text ?? string.Empty).Count;
        }

        private static bool ContainsLink(string text, string? attachmentUrl)
        {
            return LinkRegex.IsMatch(text ?? string.Empty) ||
                   (!string.IsNullOrWhiteSpace(attachmentUrl) &&
                    Uri.TryCreate(attachmentUrl, UriKind.Absolute, out var parsedUrl) &&
                    (parsedUrl.Scheme == Uri.UriSchemeHttp || parsedUrl.Scheme == Uri.UriSchemeHttps));
        }
    }
}
