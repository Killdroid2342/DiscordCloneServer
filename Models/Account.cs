using System.ComponentModel.DataAnnotations;

namespace DiscordCloneServer.Models
{

    public class Account
    {
        [Key]
        public int Id { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string PassWord { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string[]? Friends { get; set; }
        public string[]? IncomingFriendRequests { get; set; }
        public string[]? OutgoingFriendRequests { get; set; }
        public string[]? Groups { get; set; }
        public string? BackgroundColor { get; set; }
        public string? TextColor { get; set; }
        public string? ProfilePictureUrl { get; set; }
        public string? ProfileBannerUrl { get; set; }
        public string? ProfileBannerColor { get; set; }
        public string? Description { get; set; }
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
        public DateTime? EmailVerifiedAt { get; set; }
        public DateTime? PhoneNumberVerifiedAt { get; set; }
        public string PresenceStatus { get; set; } = "online";
        public string PrivacyDmPolicy { get; set; } = "friends";
        public bool PrivacyAllowFriendRequestsEveryone { get; set; } = true;
        public bool PrivacyAllowFriendRequestsFriendsOfFriends { get; set; } = true;
        public bool PrivacyAllowFriendRequestsServerMembers { get; set; } = true;
        public bool PrivacyShowActivity { get; set; } = true;
        public string[]? BlockedUsers { get; set; }
        public string SettingsJson { get; set; } = "{}";
        public string VoiceChangerSettingsJson { get; set; } = "{}";
        public bool IsDisabled { get; set; }
        public DateTime? PasswordUpdatedAt { get; set; }
        public string? PasswordResetTokenHash { get; set; }
        public DateTime? PasswordResetExpiresAt { get; set; }
        public bool TwoFactorEnabled { get; set; }
        public string? AuthenticatorSecretProtected { get; set; }
        public string[]? TwoFactorBackupCodeHashes { get; set; }
        public string? TwoFactorLoginTicketHash { get; set; }
        public DateTime? TwoFactorLoginTicketExpiresAt { get; set; }

    }
}
