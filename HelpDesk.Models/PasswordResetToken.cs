using System.ComponentModel.DataAnnotations;

namespace HelpDesk.Models;

/// <summary>One-time-use token for password reset, hashed and time-limited.</summary>
public sealed class PasswordResetToken
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    [Required, MaxLength(128)]
    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

