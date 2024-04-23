using System.ComponentModel.DataAnnotations;

namespace DiscordCloneServer.Models
{
    public class CreateServer
    {
        [Key]
        public string ServerName { get; set; }
        public string ServerID { get; set; }
        public string ServerOwner { get; set; }
        public DateTime Date { get; set; }
    }
}
