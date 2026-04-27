using System.ComponentModel.DataAnnotations;

namespace DiscordCloneServer.Models
{
    public class UnreadState
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Username { get; set; } = string.Empty;
        public string ScopeType { get; set; } = string.Empty;
        public string ScopeId { get; set; } = string.Empty;
        public string? LastReadMessageId { get; set; }
        public DateTime LastReadAt { get; set; } = DateTime.MinValue;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
