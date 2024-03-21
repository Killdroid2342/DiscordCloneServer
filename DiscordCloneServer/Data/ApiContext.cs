using DiscordCloneServer.Models;
using Microsoft.EntityFrameworkCore;

namespace DiscordCloneServer.Data
{
    public class ApiContext : DbContext
    {
        public DbSet<Account> Accounts { get; set; }

        public ApiContext(DbContextOptions<ApiContext> options)
            : base(options)
        {
            if (!Database.CanConnect())
            {
                // Database does not exist, create it
                Database.Migrate();
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Specify the table name for the Account entity
            modelBuilder.Entity<Account>().ToTable("Accounts");
        }
    }
}
