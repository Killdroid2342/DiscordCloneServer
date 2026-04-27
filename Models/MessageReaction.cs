using System.ComponentModel.DataAnnotations;

namespace DiscordCloneServer.Models
{
    public class MessageReaction
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ScopeType { get; set; } = string.Empty;
        public string MessageId { get; set; } = string.Empty;
        public string Emoji { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
