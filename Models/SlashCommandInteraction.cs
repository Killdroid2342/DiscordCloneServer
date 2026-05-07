using System.ComponentModel.DataAnnotations;

namespace DiscordCloneServer.Models
{
    public class SlashCommandInteraction
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string SlashCommandId { get; set; } = string.Empty;
        public string ServerId { get; set; } = string.Empty;
        public string ChannelId { get; set; } = string.Empty;
        public string BotAccountId { get; set; } = string.Empty;
        public string CommandName { get; set; } = string.Empty;
        public string InvokedBy { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? AcknowledgedAt { get; set; }
        public DateTime? RespondedAt { get; set; }
        public string? ResponseMessageId { get; set; }
        public string Status { get; set; } = "pending";
    }
}
