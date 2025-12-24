using System.ComponentModel.DataAnnotations;

namespace DiscordCloneServer.Models
{
    public class GroupChat
    {
        [Key]
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Owner { get; set; }
        public string[] Members { get; set; }
    }
}
