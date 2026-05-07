using System.ComponentModel.DataAnnotations;

using System.ComponentModel.DataAnnotations.Schema;

namespace DiscordCloneServer.Models
{
    public class ServerMessage
    {
        [Key]
        public string MessageID { get; set; } = Guid.NewGuid().ToString();
        public string ChannelId { get; set; } = string.Empty;
        public string MessagesUserSender { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public string userText { get; set; } = string.Empty;
        public string? ReplyToMessageId { get; set; }
        public string? AttachmentUrl { get; set; }
        public string? AttachmentContentType { get; set; }
        public DateTime? EditedAt { get; set; }
        public bool IsPinned { get; set; }
        public string? PinnedBy { get; set; }
        public DateTime? PinnedAt { get; set; }
        [NotMapped]
        public MessagePollDraft? Poll { get; set; }
    }
}
