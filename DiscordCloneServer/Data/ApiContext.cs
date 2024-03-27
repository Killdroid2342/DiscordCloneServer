using System;
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


            modelBuilder.Entity<Account>().ToTable("Accounts");
            base.OnModelCreating(modelBuilder);
            Console.WriteLine("this line was read");


        }

    }
}
