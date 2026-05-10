using System.ComponentModel.DataAnnotations;

namespace DiscordCloneServer.Models
{
    public class ServerInvite
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ServerId { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string CreatedBy { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ExpiresAt { get; set; }
        public int? MaxUses { get; set; }
        public int Uses { get; set; }
        public DateTime? LastUsedAt { get; set; }
        public DateTime? AbuseDetectedAt { get; set; }
        public string? AbuseReason { get; set; }
        public DateTime? RevokedAt { get; set; }
    }
}
