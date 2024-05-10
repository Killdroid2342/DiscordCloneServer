namespace DiscordCloneServer.Models
{

    public class Account
    {
        public int Id { get; set; }
        public string UserName { get; set; }
        public string PassWord { get; set; }
        public string[] Following { get; set; }
        public string[] Followers { get; set; }

    }
}
