using System.ComponentModel.DataAnnotations;

namespace DiscordCloneServer.Models
{
    public class UserReport
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ScopeType { get; set; } = "account";
        public string TargetType { get; set; } = "user";
        public string? ServerId { get; set; }
        public string? ChannelId { get; set; }
        public string? GroupId { get; set; }
        public string? MessageId { get; set; }
        public string? TargetUsername { get; set; }
        public string ReportedByUsername { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? MessagePreview { get; set; }
        public bool ReporterBlockedTarget { get; set; }
        public string Status { get; set; } = "open";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? ReviewedByUsername { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public string? ResolutionNote { get; set; }
    }
}
