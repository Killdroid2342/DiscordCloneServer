using System.ComponentModel.DataAnnotations;

namespace DiscordCloneServer.Models
{
    public class ServerAuditLog
    {
        [Key]
        public string Id { get; set; } = string.Empty;
        public string ServerId { get; set; } = string.Empty;
        public string ActionType { get; set; } = string.Empty;
        public string ActorUsername { get; set; } = string.Empty;
        public string? TargetType { get; set; }
        public string? TargetId { get; set; }
        public string? TargetUsername { get; set; }
        public string? DetailsJson { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
