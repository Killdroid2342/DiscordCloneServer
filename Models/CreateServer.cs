using System.ComponentModel.DataAnnotations;

public class CreateServer
{
    [Key]
    public string? ServerID { get; set; }
    [Required]
    public string ServerName { get; set; } = string.Empty;

    [Required]
    public string ServerOwner { get; set; } = string.Empty;

    public string? InviteLink { get; set; }
    public DateTime? Date { get; set; }
    public string? Description { get; set; }
    public bool IsPublic { get; set; }
    public string? DiscoveryCategory { get; set; }
    public string? DiscoveryTagsJson { get; set; }
    public bool WelcomeEnabled { get; set; } = true;
    public string? WelcomeMessage { get; set; }
    public string? WelcomeChecklistJson { get; set; }
    public string VerificationLevel { get; set; } = "none";
    public bool RequireVerifiedEmail { get; set; }
    public int MinimumAccountAgeMinutes { get; set; }
    public int MinimumMembershipMinutes { get; set; }
    public bool RequireTwoFactorForModerators { get; set; }
}
