using System.ComponentModel.DataAnnotations;

namespace DiscordCloneServer.Models
{
    public class ServerInviteUse
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ServerId { get; set; } = string.Empty;
        public string InviteId { get; set; } = string.Empty;
        public string InviteCode { get; set; } = string.Empty;
        public string JoinedUsername { get; set; } = string.Empty;
        public string? IpAddressHash { get; set; }
        public DateTime UsedAt { get; set; } = DateTime.UtcNow;
        public bool WasBlocked { get; set; }
        public string? ReasonCode { get; set; }
    }
}
