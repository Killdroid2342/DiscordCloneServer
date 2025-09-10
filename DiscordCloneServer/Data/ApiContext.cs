using System;
using DiscordCloneServer.Models;
using Microsoft.EntityFrameworkCore;

namespace DiscordCloneServer.Data
{
    public class ApiContext : DbContext
    {
        public DbSet<Account> Accounts { get; set; }
        public DbSet<CreateServer> CreateServers { get; set; }
        public DbSet<ServerMessage> ServerMessages { get; set; }
        public DbSet<PrivateMessageFriend> PrivateMessageFriends { get; set; }
        public DbSet<ServerMember> ServerMembers { get; set; }

        public ApiContext(DbContextOptions<ApiContext> options)
            : base(options)
        {
            if (!Database.CanConnect())
            {
                Database.Migrate();
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Account>().ToTable("Accounts");
            modelBuilder.Entity<CreateServer>().ToTable("Create_Server");
            modelBuilder.Entity<ServerMessage>().ToTable("Server_Message");
            modelBuilder.Entity<PrivateMessageFriend>().ToTable("Private_Message_Friend");
            modelBuilder.Entity<ServerMember>().ToTable("Server_Members");

            base.OnModelCreating(modelBuilder);
            Console.WriteLine("this line was read");
        }
    }
}
