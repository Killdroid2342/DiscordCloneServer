using System.ComponentModel.DataAnnotations;

namespace DiscordCloneServer.Models
{
    public class AccountSession
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public int AccountId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string RefreshTokenHash { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime ExpiresAt { get; set; }
        public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
        public DateTime? RevokedAt { get; set; }
        public string? ReplacedBySessionId { get; set; }
        public string? UserAgent { get; set; }
        public string? IpAddress { get; set; }
    }
}
