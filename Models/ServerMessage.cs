using System.ComponentModel.DataAnnotations;

namespace DiscordCloneServer.Models
{
    public class ServerMessage
    {
        [Key]
        public string MessageID { get; set; }
        public string ChannelId { get; set; }
        public string MessagesUserSender { get; set; }
        public string Date { get; set; }
        public string userText { get; set; }
    }
}
