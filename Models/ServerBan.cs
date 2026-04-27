using System.ComponentModel.DataAnnotations;

namespace DiscordCloneServer.Models
{
    public class ServerBan
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ServerId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string BannedBy { get; set; } = string.Empty;
        public string? Reason { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
