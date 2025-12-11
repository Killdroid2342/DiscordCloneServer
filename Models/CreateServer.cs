using System.ComponentModel.DataAnnotations;

public class CreateServer
{
    [Key]
    public string? ServerID { get; set; }
    [Required]
    public string ServerName { get; set; }

    [Required]
    public string ServerOwner { get; set; }

    public string? InviteLink { get; set; }
    public DateTime? Date { get; set; }
}
