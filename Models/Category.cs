using System.ComponentModel.DataAnnotations;

namespace DiscordCloneServer.Models
{
    public class Category
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ServerId { get; set; }
        public string Name { get; set; }
    }
}
