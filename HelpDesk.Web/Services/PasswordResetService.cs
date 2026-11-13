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

    /// <summary>
    /// Create password reset service.
    /// </summary>
    /// <param name="db">Help Desk database <see cref="HelpDeskDbContext"/>.</param>
    /// <param name="hasher">Password hasher <see cref="PasswordHasher{User}"/>.</param>
    /// <param name="configuration">Application configuration <see cref="IConfiguration"/>.</param>
    /// <param name="httpContextAccessor">Current HTTP context <see cref="IHttpContextAccessor"/>.</param>
    /// <param name="logger">Logger <see cref="ILogger{PasswordResetService}"/>.</param>
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
    /// <summary>Most reset emails one account can be sent within <see cref="ResetRequestWindow"/>.</summary>
    public const int MaxResetRequestsPerWindow = 3;

    /// <summary>Window for <see cref="MaxResetRequestsPerWindow"/>.</summary>
    public static readonly TimeSpan ResetRequestWindow = TimeSpan.FromMinutes(15);

    private string GetPublicBaseUrl()
    {
        var configured = (_configuration["App:BaseUrl"] ?? "").TrimEnd('/');

        // The Host header is supplied by the caller. It can only be trusted when AllowedHosts restricts it;
        // with AllowedHosts "*" an attacker could make the emailed link point at their own site.
        var req = _httpContextAccessor.HttpContext?.Request;
        var hostIsTrusted = req is not null && !string.IsNullOrEmpty(req.Host.Value) && !AllowsAnyHost();
        if (hostIsTrusted || (string.IsNullOrEmpty(configured) && req is not null && !string.IsNullOrEmpty(req.Host.Value)))
            return $"{req!.Scheme}://{req.Host.Value}".TrimEnd('/');

        return configured;
    }

    private bool AllowsAnyHost()
    {
        var allowed = _configuration["AllowedHosts"];
        if (string.IsNullOrWhiteSpace(allowed))
            return true;
        return allowed.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(h => h is "*" or "0.0.0.0" or "::");
    }

    /// <summary>True when this account has already been sent the maximum number of reset emails recently.</summary>
    private async Task<bool> IsResetThrottledAsync(int userId, CancellationToken ct)
    {
        var since = DateTime.UtcNow - ResetRequestWindow;
        var recent = await _db.PasswordResetTokens.AsNoTracking()
            .CountAsync(t => t.UserId == userId && t.CreatedAt >= since, ct)
            .ConfigureAwait(false);
        if (recent < MaxResetRequestsPerWindow)
            return false;

        _logger.LogWarning("Password reset email suppressed for user {UserId}: {Count} requests in the last {Minutes} minutes.",
            userId, recent, ResetRequestWindow.TotalMinutes);
        return true;
    }

    /// <summary>
    /// Create a reset link for an active user matching the email address.
    /// </summary>
    /// <param name="email">User email address.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Absolute reset URL, or null when no user matches.</returns>
    public async Task<string?> CreateResetTokenAsync(string email, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email && u.IsActive && u.UserType == UserType.User, ct).ConfigureAwait(false);
        if (user is null)
            return null;

        if (await IsResetThrottledAsync(user.Id, ct).ConfigureAwait(false))
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

    /// <summary>
    /// Create a reset link for the active user with the given login id (10-minute token).
    /// </summary>
    /// <param name="loginId">Normalized login id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Link and email when created; otherwise null.</returns>
    public async Task<(string Link, string Email)?> TryCreateResetTokenByLoginIdAsync(string loginId, CancellationToken ct = default)
    {
        var normalized = AdminNameValidation.NormalizeLoginId(loginId);
        if (string.IsNullOrEmpty(normalized))
            return null;

        var user = await _db.Users.FirstOrDefaultAsync(
                u => u.LoginId.ToLower() == normalized.ToLower() && u.IsActive && u.UserType == UserType.User, ct)
            .ConfigureAwait(false);
        if (user is null || string.IsNullOrWhiteSpace(user.Email))
            return null;

        if (await IsResetThrottledAsync(user.Id, ct).ConfigureAwait(false))
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

    /// <summary>
    /// Apply a new password using a valid one-time token.
    /// </summary>
    /// <param name="rawToken">Plaintext token from the email link.</param>
    /// <param name="newPassword">New password value.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success flag and optional error message.</returns>
    public async Task<(bool ok, string? error)> ResetPasswordAsync(string rawToken, string newPassword, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < Validation.PasswordPolicy.MinLength)
            return (false, Validation.PasswordPolicy.MinLengthMessage);

        var hash = Sha256(rawToken);
        var token = await _db.PasswordResetTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.UsedAt == null && t.ExpiresAt > DateTime.UtcNow && t.User.UserType == UserType.User, ct)
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
