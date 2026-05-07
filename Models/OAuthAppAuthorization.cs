using System.ComponentModel.DataAnnotations;

namespace DiscordCloneServer.Models
{
    public class OAuthAppAuthorization
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ApplicationId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string? ServerId { get; set; }
        public string ScopesJson { get; set; } = "[]";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastUsedAt { get; set; }
        public DateTime? RevokedAt { get; set; }
    }
}
