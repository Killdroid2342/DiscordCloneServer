using System.ComponentModel.DataAnnotations;

namespace DiscordCloneServer.Models
{
    public class Channel
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ServerId { get; set; } = string.Empty;
        public string? CategoryId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public int Position { get; set; }
    }
}
