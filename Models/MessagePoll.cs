using System.ComponentModel.DataAnnotations;

namespace DiscordCloneServer.Models
{
    public class MessagePoll
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ScopeType { get; set; } = string.Empty;
        public string MessageId { get; set; } = string.Empty;
        public string CreatedBy { get; set; } = string.Empty;
        public string Question { get; set; } = string.Empty;
        public bool AllowMultiple { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ExpiresAt { get; set; }
    }

    public class MessagePollOption
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string PollId { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public int Position { get; set; }
    }

    public class MessagePollVote
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string PollId { get; set; } = string.Empty;
        public string OptionId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class MessagePollDraft
    {
        public string Question { get; set; } = string.Empty;
        public string[] Options { get; set; } = Array.Empty<string>();
        public bool AllowMultiple { get; set; }
        public DateTime? ExpiresAt { get; set; }
    }

    public class MessagePollResponse
    {
        public string Id { get; set; } = string.Empty;
        public string ScopeType { get; set; } = string.Empty;
        public string MessageId { get; set; } = string.Empty;
        public string CreatedBy { get; set; } = string.Empty;
        public string Question { get; set; } = string.Empty;
        public bool AllowMultiple { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public bool IsClosed { get; set; }
        public int TotalVotes { get; set; }
        public bool HasVoted { get; set; }
        public string[] SelectedOptionIds { get; set; } = Array.Empty<string>();
        public MessagePollOptionResponse[] Options { get; set; } = Array.Empty<MessagePollOptionResponse>();
    }

    public class MessagePollOptionResponse
    {
        public string Id { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public int Position { get; set; }
        public int VoteCount { get; set; }
        public bool Selected { get; set; }
    }
}
