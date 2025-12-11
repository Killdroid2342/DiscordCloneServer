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
        public DbSet<Channel> Channels { get; set; }
        public DbSet<Category> Categories { get; set; }

        public ApiContext(DbContextOptions<ApiContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Account>().ToTable("Accounts");
         
            modelBuilder.Entity<Account>()
                .Property(a => a.Friends)
                .HasConversion(
                    v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions)null),
                    v => System.Text.Json.JsonSerializer.Deserialize<string[]>(v, (System.Text.Json.JsonSerializerOptions)null)
                );
            modelBuilder.Entity<CreateServer>().ToTable("Create_Server");
            modelBuilder.Entity<ServerMessage>().ToTable("Server_Message");
            modelBuilder.Entity<PrivateMessageFriend>().ToTable("Private_Message_Friend");
            modelBuilder.Entity<ServerMember>().ToTable("Server_Members");
            modelBuilder.Entity<Channel>().ToTable("Channels");
            modelBuilder.Entity<Category>().ToTable("Categories");

            base.OnModelCreating(modelBuilder);
            Console.WriteLine("database ready");
        }
    }
}
