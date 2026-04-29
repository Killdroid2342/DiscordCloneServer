using System.ComponentModel.DataAnnotations;

namespace DiscordCloneServer.Models
{
    public class ContactVerification
    {
        [Key]
        public string Id { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
        public string CodeHash { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ConsumedAt { get; set; }
    }
}
