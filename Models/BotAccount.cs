using System.ComponentModel.DataAnnotations;

namespace DiscordCloneServer.Models
{
    public class BotAccount
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ServerId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
        public string? Description { get; set; }
        public string Role { get; set; } = "user";
        public string TokenHash { get; set; } = string.Empty;
        public string CreatedBy { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
        public DateTime TokenLastRotatedAt { get; set; } = DateTime.UtcNow;
        public bool IsEnabled { get; set; } = true;
    }
}
