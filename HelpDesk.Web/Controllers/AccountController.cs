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
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using NLog;

namespace HelpDesk.Web.Controllers;

/// <summary>Rejects requests that lack the <c>X-Requested-With</c> header (CSRF defense for JSON/fetch endpoints).</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
internal sealed class RequireFetchHeaderAttribute : ActionFilterAttribute
{
    /// <summary>
    /// Reject the action when the fetch header is missing.
    /// </summary>
    /// <param name="context">Executing action context <see cref="ActionExecutingContext"/>.</param>
    /// <returns>This method does not return a value.</returns>
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ActionDescriptor.EndpointMetadata.Any(m => m is SkipFetchHeaderCheckAttribute))
            return;

        if (!context.HttpContext.Request.Headers.ContainsKey("X-Requested-With"))
        {
            context.Result = new BadRequestObjectResult("Missing required request header.");
            return;
        }
        base.OnActionExecuting(context);
    }
}

/// <summary>Skips the <see cref="RequireFetchHeaderAttribute"/> check (for browser form POST endpoints).</summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class SkipFetchHeaderCheckAttribute : Attribute { }

/// <remarks>
/// [ApiController] is omitted so <c>login-form</c> accepts <c>application/x-www-form-urlencoded</c> POSTs reliably.
/// JSON/fetch endpoints require <c>X-Requested-With</c> header for CSRF defense (cross-origin forms cannot set custom headers).
/// Form-POST endpoints are exempt and rely on SameSite=Lax cookies plus endpoint-level security (credentials / secret tokens).
/// </remarks>
/// <summary>
/// Handle sign-in, password changes, and password reset API endpoints.
/// </summary>
[Route("api/[controller]")]
[RequireFetchHeader]
public class AccountController : ControllerBase
{
    private readonly HelpDeskDbContext _db;
    private readonly ILoggerService _loggerService;
    private readonly PasswordHasher<User> _hasher;
    private readonly PasswordResetService _passwordReset;
    private readonly IEmailSender _email;
    private readonly IConfiguration _configuration;
    private readonly LoginRateLimiter _rateLimiter;

    /// <summary>
    /// Create account controller with auth and email dependencies.
    /// </summary>
    /// <param name="db">Help Desk database <see cref="HelpDeskDbContext"/>.</param>
    /// <param name="loggerService">Logger sink <see cref="ILoggerService"/>.</param>
    /// <param name="hasher">Password hasher <see cref="PasswordHasher{User}"/>.</param>
    /// <param name="passwordReset">Reset token service <see cref="PasswordResetService"/>.</param>
    /// <param name="email">Transactional email <see cref="IEmailSender"/>.</param>
    /// <param name="configuration">Application configuration <see cref="IConfiguration"/>.</param>
    /// <param name="rateLimiter">Login rate limiter <see cref="LoginRateLimiter"/>.</param>
    public AccountController(
        HelpDeskDbContext db,
        ILoggerService loggerService,
        PasswordHasher<User> hasher,
        PasswordResetService passwordReset,
        IEmailSender email,
        IConfiguration configuration,
        LoginRateLimiter rateLimiter)
    {
        _db = db;
        _loggerService = loggerService;
        _hasher = hasher;
        _passwordReset = passwordReset;
        _email = email;
        _configuration = configuration;
        _rateLimiter = rateLimiter;
    }

    public record LoginRequest(string LoginId, string Password, string? ReturnUrl);

    /// <summary>
    /// Authenticate with JSON credentials and issue auth cookie.
    /// </summary>
    /// <param name="req">Login payload <see cref="LoginRequest"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>JSON result with redirect URL and must-change-password flag.</returns>
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

        if (string.IsNullOrEmpty(user.PasswordHash))
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

        await _loggerService.AddAuditAsync("Login", user.LoginId, "Forms login", loginId, ct).ConfigureAwait(false);

        ClearSuppressWindowsAutoLoginCookie();

        var url = string.IsNullOrEmpty(req.ReturnUrl) || !Url.IsLocalUrl(req.ReturnUrl) ? "/" : req.ReturnUrl;
        if (user.MustChangePassword)
            url = $"/account/first-login-password?returnUrl={Uri.EscapeDataString(url)}";
        return Ok(new { ok = true, mustChangePassword = user.MustChangePassword, returnUrl = url });
    }

    /// <summary>
    /// Authenticate via browser form post and redirect on success.
    /// </summary>
    /// <remarks>Antiforgery tokens cannot be rendered in interactive-mode Blazor; SameSite=Lax cookies mitigate cross-site POST.</remarks>
    /// <param name="loginId">Submitted login id.</param>
    /// <param name="password">Submitted password.</param>
    /// <param name="returnUrl">Optional local return path after login.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Redirect to home, first-login password, or login error.</returns>
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [SkipFetchHeaderCheck]
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

        if (string.IsNullOrEmpty(user.PasswordHash))
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

        await _loggerService.AddAuditAsync("Login", user.LoginId, "Forms login", normalized, ct).ConfigureAwait(false);

        ClearSuppressWindowsAutoLoginCookie();

        var target = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl;
        if (!Url.IsLocalUrl(target))
            target = "/";
        if (user.MustChangePassword)
            return Redirect($"/account/first-login-password?returnUrl={Uri.EscapeDataString(target)}");
        return Redirect(target);
    }

    public record SetPasswordRequest(string CurrentPassword, string NewPassword);

    /// <summary>
    /// Change password while signed in (required when <see cref="User.MustChangePassword"/> is true).
    /// </summary>
    /// <param name="req">Current and new password <see cref="SetPasswordRequest"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success payload or validation errors.</returns>
    [Authorize]
    [IgnoreAntiforgeryToken]
    [HttpPost("set-password")]
    public async Task<IActionResult> SetPassword([FromBody] SetPasswordRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.NewPassword) || req.NewPassword.Length < PasswordPolicy.MinLength)
            return BadRequest(new { error = PasswordPolicy.MinLengthMessage });

        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(id, out var userId))
            return Unauthorized();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false);
        if (user is null)
            return NotFound();

        if (string.IsNullOrEmpty(user.PasswordHash))
            return BadRequest(new { error = "Current password is incorrect." });

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

        await _loggerService.AddAuditAsync("PasswordChange", user.LoginId, "User changed password", null, ct).ConfigureAwait(false);

        return Ok(new { ok = true });
    }

    /// <summary>
    /// Sign out and clear auth cookies.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Empty OK when sign-out completes.</returns>
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
                await _loggerService.AddAuditAsync("Logoff", auditLoginId, "User logged out", windowsName, ct).ConfigureAwait(false);
        }
        else
        {
            await _loggerService.AddAuditAsync("Logoff", auditLoginId, "User logged out", null, ct).ConfigureAwait(false);
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

    /// <summary>
    /// Keep session alive for idle-timeout tracking.
    /// </summary>
    /// <returns>Empty OK response.</returns>
    [Authorize]
    [IgnoreAntiforgeryToken]
    [HttpPost("session/ping")]
    public IActionResult Ping() => Ok();

    public record RequestPasswordChangeRequest(string Email);

    /// <summary>
    /// Email a password reset link for the given address.
    /// </summary>
    /// <param name="req">Email request <see cref="RequestPasswordChangeRequest"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Generic success (no email enumeration).</returns>
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [HttpPost("request-password-change")]
    public async Task<IActionResult> RequestPasswordChange([FromBody] RequestPasswordChangeRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Email))
            return BadRequest();
        var link = await _passwordReset.CreateResetTokenAsync(req.Email.Trim(), ct).ConfigureAwait(false);
        if (link is not null)
        {
            await _email.SendAsync(req.Email.Trim(), "Help Desk password reset",
                $"Use this one-time link to set a new password:\r\n{link}", ct).ConfigureAwait(false);
        }

        return Ok(new { sent = true });
    }

    public record RequestPasswordResetByLoginRequest(string LoginId);

    /// <summary>
    /// Look up email by login id and send a reset link valid for 10 minutes.
    /// </summary>
    /// <param name="req">Login id payload <see cref="RequestPasswordResetByLoginRequest"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Generic success payload.</returns>
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

    /// <summary>
    /// Submit forgot-password by form post and redirect with status query keys.
    /// </summary>
    /// <remarks>Antiforgery tokens cannot be rendered in interactive-mode Blazor; SameSite=Lax cookies mitigate cross-site POST.</remarks>
    /// <param name="loginId">Submitted login id.</param>
    /// <param name="redirectAfter">Relative path to return to after handling.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Redirect with forgot-password status.</returns>
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [SkipFetchHeaderCheck]
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

    /// <summary>
    /// Complete password reset using a one-time token.
    /// </summary>
    /// <param name="req">Token and new password <see cref="ResetPasswordRequest"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or validation errors.</returns>
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

    /// <summary>
    /// Complete password reset via form post and redirect to login.
    /// </summary>
    /// <remarks>Antiforgery tokens cannot be rendered in interactive-mode Blazor; SameSite=Lax cookies mitigate cross-site POST. Requires a valid secret token.</remarks>
    /// <param name="token">One-time reset token.</param>
    /// <param name="password">New password.</param>
    /// <param name="confirmPassword">Confirmation of new password.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Redirect to login or back to reset page with errors.</returns>
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [SkipFetchHeaderCheck]
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

        if (password.Length < PasswordPolicy.MinLength)
            return Redirect(QueryHelpers.AddQueryString(back, "resetErr", "short"));

        var (ok, _) = await _passwordReset.ResetPasswordAsync(t, password, ct).ConfigureAwait(false);
        if (!ok)
            return Redirect(QueryHelpers.AddQueryString(back, "resetErr", "invalid"));

        return Redirect("/login?reset=sent");
    }
}
