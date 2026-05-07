using System.ComponentModel.DataAnnotations;

using System.ComponentModel.DataAnnotations.Schema;

namespace DiscordCloneServer.Models
{
    public class PrivateMessageFriend
    {
        [Key]
        public string PrivateMessageID { get; set; } = Guid.NewGuid().ToString();
        public string FriendMessagesData { get; set; } = string.Empty;
        public string MessageUserReciver { get; set; } = string.Empty;
        public string MessagesUserSender { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public string? ReplyToMessageId { get; set; }
        public string? AttachmentUrl { get; set; }
        public string? AttachmentContentType { get; set; }
        public DateTime? EditedAt { get; set; }
        [NotMapped]
        public MessagePollDraft? Poll { get; set; }

    }
}
