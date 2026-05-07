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
        public DbSet<ServerThread> ServerThreads { get; set; }
        public DbSet<ServerThreadMessage> ServerThreadMessages { get; set; }
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
        public DbSet<ServerEmoji> ServerEmojis { get; set; }
        public DbSet<ServerSticker> ServerStickers { get; set; }
        public DbSet<MessageReaction> MessageReactions { get; set; }
        public DbSet<MessagePoll> MessagePolls { get; set; }
        public DbSet<MessagePollOption> MessagePollOptions { get; set; }
        public DbSet<MessagePollVote> MessagePollVotes { get; set; }
        public DbSet<UnreadState> UnreadStates { get; set; }
        public DbSet<ContactVerification> ContactVerifications { get; set; }
        public DbSet<ServerAuditLog> ServerAuditLogs { get; set; }
        public DbSet<UserReport> UserReports { get; set; }
        public DbSet<BotAccount> BotAccounts { get; set; }
        public DbSet<ServerWebhook> ServerWebhooks { get; set; }
        public DbSet<SlashCommand> SlashCommands { get; set; }
        public DbSet<SlashCommandInteraction> SlashCommandInteractions { get; set; }
        public DbSet<OAuthApplication> OAuthApplications { get; set; }
        public DbSet<OAuthAppAuthorization> OAuthAppAuthorizations { get; set; }
        public DbSet<OAuthAuthorizationCode> OAuthAuthorizationCodes { get; set; }
        public DbSet<OAuthAccessToken> OAuthAccessTokens { get; set; }

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
                .Property(a => a.ActivityStatus)
                .HasMaxLength(120)
                .HasDefaultValue("");
            modelBuilder.Entity<Account>()
                .Property(a => a.AccountStanding)
                .HasMaxLength(32)
                .HasDefaultValue("good");
            modelBuilder.Entity<Account>()
                .Property(a => a.TrustScore)
                .HasDefaultValue(60);
            modelBuilder.Entity<Account>()
                .Property(a => a.StandingReason)
                .HasMaxLength(300);
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
                .Property(server => server.IsPublic)
                .HasDefaultValue(false);
            modelBuilder.Entity<CreateServer>()
                .Property(server => server.DiscoveryCategory)
                .HasMaxLength(64);
            modelBuilder.Entity<CreateServer>()
                .Property(server => server.DiscoveryTagsJson)
                .HasColumnType("nvarchar(max)");
            modelBuilder.Entity<CreateServer>()
                .Property(server => server.ServerIconUrl)
                .HasMaxLength(2048);
            modelBuilder.Entity<CreateServer>()
                .Property(server => server.ServerBannerUrl)
                .HasMaxLength(2048);
            modelBuilder.Entity<CreateServer>()
                .Property(server => server.WelcomeEnabled)
                .HasDefaultValue(true);
            modelBuilder.Entity<CreateServer>()
                .Property(server => server.WelcomeMessage)
                .HasMaxLength(600);
            modelBuilder.Entity<CreateServer>()
                .Property(server => server.WelcomeChecklistJson)
                .HasColumnType("nvarchar(max)");
            modelBuilder.Entity<CreateServer>()
                .HasIndex(server => new { server.IsPublic, server.DiscoveryCategory });
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
            modelBuilder.Entity<ServerMessage>()
                .Property(message => message.IsPinned)
                .HasDefaultValue(false);
            modelBuilder.Entity<ServerMessage>()
                .Property(message => message.PinnedBy)
                .HasMaxLength(256);
            modelBuilder.Entity<ServerMessage>()
                .Property(message => message.BotAccountId)
                .HasMaxLength(128);
            modelBuilder.Entity<ServerMessage>()
                .Property(message => message.WebhookId)
                .HasMaxLength(128);
            modelBuilder.Entity<ServerMessage>()
                .Property(message => message.SenderDisplayName)
                .HasMaxLength(80);
            modelBuilder.Entity<ServerMessage>()
                .Property(message => message.SenderAvatarUrl)
                .HasMaxLength(2048);
            modelBuilder.Entity<ServerThread>().ToTable("Server_Threads");
            modelBuilder.Entity<ServerThread>()
                .Property(thread => thread.ServerId)
                .HasMaxLength(128);
            modelBuilder.Entity<ServerThread>()
                .Property(thread => thread.ChannelId)
                .HasMaxLength(128);
            modelBuilder.Entity<ServerThread>()
                .Property(thread => thread.ParentMessageId)
                .HasMaxLength(128);
            modelBuilder.Entity<ServerThread>()
                .Property(thread => thread.Name)
                .HasMaxLength(120);
            modelBuilder.Entity<ServerThread>()
                .Property(thread => thread.CreatedBy)
                .HasMaxLength(256);
            modelBuilder.Entity<ServerThread>()
                .HasIndex(thread => thread.ParentMessageId)
                .IsUnique();
            modelBuilder.Entity<ServerThread>()
                .HasIndex(thread => new { thread.ChannelId, thread.LastActivityAt });
            modelBuilder.Entity<ServerThreadMessage>().ToTable("Server_Thread_Messages");
            modelBuilder.Entity<ServerThreadMessage>()
                .Property(message => message.ThreadId)
                .HasMaxLength(128);
            modelBuilder.Entity<ServerThreadMessage>()
                .Property(message => message.MessagesUserSender)
                .HasMaxLength(256);
            modelBuilder.Entity<ServerThreadMessage>()
                .HasIndex(message => message.ThreadId);
            modelBuilder.Entity<PrivateMessageFriend>().ToTable("Private_Message_Friend");
            modelBuilder.Entity<ServerMember>().ToTable("Server_Members");
            modelBuilder.Entity<ServerMember>()
                .Property(member => member.IsMuted)
                .HasDefaultValue(false);
            modelBuilder.Entity<ServerMember>()
                .Property(member => member.OnboardingCompletedAt)
                .HasColumnType("datetime2");
            modelBuilder.Entity<Channel>().ToTable("Channels");
            modelBuilder.Entity<Channel>()
                .Property(channel => channel.ViewAccessRestricted)
                .HasDefaultValue(false);
            modelBuilder.Entity<Channel>()
                .Property(channel => channel.ViewAllowedRolesJson)
                .HasColumnType("nvarchar(max)")
                .HasDefaultValue("[]");
            modelBuilder.Entity<Channel>()
                .Property(channel => channel.MessageSendRestricted)
                .HasDefaultValue(false);
            modelBuilder.Entity<Channel>()
                .Property(channel => channel.MessageSendAllowedRolesJson)
                .HasColumnType("nvarchar(max)")
                .HasDefaultValue("[]");
            modelBuilder.Entity<Channel>()
                .Property(channel => channel.VoiceAccessRestricted)
                .HasDefaultValue(false);
            modelBuilder.Entity<Channel>()
                .Property(channel => channel.VoiceAllowedRolesJson)
                .HasColumnType("nvarchar(max)")
                .HasDefaultValue("[]");
            modelBuilder.Entity<Channel>()
                .Property(channel => channel.StageSpeakerRestricted)
                .HasDefaultValue(false);
            modelBuilder.Entity<Channel>()
                .Property(channel => channel.StageSpeakerRolesJson)
                .HasColumnType("nvarchar(max)")
                .HasDefaultValue("[]");
            modelBuilder.Entity<Category>().ToTable("Categories");
            modelBuilder.Entity<ServerRole>().ToTable("Server_Roles");
            modelBuilder.Entity<ServerRole>()
                .Property(role => role.Color)
                .HasMaxLength(16)
                .HasDefaultValue("#949ba4");
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
            modelBuilder.Entity<ServerEmoji>().ToTable("Server_Emojis");
            modelBuilder.Entity<ServerEmoji>()
                .Property(emoji => emoji.ServerId)
                .HasMaxLength(128);
            modelBuilder.Entity<ServerEmoji>()
                .Property(emoji => emoji.Name)
                .HasMaxLength(32);
            modelBuilder.Entity<ServerEmoji>()
                .Property(emoji => emoji.ImageUrl)
                .HasMaxLength(2048);
            modelBuilder.Entity<ServerEmoji>()
                .Property(emoji => emoji.CreatedBy)
                .HasMaxLength(256);
            modelBuilder.Entity<ServerEmoji>()
                .HasIndex(emoji => new { emoji.ServerId, emoji.Name })
                .IsUnique();
            modelBuilder.Entity<ServerSticker>().ToTable("Server_Stickers");
            modelBuilder.Entity<ServerSticker>()
                .Property(sticker => sticker.ServerId)
                .HasMaxLength(128);
            modelBuilder.Entity<ServerSticker>()
                .Property(sticker => sticker.Name)
                .HasMaxLength(32);
            modelBuilder.Entity<ServerSticker>()
                .Property(sticker => sticker.ImageUrl)
                .HasMaxLength(2048);
            modelBuilder.Entity<ServerSticker>()
                .Property(sticker => sticker.CreatedBy)
                .HasMaxLength(256);
            modelBuilder.Entity<ServerSticker>()
                .HasIndex(sticker => new { sticker.ServerId, sticker.Name })
                .IsUnique();
            modelBuilder.Entity<MessageReaction>().ToTable("Message_Reactions");
            modelBuilder.Entity<MessageReaction>()
                .HasIndex(reaction => new { reaction.ScopeType, reaction.MessageId, reaction.Emoji, reaction.Username })
                .IsUnique();
            modelBuilder.Entity<MessagePoll>().ToTable("Message_Polls");
            modelBuilder.Entity<MessagePoll>()
                .Property(poll => poll.ScopeType)
                .HasMaxLength(32);
            modelBuilder.Entity<MessagePoll>()
                .Property(poll => poll.MessageId)
                .HasMaxLength(128);
            modelBuilder.Entity<MessagePoll>()
                .Property(poll => poll.CreatedBy)
                .HasMaxLength(256);
            modelBuilder.Entity<MessagePoll>()
                .Property(poll => poll.Question)
                .HasMaxLength(280);
            modelBuilder.Entity<MessagePoll>()
                .HasIndex(poll => new { poll.ScopeType, poll.MessageId })
                .IsUnique();
            modelBuilder.Entity<MessagePollOption>().ToTable("Message_Poll_Options");
            modelBuilder.Entity<MessagePollOption>()
                .Property(option => option.PollId)
                .HasMaxLength(128);
            modelBuilder.Entity<MessagePollOption>()
                .Property(option => option.Text)
                .HasMaxLength(100);
            modelBuilder.Entity<MessagePollOption>()
                .HasIndex(option => new { option.PollId, option.Position })
                .IsUnique();
            modelBuilder.Entity<MessagePollVote>().ToTable("Message_Poll_Votes");
            modelBuilder.Entity<MessagePollVote>()
                .Property(vote => vote.PollId)
                .HasMaxLength(128);
            modelBuilder.Entity<MessagePollVote>()
                .Property(vote => vote.OptionId)
                .HasMaxLength(128);
            modelBuilder.Entity<MessagePollVote>()
                .Property(vote => vote.Username)
                .HasMaxLength(256);
            modelBuilder.Entity<MessagePollVote>()
                .HasIndex(vote => new { vote.PollId, vote.OptionId, vote.Username })
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

            modelBuilder.Entity<ServerAuditLog>().ToTable("Server_Audit_Logs");
            modelBuilder.Entity<ServerAuditLog>()
                .Property(log => log.ServerId)
                .HasMaxLength(128);
            modelBuilder.Entity<ServerAuditLog>()
                .Property(log => log.ActionType)
                .HasMaxLength(64);
            modelBuilder.Entity<ServerAuditLog>()
                .Property(log => log.ActorUsername)
                .HasMaxLength(256);
            modelBuilder.Entity<ServerAuditLog>()
                .Property(log => log.TargetType)
                .HasMaxLength(64);
            modelBuilder.Entity<ServerAuditLog>()
                .Property(log => log.TargetId)
                .HasMaxLength(256);
            modelBuilder.Entity<ServerAuditLog>()
                .Property(log => log.TargetUsername)
                .HasMaxLength(256);
            modelBuilder.Entity<ServerAuditLog>()
                .Property(log => log.DetailsJson)
                .HasColumnType("nvarchar(max)");
            modelBuilder.Entity<ServerAuditLog>()
                .HasIndex(log => new { log.ServerId, log.CreatedAt });

            modelBuilder.Entity<UserReport>().ToTable("User_Reports");
            modelBuilder.Entity<UserReport>()
                .Property(report => report.ScopeType)
                .HasMaxLength(32);
            modelBuilder.Entity<UserReport>()
                .Property(report => report.TargetType)
                .HasMaxLength(32);
            modelBuilder.Entity<UserReport>()
                .Property(report => report.ServerId)
                .HasMaxLength(128);
            modelBuilder.Entity<UserReport>()
                .Property(report => report.ChannelId)
                .HasMaxLength(128);
            modelBuilder.Entity<UserReport>()
                .Property(report => report.GroupId)
                .HasMaxLength(128);
            modelBuilder.Entity<UserReport>()
                .Property(report => report.MessageId)
                .HasMaxLength(128);
            modelBuilder.Entity<UserReport>()
                .Property(report => report.TargetUsername)
                .HasMaxLength(256);
            modelBuilder.Entity<UserReport>()
                .Property(report => report.ReportedByUsername)
                .HasMaxLength(256);
            modelBuilder.Entity<UserReport>()
                .Property(report => report.Reason)
                .HasMaxLength(80);
            modelBuilder.Entity<UserReport>()
                .Property(report => report.Description)
                .HasMaxLength(1000);
            modelBuilder.Entity<UserReport>()
                .Property(report => report.MessagePreview)
                .HasMaxLength(500);
            modelBuilder.Entity<UserReport>()
                .Property(report => report.Status)
                .HasMaxLength(32);
            modelBuilder.Entity<UserReport>()
                .Property(report => report.ReviewedByUsername)
                .HasMaxLength(256);
            modelBuilder.Entity<UserReport>()
                .Property(report => report.ResolutionNote)
                .HasMaxLength(1000);
            modelBuilder.Entity<UserReport>()
                .HasIndex(report => new { report.ServerId, report.Status, report.CreatedAt });
            modelBuilder.Entity<UserReport>()
                .HasIndex(report => new { report.ReportedByUsername, report.CreatedAt });

            modelBuilder.Entity<BotAccount>().ToTable("Bot_Accounts");
            modelBuilder.Entity<BotAccount>()
                .Property(bot => bot.ServerId)
                .HasMaxLength(128);
            modelBuilder.Entity<BotAccount>()
                .Property(bot => bot.Username)
                .HasMaxLength(80);
            modelBuilder.Entity<BotAccount>()
                .Property(bot => bot.DisplayName)
                .HasMaxLength(80);
            modelBuilder.Entity<BotAccount>()
                .Property(bot => bot.AvatarUrl)
                .HasMaxLength(2048);
            modelBuilder.Entity<BotAccount>()
                .Property(bot => bot.Description)
                .HasMaxLength(240);
            modelBuilder.Entity<BotAccount>()
                .Property(bot => bot.Role)
                .HasMaxLength(40);
            modelBuilder.Entity<BotAccount>()
                .Property(bot => bot.TokenHash)
                .HasMaxLength(128);
            modelBuilder.Entity<BotAccount>()
                .Property(bot => bot.CreatedBy)
                .HasMaxLength(256);
            modelBuilder.Entity<BotAccount>()
                .HasIndex(bot => new { bot.ServerId, bot.Username })
                .IsUnique();

            modelBuilder.Entity<ServerWebhook>().ToTable("Server_Webhooks");
            modelBuilder.Entity<ServerWebhook>()
                .Property(webhook => webhook.ServerId)
                .HasMaxLength(128);
            modelBuilder.Entity<ServerWebhook>()
                .Property(webhook => webhook.ChannelId)
                .HasMaxLength(128);
            modelBuilder.Entity<ServerWebhook>()
                .Property(webhook => webhook.Name)
                .HasMaxLength(80);
            modelBuilder.Entity<ServerWebhook>()
                .Property(webhook => webhook.AvatarUrl)
                .HasMaxLength(2048);
            modelBuilder.Entity<ServerWebhook>()
                .Property(webhook => webhook.TokenHash)
                .HasMaxLength(128);
            modelBuilder.Entity<ServerWebhook>()
                .Property(webhook => webhook.CreatedBy)
                .HasMaxLength(256);
            modelBuilder.Entity<ServerWebhook>()
                .HasIndex(webhook => new { webhook.ServerId, webhook.ChannelId });

            modelBuilder.Entity<SlashCommand>().ToTable("Slash_Commands");
            modelBuilder.Entity<SlashCommand>()
                .Property(command => command.ServerId)
                .HasMaxLength(128);
            modelBuilder.Entity<SlashCommand>()
                .Property(command => command.BotAccountId)
                .HasMaxLength(128);
            modelBuilder.Entity<SlashCommand>()
                .Property(command => command.Name)
                .HasMaxLength(32);
            modelBuilder.Entity<SlashCommand>()
                .Property(command => command.Description)
                .HasMaxLength(120);
            modelBuilder.Entity<SlashCommand>()
                .Property(command => command.Usage)
                .HasMaxLength(120);
            modelBuilder.Entity<SlashCommand>()
                .Property(command => command.CreatedBy)
                .HasMaxLength(256);
            modelBuilder.Entity<SlashCommand>()
                .HasIndex(command => new { command.ServerId, command.Name })
                .IsUnique();
            modelBuilder.Entity<SlashCommand>()
                .HasIndex(command => new { command.BotAccountId, command.IsEnabled });

            modelBuilder.Entity<SlashCommandInteraction>().ToTable("Slash_Command_Interactions");
            modelBuilder.Entity<SlashCommandInteraction>()
                .Property(interaction => interaction.SlashCommandId)
                .HasMaxLength(128);
            modelBuilder.Entity<SlashCommandInteraction>()
                .Property(interaction => interaction.ServerId)
                .HasMaxLength(128);
            modelBuilder.Entity<SlashCommandInteraction>()
                .Property(interaction => interaction.ChannelId)
                .HasMaxLength(128);
            modelBuilder.Entity<SlashCommandInteraction>()
                .Property(interaction => interaction.BotAccountId)
                .HasMaxLength(128);
            modelBuilder.Entity<SlashCommandInteraction>()
                .Property(interaction => interaction.CommandName)
                .HasMaxLength(32);
            modelBuilder.Entity<SlashCommandInteraction>()
                .Property(interaction => interaction.InvokedBy)
                .HasMaxLength(256);
            modelBuilder.Entity<SlashCommandInteraction>()
                .Property(interaction => interaction.Arguments)
                .HasMaxLength(2000);
            modelBuilder.Entity<SlashCommandInteraction>()
                .Property(interaction => interaction.ResponseMessageId)
                .HasMaxLength(128);
            modelBuilder.Entity<SlashCommandInteraction>()
                .Property(interaction => interaction.Status)
                .HasMaxLength(32);
            modelBuilder.Entity<SlashCommandInteraction>()
                .HasIndex(interaction => new { interaction.BotAccountId, interaction.Status, interaction.CreatedAt });
            modelBuilder.Entity<SlashCommandInteraction>()
                .HasIndex(interaction => new { interaction.ServerId, interaction.ChannelId, interaction.CreatedAt });

            modelBuilder.Entity<OAuthApplication>().ToTable("OAuth_Applications");
            modelBuilder.Entity<OAuthApplication>()
                .Property(application => application.Name)
                .HasMaxLength(80);
            modelBuilder.Entity<OAuthApplication>()
                .Property(application => application.Description)
                .HasMaxLength(240);
            modelBuilder.Entity<OAuthApplication>()
                .Property(application => application.IconUrl)
                .HasMaxLength(2048);
            modelBuilder.Entity<OAuthApplication>()
                .Property(application => application.OwnerUsername)
                .HasMaxLength(256);
            modelBuilder.Entity<OAuthApplication>()
                .Property(application => application.ClientSecretHash)
                .HasMaxLength(128);
            modelBuilder.Entity<OAuthApplication>()
                .Property(application => application.RedirectUrisJson)
                .HasColumnType("nvarchar(max)");
            modelBuilder.Entity<OAuthApplication>()
                .Property(application => application.AllowedScopesJson)
                .HasColumnType("nvarchar(max)");
            modelBuilder.Entity<OAuthApplication>()
                .Property(application => application.BotAccountId)
                .HasMaxLength(128);
            modelBuilder.Entity<OAuthApplication>()
                .HasIndex(application => application.OwnerUsername);

            modelBuilder.Entity<OAuthAppAuthorization>().ToTable("OAuth_App_Authorizations");
            modelBuilder.Entity<OAuthAppAuthorization>()
                .Property(authorization => authorization.ApplicationId)
                .HasMaxLength(128);
            modelBuilder.Entity<OAuthAppAuthorization>()
                .Property(authorization => authorization.Username)
                .HasMaxLength(256);
            modelBuilder.Entity<OAuthAppAuthorization>()
                .Property(authorization => authorization.ServerId)
                .HasMaxLength(128);
            modelBuilder.Entity<OAuthAppAuthorization>()
                .Property(authorization => authorization.ScopesJson)
                .HasColumnType("nvarchar(max)");
            modelBuilder.Entity<OAuthAppAuthorization>()
                .HasIndex(authorization => new { authorization.ApplicationId, authorization.Username, authorization.ServerId });

            modelBuilder.Entity<OAuthAuthorizationCode>().ToTable("OAuth_Authorization_Codes");
            modelBuilder.Entity<OAuthAuthorizationCode>()
                .Property(code => code.ApplicationId)
                .HasMaxLength(128);
            modelBuilder.Entity<OAuthAuthorizationCode>()
                .Property(code => code.AuthorizationId)
                .HasMaxLength(128);
            modelBuilder.Entity<OAuthAuthorizationCode>()
                .Property(code => code.Username)
                .HasMaxLength(256);
            modelBuilder.Entity<OAuthAuthorizationCode>()
                .Property(code => code.ServerId)
                .HasMaxLength(128);
            modelBuilder.Entity<OAuthAuthorizationCode>()
                .Property(code => code.CodeHash)
                .HasMaxLength(128);
            modelBuilder.Entity<OAuthAuthorizationCode>()
                .Property(code => code.RedirectUri)
                .HasMaxLength(2048);
            modelBuilder.Entity<OAuthAuthorizationCode>()
                .Property(code => code.ScopesJson)
                .HasColumnType("nvarchar(max)");
            modelBuilder.Entity<OAuthAuthorizationCode>()
                .HasIndex(code => code.CodeHash)
                .IsUnique();
            modelBuilder.Entity<OAuthAuthorizationCode>()
                .HasIndex(code => new { code.ApplicationId, code.ExpiresAt });

            modelBuilder.Entity<OAuthAccessToken>().ToTable("OAuth_Access_Tokens");
            modelBuilder.Entity<OAuthAccessToken>()
                .Property(token => token.ApplicationId)
                .HasMaxLength(128);
            modelBuilder.Entity<OAuthAccessToken>()
                .Property(token => token.AuthorizationId)
                .HasMaxLength(128);
            modelBuilder.Entity<OAuthAccessToken>()
                .Property(token => token.Username)
                .HasMaxLength(256);
            modelBuilder.Entity<OAuthAccessToken>()
                .Property(token => token.ServerId)
                .HasMaxLength(128);
            modelBuilder.Entity<OAuthAccessToken>()
                .Property(token => token.TokenHash)
                .HasMaxLength(128);
            modelBuilder.Entity<OAuthAccessToken>()
                .Property(token => token.ScopesJson)
                .HasColumnType("nvarchar(max)");
            modelBuilder.Entity<OAuthAccessToken>()
                .HasIndex(token => token.TokenHash)
                .IsUnique();
            modelBuilder.Entity<OAuthAccessToken>()
                .HasIndex(token => new { token.ApplicationId, token.Username, token.ExpiresAt });

            base.OnModelCreating(modelBuilder);
            Console.WriteLine("database ready");
        }
    }
}
