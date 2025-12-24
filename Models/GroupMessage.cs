using System.ComponentModel.DataAnnotations;

namespace DiscordCloneServer.Models
{
    public class GroupMessage
    {
        [Key]
        public Guid Id { get; set; }
        public Guid GroupId { get; set; }
        public string Sender { get; set; }
        public string Content { get; set; }
        public string Date { get; set; }
    }
}
