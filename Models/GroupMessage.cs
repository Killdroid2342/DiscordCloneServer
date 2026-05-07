using System.ComponentModel.DataAnnotations;

using System.ComponentModel.DataAnnotations.Schema;

namespace DiscordCloneServer.Models
{
    public class GroupMessage
    {
        [Key]
        public Guid Id { get; set; }
        public Guid GroupId { get; set; }
        public string Sender { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public Guid? ReplyToMessageId { get; set; }
        public string? AttachmentUrl { get; set; }
        public string? AttachmentContentType { get; set; }
        public DateTime? EditedAt { get; set; }
        [NotMapped]
        public MessagePollDraft? Poll { get; set; }
    }
}
