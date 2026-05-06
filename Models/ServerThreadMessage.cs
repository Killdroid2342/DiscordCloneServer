using System.ComponentModel.DataAnnotations;

namespace DiscordCloneServer.Models
{
    public class ServerThreadMessage
    {
        [Key]
        public string ThreadMessageId { get; set; } = Guid.NewGuid().ToString();
        public string ThreadId { get; set; } = string.Empty;
        public string MessagesUserSender { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public string userText { get; set; } = string.Empty;
        public string? AttachmentUrl { get; set; }
        public string? AttachmentContentType { get; set; }
        public DateTime? EditedAt { get; set; }
    }
}
