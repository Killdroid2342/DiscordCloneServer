using System.ComponentModel.DataAnnotations;

namespace DiscordCloneServer.Models
{
    public class PrivateMessageFriend
    {
        [Key]
        public string PrivateMessageID { get; set; } = Guid.NewGuid().ToString();
        public string FriendMessagesData { get; set; }
        public string MessageUserReciver { get; set; }
        public string MessagesUserSender { get; set; }
        public string Date { get; set; }

    }
}
