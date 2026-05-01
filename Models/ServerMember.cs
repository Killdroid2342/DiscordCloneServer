using System.ComponentModel.DataAnnotations;

namespace DiscordCloneServer.Models
{
    public class ServerMember
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string ServerId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Role { get; set; } = "user";
        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
        public bool IsMuted { get; set; }
        public DateTime? MutedUntil { get; set; }
        public DateTime? TimedOutUntil { get; set; }
    }
}
