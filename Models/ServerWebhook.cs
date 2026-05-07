using System.ComponentModel.DataAnnotations;

namespace DiscordCloneServer.Models
{
    public class ServerWebhook
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ServerId { get; set; } = string.Empty;
        public string ChannelId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
        public string TokenHash { get; set; } = string.Empty;
        public string CreatedBy { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
        public DateTime TokenLastRotatedAt { get; set; } = DateTime.UtcNow;
        public bool IsEnabled { get; set; } = true;
    }
}
