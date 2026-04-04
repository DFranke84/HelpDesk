using System.Security.Claims;
using HelpDesk.Models;
using HelpDesk.Models.Data;
using HelpDesk.Logging;
using HelpDesk.Web.Services;
using HelpDesk.Web.Validation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using NLog;

namespace HelpDesk.Web.Controllers;

/// <remarks>
/// [ApiController] is omitted so <c>login-form</c> accepts <c>application/x-www-form-urlencoded</c> POSTs reliably.
/// Antiforgery is handled per-endpoint: JSON/fetch endpoints ignore it (custom headers provide implicit CSRF defense),
/// and form-POST endpoints are protected by SameSite=Lax cookies plus endpoint-level security (credentials / secret tokens).
/// </remarks>
[Route("api/[controller]")]
public class AccountController : ControllerBase
{
    private readonly HelpDeskDbContext _db;
    private readonly IObservabilityService _observability;
    private readonly PasswordHasher<User> _hasher;
    private readonly PasswordResetService _passwordReset;
    private readonly IEmailSender _email;
    private readonly IConfiguration _configuration;
    private readonly LoginRateLimiter _rateLimiter;

    public AccountController(
        HelpDeskDbContext db,
        IObservabilityService observability,
        PasswordHasher<User> hasher,
        PasswordResetService passwordReset,
        IEmailSender email,
        IConfiguration configuration,
        LoginRateLimiter rateLimiter)
    {
        _db = db;
        _observability = observability;
        _hasher = hasher;
        _passwordReset = passwordReset;
        _email = email;
        _configuration = configuration;
        _rateLimiter = rateLimiter;
    }

    public record LoginRequest(string LoginId, string Password, string? ReturnUrl);

    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        var loginId = AdminNameValidation.ToUpperNoSpaces(req.LoginId);
        if (string.IsNullOrEmpty(loginId) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest("Login ID and password required.");

        if (_rateLimiter.IsBlocked(loginId, out var retryAfter))
        {
            var minutes = (int)Math.Ceiling(retryAfter.TotalMinutes);
            return Unauthorized(new { error = $"Too many failed attempts. Try again in {minutes} minute(s)." });
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.LoginId == loginId && u.IsActive, ct)
            .ConfigureAwait(false);
        if (user is null)
        {
            _rateLimiter.RecordFailure(loginId);
            return Unauthorized();
        }

        var verify = _hasher.VerifyHashedPassword(user, user.PasswordHash, req.Password);
        if (verify == PasswordVerificationResult.Failed)
        {
            _rateLimiter.RecordFailure(loginId);
            return Unauthorized();
        }

        _rateLimiter.RecordSuccess(loginId);

        var claims = CookieAuthClaims.BuildForFormsUser(user);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)),
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(60),
                AllowRefresh = true
            }).ConfigureAwait(false);

        await _observability.AddAuditAsync("Login", user.LoginId, "Forms login", loginId, ct).ConfigureAwait(false);

        ClearSuppressWindowsAutoLoginCookie();

        var url = string.IsNullOrEmpty(req.ReturnUrl) || !Url.IsLocalUrl(req.ReturnUrl) ? "/" : req.ReturnUrl;
        if (user.MustChangePassword)
            url = $"/account/first-login-password?returnUrl={Uri.EscapeDataString(url)}";
        return Ok(new { ok = true, mustChangePassword = user.MustChangePassword, returnUrl = url });
    }

    /// <summary>Browser form POST (no Blazor circuit / no JavaScript required). Primary login path for reliability.</summary>
    /// <remarks>Antiforgery tokens cannot be rendered in interactive-mode Blazor; SameSite=Lax cookies mitigate cross-site POST.</remarks>
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [HttpPost("login-form")]
    public async Task<IActionResult> LoginForm(
        [FromForm] string? loginId,
        [FromForm] string? password,
        [FromForm] string? returnUrl,
        CancellationToken ct)
    {
        var normalized = AdminNameValidation.ToUpperNoSpaces(loginId);
        if (string.IsNullOrEmpty(normalized) || string.IsNullOrWhiteSpace(password))
            return Redirect("/login?loginError=required");

        if (_rateLimiter.IsBlocked(normalized, out _))
            return Redirect("/login?loginError=locked");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.LoginId == normalized && u.IsActive, ct)
            .ConfigureAwait(false);
        if (user is null)
        {
            _rateLimiter.RecordFailure(normalized);
            return Redirect("/login?loginError=invalid");
        }

        var verify = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verify == PasswordVerificationResult.Failed)
        {
            _rateLimiter.RecordFailure(normalized);
            return Redirect("/login?loginError=invalid");
        }

        _rateLimiter.RecordSuccess(normalized);

        var claims = CookieAuthClaims.BuildForFormsUser(user);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)),
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(60),
                AllowRefresh = true
            }).ConfigureAwait(false);

        await _observability.AddAuditAsync("Login", user.LoginId, "Forms login", normalized, ct).ConfigureAwait(false);

        ClearSuppressWindowsAutoLoginCookie();

        var target = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl;
        if (!Url.IsLocalUrl(target))
            target = "/";
        if (user.MustChangePassword)
            return Redirect($"/account/first-login-password?returnUrl={Uri.EscapeDataString(target)}");
        return Redirect(target);
    }

    public record SetPasswordRequest(string CurrentPassword, string NewPassword);

    /// <summary>Change password while signed in (required when <see cref="User.MustChangePassword"/> is true).</summary>
    [Authorize]
    [IgnoreAntiforgeryToken]
    [HttpPost("set-password")]
    public async Task<IActionResult> SetPassword([FromBody] SetPasswordRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.NewPassword) || req.NewPassword.Length < 6)
            return BadRequest(new { error = "New password must be at least 6 characters." });

        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(id, out var userId))
            return Unauthorized();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false);
        if (user is null)
            return NotFound();

        var verify = _hasher.VerifyHashedPassword(user, user.PasswordHash, req.CurrentPassword ?? "");
        if (verify == PasswordVerificationResult.Failed)
            return BadRequest(new { error = "Current password is incorrect." });

        user.PasswordHash = _hasher.HashPassword(user, req.NewPassword);
        user.MustChangePassword = false;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        var sessionKind = User.FindFirst(AuthClaims.SessionKind)?.Value ?? AuthClaims.SessionForms;
        var claims = CookieAuthClaims.BuildForUser(user, sessionKind);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)),
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(60),
                AllowRefresh = true
            }).ConfigureAwait(false);

        await _observability.AddAuditAsync("PasswordChange", user.LoginId, "User changed password", null, ct).ConfigureAwait(false);

        return Ok(new { ok = true });
    }

    [Authorize]
    [IgnoreAntiforgeryToken]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        // Prefer login_id from the cookie (always set on sign-in). DB lookup alone can miss if the row is gone
        // or claims differ; NLog should still record who logged off.
        var auditLoginId = User.FindFirstValue(AuthClaims.LoginId);
        if (string.IsNullOrWhiteSpace(auditLoginId))
        {
            var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (int.TryParse(id, out var userId))
            {
                var auditUser = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false);
                auditLoginId = auditUser?.LoginId;
            }
        }

        var windowsName = User.FindFirst(AuthClaims.WindowsName)?.Value;
        if (!string.IsNullOrWhiteSpace(windowsName))
        {
            using (ScopeContext.PushProperty("domainLogin", windowsName))
                await _observability.AddAuditAsync("Logoff", auditLoginId, "User logged out", windowsName, ct).ConfigureAwait(false);
        }
        else
        {
            await _observability.AddAuditAsync("Logoff", auditLoginId, "User logged out", null, ct).ConfigureAwait(false);
        }
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme).ConfigureAwait(false);

        // Defense-in-depth: explicitly delete cookies as well. SignOutAsync should already emit the correct Set-Cookie,
        // but explicit deletes help when browsers/extensions cache state or when older cookie names existed.
        Response.Cookies.Delete(AuthCookieNames.Auth, new CookieOptions { Path = "/" });
        Response.Cookies.Delete(".AspNetCore.Cookies", new CookieOptions { Path = "/" });

        // Manual logout: suppress Windows auto-login so the user stays on the login screen until they sign in again.
        // Idle timeout sends X-Helpdesk-Logout-Reason: idle so integrated login can still occur on the next visit.
        var logoutReason = Request.Headers["X-Helpdesk-Logout-Reason"].ToString();
        if (!string.Equals(logoutReason, "idle", StringComparison.OrdinalIgnoreCase))
        {
            var minutes = _configuration.GetValue("App:SuppressWindowsAutoLoginMinutes", 24 * 60);
            Response.Cookies.Append(AuthCookieNames.SuppressWindowsAutoLogin, "1", new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Secure = Request.IsHttps,
                Path = "/",
                Expires = DateTimeOffset.UtcNow.AddMinutes(minutes)
            });
        }

        return Ok();
    }

    private void ClearSuppressWindowsAutoLoginCookie() =>
        Response.Cookies.Delete(AuthCookieNames.SuppressWindowsAutoLogin, new CookieOptions { Path = "/" });

    [Authorize]
    [IgnoreAntiforgeryToken]
    [HttpPost("session/ping")]
    public IActionResult Ping() => Ok();

    public record RequestPasswordChangeRequest(string Email);

    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [HttpPost("request-password-change")]
    public async Task<IActionResult> RequestPasswordChange([FromBody] RequestPasswordChangeRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Email))
            return BadRequest();
        var link = await _passwordReset.CreateResetTokenAsync(req.Email.Trim(), ct).ConfigureAwait(false);
        if (link is null)
            return Ok(new { sent = false }); // do not reveal whether email exists

        await _email.SendAsync(req.Email.Trim(), "Help Desk password reset",
            $"Use this one-time link to set a new password:\r\n{link}", ct).ConfigureAwait(false);
        return Ok(new { sent = true });
    }

    public record RequestPasswordResetByLoginRequest(string LoginId);

    /// <summary>Forgot password: looks up email by login ID and sends a reset link valid for 10 minutes.</summary>
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [HttpPost("request-password-reset-by-login")]
    public async Task<IActionResult> RequestPasswordResetByLogin([FromBody] RequestPasswordResetByLoginRequest req, CancellationToken ct)
    {
        var loginId = AdminNameValidation.ToUpperNoSpaces(req.LoginId);
        if (string.IsNullOrEmpty(loginId))
            return BadRequest();

        var created = await _passwordReset.TryCreateResetTokenByLoginIdAsync(loginId, ct).ConfigureAwait(false);
        if (created is null)
            return Ok(new { ok = true });

        await _email.SendAsync(created.Value.Email, "Help Desk password reset",
            "Use this one-time link to set a new password. It expires in 10 minutes:\r\n" + created.Value.Link, ct)
            .ConfigureAwait(false);
        return Ok(new { ok = true });
    }

    /// <summary>Browser form POST for forgot password (same reliability as <c>login-form</c>; no Blazor circuit required).</summary>
    /// <remarks>Antiforgery tokens cannot be rendered in interactive-mode Blazor; SameSite=Lax cookies mitigate cross-site POST.</remarks>
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [HttpPost("request-password-reset-by-login-form")]
    public async Task<IActionResult> RequestPasswordResetByLoginForm(
        [FromForm] string? loginId,
        [FromForm] string? redirectAfter,
        CancellationToken ct)
    {
        var target = SafeForgotRedirect(redirectAfter);
        var normalized = AdminNameValidation.ToUpperNoSpaces(loginId);
        if (string.IsNullOrEmpty(normalized))
        {
            var err = QueryHelpers.AddQueryString(target, "forgot", "1");
            err = QueryHelpers.AddQueryString(err, "forgotError", "required");
            return Redirect(err);
        }

        var created = await _passwordReset.TryCreateResetTokenByLoginIdAsync(normalized, ct).ConfigureAwait(false);
        if (created is not null)
        {
            await _email.SendAsync(created.Value.Email, "Help Desk password reset",
                    "Use this one-time link to set a new password. It expires in 10 minutes:\r\n" + created.Value.Link, ct)
                .ConfigureAwait(false);
        }

        var ok = QueryHelpers.AddQueryString(StripForgotQueryKeys(target), "forgotSent", "1");
        return Redirect(ok);
    }

    /// <summary>Allow only same-site relative paths (path + optional query), no scheme/host.</summary>
    private static string SafeForgotRedirect(string? redirectAfter)
    {
        if (string.IsNullOrWhiteSpace(redirectAfter))
            return "/login";
        var t = redirectAfter.Trim();
        if (!t.StartsWith('/') || t.StartsWith("//", StringComparison.Ordinal))
            return "/login";
        return t;
    }

    private static string StripForgotQueryKeys(string relativePathAndQuery)
    {
        var q = relativePathAndQuery.IndexOf('?', StringComparison.Ordinal);
        if (q < 0)
            return relativePathAndQuery;
        var path = relativePathAndQuery[..q];
        var parsed = QueryHelpers.ParseQuery(relativePathAndQuery[q..]);
        var d = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in parsed)
        {
            if (string.Equals(kv.Key, "forgot", StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.Equals(kv.Key, "forgotSent", StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.Equals(kv.Key, "forgotError", StringComparison.OrdinalIgnoreCase))
                continue;
            d[kv.Key] = kv.Value.ToString();
        }

        if (d.Count == 0)
            return path.Length == 0 ? "/" : path;
        return QueryHelpers.AddQueryString(path, d);
    }

    public record ResetPasswordRequest(string Token, string NewPassword);

    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPasswordApi([FromBody] ResetPasswordRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Token) || string.IsNullOrWhiteSpace(req.NewPassword))
            return BadRequest();
        var (ok, err) = await _passwordReset.ResetPasswordAsync(req.Token, req.NewPassword, ct).ConfigureAwait(false);
        if (!ok)
            return BadRequest(err);
        return Ok(new { ok = true });
    }

    /// <summary>Browser form POST for reset password (no Blazor circuit required; pairs with <c>data-enhance="false"</c>).</summary>
    /// <remarks>Antiforgery tokens cannot be rendered in interactive-mode Blazor; SameSite=Lax cookies mitigate cross-site POST. Requires a valid secret token.</remarks>
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [HttpPost("reset-password-form")]
    public async Task<IActionResult> ResetPasswordForm(
        [FromForm] string? token,
        [FromForm] string? password,
        [FromForm] string? confirmPassword,
        CancellationToken ct)
    {
        var t = token?.Trim() ?? "";
        var back = string.IsNullOrEmpty(t)
            ? "/account/reset-password"
            : QueryHelpers.AddQueryString("/account/reset-password", "token", t);

        if (string.IsNullOrWhiteSpace(t))
            return Redirect(QueryHelpers.AddQueryString(back, "resetErr", "missing"));

        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(confirmPassword) || password != confirmPassword)
            return Redirect(QueryHelpers.AddQueryString(back, "resetErr", "mismatch"));

        if (password.Length < 6)
            return Redirect(QueryHelpers.AddQueryString(back, "resetErr", "short"));

        var (ok, _) = await _passwordReset.ResetPasswordAsync(t, password, ct).ConfigureAwait(false);
        if (!ok)
            return Redirect(QueryHelpers.AddQueryString(back, "resetErr", "invalid"));

        return Redirect("/login?reset=sent");
    }
}
