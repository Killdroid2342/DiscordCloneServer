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
        public DbSet<GroupChat> GroupChats { get; set; }
        public DbSet<GroupMessage> GroupMessages { get; set; }
        public DbSet<AccountSession> AccountSessions { get; set; }
        public DbSet<ServerRole> ServerRoles { get; set; }
        public DbSet<ServerBan> ServerBans { get; set; }
        public DbSet<ServerInvite> ServerInvites { get; set; }
        public DbSet<MessageReaction> MessageReactions { get; set; }
        public DbSet<UnreadState> UnreadStates { get; set; }
        public DbSet<ContactVerification> ContactVerifications { get; set; }

        public ApiContext(DbContextOptions<ApiContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var stringArrayComparer = new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<string[]>(
                (c1, c2) => (c1 ?? Array.Empty<string>()).SequenceEqual(c2 ?? Array.Empty<string>()),
                c => (c ?? Array.Empty<string>()).Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                c => (c ?? Array.Empty<string>()).ToArray());

            modelBuilder.Entity<Account>().ToTable("Accounts");
            modelBuilder.Entity<AccountSession>().ToTable("Account_Sessions");
            modelBuilder.Entity<AccountSession>()
                .Property(session => session.RefreshTokenHash)
                .HasMaxLength(128);
            modelBuilder.Entity<AccountSession>()
                .Property(session => session.Username)
                .HasMaxLength(256);
            modelBuilder.Entity<AccountSession>()
                .HasIndex(session => session.RefreshTokenHash)
                .IsUnique();
            modelBuilder.Entity<AccountSession>()
                .HasIndex(session => new { session.AccountId, session.RevokedAt });
         
            modelBuilder.Entity<Account>()
                .Property(a => a.Friends)
                .HasConversion(
                    v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                    v => System.Text.Json.JsonSerializer.Deserialize<string[]>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? Array.Empty<string>()
                )
                .Metadata.SetValueComparer(stringArrayComparer);

            modelBuilder.Entity<Account>()
                .Property(a => a.IncomingFriendRequests)
                .HasConversion(
                    v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                    v => System.Text.Json.JsonSerializer.Deserialize<string[]>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? Array.Empty<string>()
                )
                .Metadata.SetValueComparer(stringArrayComparer);

            modelBuilder.Entity<Account>()
                .Property(a => a.OutgoingFriendRequests)
                .HasConversion(
                    v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                    v => System.Text.Json.JsonSerializer.Deserialize<string[]>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? Array.Empty<string>()
                )
                .Metadata.SetValueComparer(stringArrayComparer);
            modelBuilder.Entity<Account>()
                .Property(a => a.Groups)
                .HasConversion(
                    v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                    v => System.Text.Json.JsonSerializer.Deserialize<string[]>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? Array.Empty<string>()
                )
                .Metadata.SetValueComparer(stringArrayComparer);

            modelBuilder.Entity<Account>()
                .Property(a => a.BlockedUsers)
                .HasConversion(
                    v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                    v => System.Text.Json.JsonSerializer.Deserialize<string[]>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? Array.Empty<string>()
                )
                .Metadata.SetValueComparer(stringArrayComparer);

            modelBuilder.Entity<Account>()
                .Property(a => a.TwoFactorBackupCodeHashes)
                .HasConversion(
                    v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                    v => System.Text.Json.JsonSerializer.Deserialize<string[]>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? Array.Empty<string>()
                )
                .Metadata.SetValueComparer(stringArrayComparer);

            modelBuilder.Entity<Account>()
                .Property(a => a.Email)
                .HasMaxLength(256);
            modelBuilder.Entity<Account>()
                .Property(a => a.PhoneNumber)
                .HasMaxLength(32);
            modelBuilder.Entity<Account>()
                .Property(a => a.PresenceStatus)
                .HasMaxLength(32);
            modelBuilder.Entity<Account>()
                .Property(a => a.PrivacyDmPolicy)
                .HasMaxLength(32);
            modelBuilder.Entity<Account>()
                .Property(a => a.ProfileBannerColor)
                .HasMaxLength(16);
            modelBuilder.Entity<Account>()
                .Property(a => a.SettingsJson)
                .HasColumnType("nvarchar(max)");
            modelBuilder.Entity<Account>()
                .Property(a => a.VoiceChangerSettingsJson)
                .HasColumnType("nvarchar(max)");
            modelBuilder.Entity<Account>()
                .Property(a => a.AuthenticatorSecretProtected)
                .HasColumnType("nvarchar(max)");
            modelBuilder.Entity<Account>()
                .Property(a => a.TwoFactorLoginTicketHash)
                .HasMaxLength(128);

            modelBuilder.Entity<GroupChat>().ToTable("GroupChats");
            modelBuilder.Entity<GroupChat>()
                .Property(g => g.Members)
                .HasConversion(
                    v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                    v => System.Text.Json.JsonSerializer.Deserialize<string[]>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? Array.Empty<string>()
                )
                .Metadata.SetValueComparer(stringArrayComparer);
            modelBuilder.Entity<GroupMessage>().ToTable("GroupMessages");

            modelBuilder.Entity<CreateServer>().ToTable("Create_Server");
            modelBuilder.Entity<CreateServer>()
                .Property(server => server.VerificationLevel)
                .HasMaxLength(32);
            modelBuilder.Entity<CreateServer>()
                .Property(server => server.RequireVerifiedEmail)
                .HasDefaultValue(false);
            modelBuilder.Entity<CreateServer>()
                .Property(server => server.MinimumAccountAgeMinutes)
                .HasDefaultValue(0);
            modelBuilder.Entity<CreateServer>()
                .Property(server => server.MinimumMembershipMinutes)
                .HasDefaultValue(0);
            modelBuilder.Entity<CreateServer>()
                .Property(server => server.RequireTwoFactorForModerators)
                .HasDefaultValue(false);
            modelBuilder.Entity<ServerMessage>().ToTable("Server_Message");
            modelBuilder.Entity<PrivateMessageFriend>().ToTable("Private_Message_Friend");
            modelBuilder.Entity<ServerMember>().ToTable("Server_Members");
            modelBuilder.Entity<Channel>().ToTable("Channels");
            modelBuilder.Entity<Category>().ToTable("Categories");
            modelBuilder.Entity<ServerRole>().ToTable("Server_Roles");
            modelBuilder.Entity<ServerRole>()
                .HasIndex(role => new { role.ServerId, role.Name })
                .IsUnique();
            modelBuilder.Entity<ServerBan>().ToTable("Server_Bans");
            modelBuilder.Entity<ServerBan>()
                .HasIndex(ban => new { ban.ServerId, ban.Username })
                .IsUnique();
            modelBuilder.Entity<ServerInvite>().ToTable("Server_Invites");
            modelBuilder.Entity<ServerInvite>()
                .HasIndex(invite => invite.Code)
                .IsUnique();
            modelBuilder.Entity<MessageReaction>().ToTable("Message_Reactions");
            modelBuilder.Entity<MessageReaction>()
                .HasIndex(reaction => new { reaction.ScopeType, reaction.MessageId, reaction.Emoji, reaction.Username })
                .IsUnique();
            modelBuilder.Entity<UnreadState>().ToTable("Unread_States");
            modelBuilder.Entity<UnreadState>()
                .HasIndex(state => new { state.Username, state.ScopeType, state.ScopeId })
                .IsUnique();
            modelBuilder.Entity<ContactVerification>().ToTable("Contact_Verifications");
            modelBuilder.Entity<ContactVerification>()
                .Property(verification => verification.Username)
                .HasMaxLength(256);
            modelBuilder.Entity<ContactVerification>()
                .Property(verification => verification.Kind)
                .HasMaxLength(16);
            modelBuilder.Entity<ContactVerification>()
                .Property(verification => verification.Target)
                .HasMaxLength(256);
            modelBuilder.Entity<ContactVerification>()
                .Property(verification => verification.CodeHash)
                .HasMaxLength(128);
            modelBuilder.Entity<ContactVerification>()
                .HasIndex(verification => new { verification.Username, verification.Kind, verification.ConsumedAt });

            base.OnModelCreating(modelBuilder);
            Console.WriteLine("database ready");
        }
    }
}
