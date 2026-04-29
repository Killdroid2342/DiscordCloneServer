using System.ComponentModel.DataAnnotations;

namespace DiscordCloneServer.Models
{
    public class GroupChat
    {
        [Key]
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Owner { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
        public string[] Members { get; set; } = Array.Empty<string>();
    }
}
