using System.ComponentModel.DataAnnotations;

namespace DiscordCloneServer.Models
{
    public class ServerAutoModRule
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ServerId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string TriggerType { get; set; } = "keyword";
        public string TriggerValue { get; set; } = string.Empty;
        public string ActionType { get; set; } = "block_message";
        public bool IsEnabled { get; set; } = true;
        public string CreatedBy { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public int TimesTriggered { get; set; }
        public DateTime? LastTriggeredAt { get; set; }
    }
}
