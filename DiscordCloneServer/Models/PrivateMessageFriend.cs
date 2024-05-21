using System.ComponentModel.DataAnnotations;

namespace DiscordCloneServer.Models
{
    public class PrivateMessageFriend
    {
        [Key]
        public string MessageId { get; set; }
        public string friendMessagesData { get; set; }
        public string MessageUserReciver { get; set; }
        public string MessagesUserSender { get; set; }
        public DateTime Date { get; set; }

    }
}
