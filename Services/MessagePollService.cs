using DiscordCloneServer.Data;
using DiscordCloneServer.Models;
using Microsoft.EntityFrameworkCore;

namespace DiscordCloneServer.Services
{
    public sealed record NormalizedMessagePollDraft(
        string Question,
        string[] Options,
        bool AllowMultiple,
        DateTime? ExpiresAt);

    public static class MessagePollService
    {
        private const int MaxQuestionLength = 280;
        private const int MaxOptionLength = 100;
        private const int MaxOptionCount = 10;

        public static bool TryNormalizeDraft(
            MessagePollDraft? draft,
            out NormalizedMessagePollDraft? normalized,
            out string error)
        {
            normalized = null;
            error = string.Empty;

            if (draft == null)
            {
                return true;
            }

            var question = draft.Question?.Trim() ?? string.Empty;
            if (question.Length is < 1 or > MaxQuestionLength)
            {
                error = $"Poll question must be 1-{MaxQuestionLength} characters.";
                return false;
            }

            var options = (draft.Options ?? Array.Empty<string>())
                .Select(option => option?.Trim() ?? string.Empty)
                .Where(option => !string.IsNullOrWhiteSpace(option))
                .ToArray();

            if (options.Length is < 2 or > MaxOptionCount)
            {
                error = $"Polls need 2-{MaxOptionCount} options.";
                return false;
            }

            if (options.Any(option => option.Length > MaxOptionLength))
            {
                error = $"Poll options must be {MaxOptionLength} characters or fewer.";
                return false;
            }

            if (options.Distinct(StringComparer.OrdinalIgnoreCase).Count() != options.Length)
            {
                error = "Poll options must be unique.";
                return false;
            }

            DateTime? expiresAt = null;
            if (draft.ExpiresAt.HasValue)
            {
                expiresAt = draft.ExpiresAt.Value.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(draft.ExpiresAt.Value, DateTimeKind.Utc)
                    : draft.ExpiresAt.Value.ToUniversalTime();

                if (expiresAt <= DateTime.UtcNow)
                {
                    error = "Poll expiration must be in the future.";
                    return false;
                }

                if (expiresAt > DateTime.UtcNow.AddDays(90))
                {
                    error = "Poll expiration can be at most 90 days away.";
                    return false;
                }
            }

            normalized = new NormalizedMessagePollDraft(question, options, draft.AllowMultiple, expiresAt);
            return true;
        }

        public static void AddPoll(
            ApiContext context,
            string scopeType,
            string messageId,
            string createdBy,
            NormalizedMessagePollDraft draft)
        {
            var pollId = Guid.NewGuid().ToString();
            context.MessagePolls.Add(new MessagePoll
            {
                Id = pollId,
                ScopeType = scopeType,
                MessageId = messageId,
                CreatedBy = createdBy,
                Question = draft.Question,
                AllowMultiple = draft.AllowMultiple,
                ExpiresAt = draft.ExpiresAt,
                CreatedAt = DateTime.UtcNow
            });

            for (var index = 0; index < draft.Options.Length; index += 1)
            {
                context.MessagePollOptions.Add(new MessagePollOption
                {
                    Id = Guid.NewGuid().ToString(),
                    PollId = pollId,
                    Text = draft.Options[index],
                    Position = index
                });
            }
        }

        public static async Task<Dictionary<string, MessagePollResponse>> GetPollLookupAsync(
            ApiContext context,
            string scopeType,
            IEnumerable<string> messageIds,
            string username,
            CancellationToken cancellationToken = default)
        {
            var ids = messageIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (ids.Length == 0)
            {
                return new Dictionary<string, MessagePollResponse>(StringComparer.OrdinalIgnoreCase);
            }

            var polls = await context.MessagePolls
                .Where(poll => poll.ScopeType == scopeType && ids.Contains(poll.MessageId))
                .ToListAsync(cancellationToken);

            if (polls.Count == 0)
            {
                return new Dictionary<string, MessagePollResponse>(StringComparer.OrdinalIgnoreCase);
            }

            var pollIds = polls.Select(poll => poll.Id).ToArray();
            var options = await context.MessagePollOptions
                .Where(option => pollIds.Contains(option.PollId))
                .OrderBy(option => option.Position)
                .ToListAsync(cancellationToken);
            var votes = await context.MessagePollVotes
                .Where(vote => pollIds.Contains(vote.PollId))
                .ToListAsync(cancellationToken);

            return polls.ToDictionary(
                poll => poll.MessageId,
                poll => BuildResponse(
                    poll,
                    options.Where(option => option.PollId == poll.Id),
                    votes.Where(vote => vote.PollId == poll.Id),
                    username),
                StringComparer.OrdinalIgnoreCase);
        }

        public static async Task<MessagePollResponse> GetPollResponseAsync(
            ApiContext context,
            MessagePoll poll,
            string username,
            CancellationToken cancellationToken = default)
        {
            var options = await context.MessagePollOptions
                .Where(option => option.PollId == poll.Id)
                .OrderBy(option => option.Position)
                .ToListAsync(cancellationToken);
            var votes = await context.MessagePollVotes
                .Where(vote => vote.PollId == poll.Id)
                .ToListAsync(cancellationToken);

            return BuildResponse(poll, options, votes, username);
        }

        public static async Task RemovePollsForMessagesAsync(
            ApiContext context,
            string scopeType,
            IEnumerable<string> messageIds,
            CancellationToken cancellationToken = default)
        {
            var ids = messageIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (ids.Length == 0)
            {
                return;
            }

            var polls = await context.MessagePolls
                .Where(poll => poll.ScopeType == scopeType && ids.Contains(poll.MessageId))
                .ToListAsync(cancellationToken);
            if (polls.Count == 0)
            {
                return;
            }

            var pollIds = polls.Select(poll => poll.Id).ToArray();
            var options = await context.MessagePollOptions
                .Where(option => pollIds.Contains(option.PollId))
                .ToListAsync(cancellationToken);
            var votes = await context.MessagePollVotes
                .Where(vote => pollIds.Contains(vote.PollId))
                .ToListAsync(cancellationToken);

            context.MessagePollVotes.RemoveRange(votes);
            context.MessagePollOptions.RemoveRange(options);
            context.MessagePolls.RemoveRange(polls);
        }

        private static MessagePollResponse BuildResponse(
            MessagePoll poll,
            IEnumerable<MessagePollOption> options,
            IEnumerable<MessagePollVote> votes,
            string username)
        {
            var voteList = votes.ToList();
            var selectedOptionIds = voteList
                .Where(vote => string.Equals(vote.Username, username, StringComparison.OrdinalIgnoreCase))
                .Select(vote => vote.OptionId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new MessagePollResponse
            {
                Id = poll.Id,
                ScopeType = poll.ScopeType,
                MessageId = poll.MessageId,
                CreatedBy = poll.CreatedBy,
                Question = poll.Question,
                AllowMultiple = poll.AllowMultiple,
                CreatedAt = poll.CreatedAt,
                ExpiresAt = poll.ExpiresAt,
                IsClosed = poll.ExpiresAt.HasValue && poll.ExpiresAt.Value <= DateTime.UtcNow,
                TotalVotes = voteList
                    .Select(vote => vote.Username)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count(),
                HasVoted = selectedOptionIds.Length > 0,
                SelectedOptionIds = selectedOptionIds,
                Options = options
                    .OrderBy(option => option.Position)
                    .Select(option => new MessagePollOptionResponse
                    {
                        Id = option.Id,
                        Text = option.Text,
                        Position = option.Position,
                        VoteCount = voteList
                            .Where(vote => vote.OptionId == option.Id)
                            .Select(vote => vote.Username)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Count(),
                        Selected = selectedOptionIds.Contains(option.Id, StringComparer.OrdinalIgnoreCase)
                    })
                    .ToArray()
            };
        }
    }
}
