using System.ComponentModel.DataAnnotations;

namespace DiscordCloneServer.Models
{
    public class OAuthAuthorizationCode
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ApplicationId { get; set; } = string.Empty;
        public string AuthorizationId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string? ServerId { get; set; }
        public string CodeHash { get; set; } = string.Empty;
        public string RedirectUri { get; set; } = string.Empty;
        public string ScopesJson { get; set; } = "[]";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddMinutes(10);
        public DateTime? ConsumedAt { get; set; }
    }
}
