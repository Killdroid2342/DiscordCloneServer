using System.ComponentModel.DataAnnotations;

namespace DiscordCloneServer.Models
{
    public class ServerMember
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string ServerId { get; set; }
        public string Username { get; set; }
        public string Role { get; set; } = "user";
    }
}
