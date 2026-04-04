using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HelpDesk.Models;
using HelpDesk.Models.Data;
using HelpDesk.Web.Validation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Web.Services;

/// <summary>Creates and validates time-limited password reset tokens, with SHA-256 hashed storage.</summary>
public class PasswordResetService
{
    private readonly HelpDeskDbContext _db;
    private readonly PasswordHasher<User> _hasher;
    private readonly IConfiguration _configuration;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<PasswordResetService> _logger;

    public PasswordResetService(
        HelpDeskDbContext db,
        PasswordHasher<User> hasher,
        IConfiguration configuration,
        IHttpContextAccessor httpContextAccessor,
        ILogger<PasswordResetService> logger)
    {
        _db = db;
        _hasher = hasher;
        _configuration = configuration;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    /// <summary>
    /// Prefer the current HTTP request (correct port, e.g. launchSettings https://localhost:7099) over <c>App:BaseUrl</c> for links in emails.
    /// </summary>
    private string GetPublicBaseUrl()
    {
        var req = _httpContextAccessor.HttpContext?.Request;
        if (req is not null && !string.IsNullOrEmpty(req.Host.Value))
            return $"{req.Scheme}://{req.Host.Value}".TrimEnd('/');

        return (_configuration["App:BaseUrl"] ?? "").TrimEnd('/');
    }

    public async Task<string?> CreateResetTokenAsync(string email, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email && u.IsActive, ct).ConfigureAwait(false);
        if (user is null)
            return null;

        await InvalidateExistingTokensAsync(user.Id, ct).ConfigureAwait(false);

        var raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var hash = Sha256(raw);

        var hours = double.TryParse(_configuration["App:PasswordResetTokenHours"], CultureInfo.InvariantCulture, out var h) ? h : 24;
        var now = DateTime.UtcNow;
        _db.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = user.Id,
            TokenHash = hash,
            ExpiresAt = now.AddHours(hours),
            CreatedAt = now
        });
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        var baseUrl = GetPublicBaseUrl();
        return $"{baseUrl}/account/reset-password?token={Uri.EscapeDataString(raw)}";
    }

    /// <summary>Creates a reset link for the active user with the given login ID; token expires in 10 minutes.</summary>
    public async Task<(string Link, string Email)?> TryCreateResetTokenByLoginIdAsync(string loginId, CancellationToken ct = default)
    {
        var normalized = AdminNameValidation.ToUpperNoSpaces(loginId);
        if (string.IsNullOrEmpty(normalized))
            return null;

        var user = await _db.Users.FirstOrDefaultAsync(u => u.LoginId == normalized && u.IsActive, ct).ConfigureAwait(false);
        if (user is null || string.IsNullOrWhiteSpace(user.Email))
            return null;

        await InvalidateExistingTokensAsync(user.Id, ct).ConfigureAwait(false);

        var raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var hash = Sha256(raw);

        var now = DateTime.UtcNow;
        _db.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = user.Id,
            TokenHash = hash,
            ExpiresAt = now.AddMinutes(10),
            CreatedAt = now
        });
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        var baseUrl = GetPublicBaseUrl();
        var link = $"{baseUrl}/account/reset-password?token={Uri.EscapeDataString(raw)}";
        return (link, user.Email.Trim());
    }

    public async Task<(bool ok, string? error)> ResetPasswordAsync(string rawToken, string newPassword, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < Validation.PasswordPolicy.MinLength)
            return (false, Validation.PasswordPolicy.MinLengthMessage);

        var hash = Sha256(rawToken);
        var token = await _db.PasswordResetTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.UsedAt == null && t.ExpiresAt > DateTime.UtcNow, ct)
            .ConfigureAwait(false);

        if (token is null)
            return (false, "Invalid or expired token.");

        token.User.PasswordHash = _hasher.HashPassword(token.User, newPassword);
        token.User.MustChangePassword = false;
        token.User.UpdatedAt = DateTime.UtcNow;
        token.UsedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return (true, null);
    }

    private async Task InvalidateExistingTokensAsync(int userId, CancellationToken ct)
    {
        var existing = await _db.PasswordResetTokens
            .Where(t => t.UserId == userId && t.UsedAt == null && t.ExpiresAt > DateTime.UtcNow)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var t in existing)
            t.UsedAt = DateTime.UtcNow;
    }

    private static string Sha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }
}
