using System.ComponentModel.DataAnnotations;

namespace DiscordCloneServer.Models
{
    public class ServerThread
    {
        [Key]
        public string ThreadId { get; set; } = Guid.NewGuid().ToString();
        public string ServerId { get; set; } = string.Empty;
        public string ChannelId { get; set; } = string.Empty;
        public string ParentMessageId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string CreatedBy { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;
    }
}
