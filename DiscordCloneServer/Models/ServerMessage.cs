using System.ComponentModel.DataAnnotations;

namespace DiscordCloneServer.Models
{
    public class ServerMessage
    {
        [Key]
        public string MessageID { get; set; }
        public string ServerID { get; set; }
        public string ServerName { get; set; }
        public string MessagesUserSender { get; set; }
        public string Date { get; set; }
    }
}
