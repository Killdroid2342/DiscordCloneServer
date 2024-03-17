using Microsoft.EntityFrameworkCore;

namespace DiscordCloneServer
{
    public class Account
    {
        public string Id;
        public string UserName;
        public string Password;

    }

    public class DiscordContext : DbContext
    {
        public DiscordContext(DbContextOptions options) : base(options) { }
        public DiscordContext() : base()
        {
            var optionsBuilder = new DbContextOptionsBuilder<DiscordContext>();
            base.OnConfiguring(optionsBuilder);
        }
        public DbSet<Account> Accounts;
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseSqlServer("Data Source=DESKTOP-28PDSSI\\DiscordClone;Initial Catalog=DiscordClone;User ID=sa;Password=123456;Encrypt=False;TrustServerCertificate=True");
                optionsBuilder.UseSqlServer(Environment.GetEnvironmentVariable("DATABASE"));
            }
        }

    }
}
