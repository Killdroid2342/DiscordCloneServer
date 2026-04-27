using System.ComponentModel.DataAnnotations;

namespace DiscordCloneServer.Models
{
    public class ServerRole
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ServerId { get; set; } = string.Empty;
        public string Name { get; set; } = "member";
        public int Position { get; set; }
        public bool CanManageServer { get; set; }
        public bool CanManageChannels { get; set; }
        public bool CanManageMembers { get; set; }
        public bool CanBanMembers { get; set; }
        public bool CanCreateInvites { get; set; } = true;
        public bool CanSendMessages { get; set; } = true;
        public bool CanJoinVoice { get; set; } = true;
    }
}
