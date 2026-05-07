using System.ComponentModel.DataAnnotations;

namespace DiscordCloneServer.Models
{
    public class OAuthApplication
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? IconUrl { get; set; }
        public string OwnerUsername { get; set; } = string.Empty;
        public string ClientSecretHash { get; set; } = string.Empty;
        public string RedirectUrisJson { get; set; } = "[]";
        public string AllowedScopesJson { get; set; } = "[]";
        public string? BotAccountId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
        public DateTime SecretLastRotatedAt { get; set; } = DateTime.UtcNow;
        public bool IsEnabled { get; set; } = true;
    }
}
