using System.ComponentModel.DataAnnotations;

namespace DiscordCloneServer.Models
{

    public class Account
    {
        [Key]
        public int Id { get; set; }
        public string UserName { get; set; }
        public string PassWord { get; set; }
        public string[]? Friends { get; set; }
        public string[]? IncomingFriendRequests { get; set; }
        public string[]? OutgoingFriendRequests { get; set; }

    }
}