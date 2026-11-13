using System.Net;
using System.Security.Cryptography;
using System.Text;
using HelpDesk.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static HelpDesk.Tests.Infrastructure.AppFixture;

namespace HelpDesk.Tests.Security;

/// <summary>Forced first-login change, change-password, and the forgot/reset-password flow.</summary>
[Collection(AppCollection.Name)]
[Trait(Category.Name, Category.Security)]
public sealed class PasswordTests
{
    private readonly AppFixture _fx;

    public PasswordTests(AppFixture fx) => _fx = fx;

    private static Task<HttpResponseMessage> SetPasswordAsync(HttpClient c, string current, string next, bool fetchHeader = true) =>
        PostJsonAsync(c, "/api/account/set-password", new { currentPassword = current, newPassword = next }, fetchHeader);

    // ── must-change-password ────────────────────────────────────────

    [Fact]
    public async Task Seeded_admin_must_change_password_on_first_login()
    {
        var r = await LoginRequestAsync(_fx.NewClient(), "ADMIN", "ChangeMe!123");
        var body = await r.Content.ReadAsStringAsync();
        Assert.Contains("\"mustChangePassword\":true", body);
        Assert.Contains("/account/first-login-password", body);
    }

    [Fact]
    public async Task User_who_must_change_password_is_locked_out_of_everything_else()
    {
        var c = await _fx.LoginAsync("NEWBIE");

        foreach (var path in new[] { "/", "/dashboard", "/user/books/books", "/api/grid/books/catalog", "/account/change-password" })
        {
            var r = await c.GetAsync(path);
            Assert.True(IsRedirectTo(r, "/account/first-login-password"), $"{path} was not blocked: {(int)r.StatusCode}");
        }

        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/account/first-login-password")).StatusCode);
    }

    [Theory]
    [InlineData("/account/first-login-passwordX/../../admin/users")]
    [InlineData("/ACCOUNT/FIRST-LOGIN-PASSWORD/../api/grid/books/catalog")]
    public async Task Must_change_gate_cannot_be_bypassed_with_path_tricks(string path)
    {
        var c = await _fx.LoginAsync("NEWBIE");
        var r = await c.GetAsync(path);
        var body = await r.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Seed Book", body);
        Assert.DoesNotContain("tbl-admin-users", body);
    }

    [Fact]
    public async Task Form_login_sends_must_change_user_to_first_login_page()
    {
        var r = await PostFormAsync(_fx.NewClient(), "/api/account/login-form",
            ("loginId", "NEWBIE"), ("password", Pw), ("returnUrl", "/user/books/books"));
        Assert.StartsWith("/account/first-login-password?returnUrl=", r.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Changing_the_password_lifts_the_gate_and_the_new_password_works()
    {
        var login = Unique("FIRST");
        await _fx.AddUserAsync(login, mustChange: true);
        var c = await _fx.LoginAsync(login);

        var set = await SetPasswordAsync(c, Pw, "BrandNew#2026");
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        UseCookie(c, AuthCookie(set)!);

        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginRequestAsync(_fx.NewClient(), login, Pw)).StatusCode);
        var relog = await LoginRequestAsync(_fx.NewClient(), login, "BrandNew#2026");
        Assert.Contains("\"mustChangePassword\":false", await relog.Content.ReadAsStringAsync());
    }

    // ── set-password ────────────────────────────────────────────────

    [Fact]
    public async Task Set_password_requires_sign_in()
    {
        var r = await SetPasswordAsync(_fx.NewClient(), Pw, "Whatever#2026");
        Assert.True(IsDenied(r));
    }

    [Fact]
    public async Task Set_password_requires_the_current_password()
    {
        var login = Unique("PW");
        await _fx.AddUserAsync(login);
        var c = await _fx.LoginAsync(login);

        var r = await SetPasswordAsync(c, "wrong-current", "Whatever#2026");
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginRequestAsync(_fx.NewClient(), login, Pw)).StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short7!")]
    [InlineData("        ")]
    public async Task Set_password_enforces_minimum_length(string newPassword)
    {
        var login = Unique("PW");
        await _fx.AddUserAsync(login);
        var c = await _fx.LoginAsync(login);

        var r = await SetPasswordAsync(c, Pw, newPassword);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Set_password_without_fetch_header_is_refused()
    {
        var login = Unique("PW");
        await _fx.AddUserAsync(login);
        var c = await _fx.LoginAsync(login);
        var r = await SetPasswordAsync(c, Pw, "Whatever#2026", fetchHeader: false);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Passwords_are_stored_hashed_never_in_clear_text()
    {
        var login = Unique("PW");
        await _fx.AddUserAsync(login);
        var c = await _fx.LoginAsync(login);
        await SetPasswordAsync(c, Pw, "Visible#Secret1");

        await using var db = _fx.NewDb();
        var hash = (await db.Users.SingleAsync(u => u.LoginId == login)).PasswordHash!;
        Assert.DoesNotContain("Visible#Secret1", hash);
        Assert.StartsWith("AQAAAA", hash); // PBKDF2 (ASP.NET Identity v3 format)
    }

    [Fact]
    [Trait(Category.Name, Category.KnownIssue)]
    public async Task Guessing_the_current_password_is_rate_limited()
    {
        // With a stolen session an attacker can try current passwords without limit.
        var login = Unique("PW");
        await _fx.AddUserAsync(login);
        var c = await _fx.LoginAsync(login);

        for (var i = 0; i < 8; i++)
            await SetPasswordAsync(c, "guess" + i, "Whatever#2026");

        var r = await SetPasswordAsync(c, Pw, "Whatever#2026");
        Assert.NotEqual(HttpStatusCode.OK, r.StatusCode);
    }

    // ── forgot / reset ──────────────────────────────────────────────

    private async Task<(string Login, string Email)> NewResettableUserAsync(bool active = true)
    {
        var login = Unique("RST");
        var email = login.ToLowerInvariant() + "@test.local";
        await _fx.AddUserAsync(login, email: email, active: active);
        return (login, email);
    }

    private async Task<string> RequestTokenAsync(string login, string email)
    {
        var before = _fx.Email.To(email).Count;
        var r = await PostJsonAsync(_fx.NewClient(), "/api/account/request-password-reset-by-login", new { loginId = login });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var mails = _fx.Email.To(email);
        Assert.Equal(before + 1, mails.Count);
        return FakeEmailSender.ExtractToken(mails[^1].Body);
    }

    private Task<HttpResponseMessage> ResetAsync(string token, string newPassword) =>
        PostJsonAsync(_fx.NewClient(), "/api/account/reset-password", new { token, newPassword });

    [Fact]
    public async Task Reset_link_is_emailed_and_sets_a_new_password()
    {
        var (login, email) = await NewResettableUserAsync();
        var token = await RequestTokenAsync(login, email);

        Assert.Equal(HttpStatusCode.OK, (await ResetAsync(token, "AfterReset#1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginRequestAsync(_fx.NewClient(), login, "AfterReset#1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginRequestAsync(_fx.NewClient(), login, Pw)).StatusCode);
    }

    [Fact]
    public async Task Reset_link_points_at_this_site()
    {
        var (login, email) = await NewResettableUserAsync();
        await RequestTokenAsync(login, email);
        Assert.Contains("https://localhost/account/reset-password?token=", _fx.Email.To(email)[^1].Body);
    }

    [Fact]
    public async Task Unknown_login_gets_the_same_answer_and_no_email()
    {
        var sentBefore = _fx.Email.Sent.Count(m => m.To.Contains("ghost"));
        var (login, _) = await NewResettableUserAsync();
        var known = await PostJsonAsync(_fx.NewClient(), "/api/account/request-password-reset-by-login", new { loginId = login });
        var unknown = await PostJsonAsync(_fx.NewClient(), "/api/account/request-password-reset-by-login", new { loginId = Unique("GHOST") });

        Assert.Equal(known.StatusCode, unknown.StatusCode);
        Assert.Equal(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
        Assert.Equal(sentBefore, _fx.Email.Sent.Count(m => m.To.Contains("ghost")));
    }

    [Fact]
    public async Task Reset_by_email_address_does_not_reveal_whether_the_address_exists()
    {
        var (_, email) = await NewResettableUserAsync();
        var known = await PostJsonAsync(_fx.NewClient(), "/api/account/request-password-change", new { email });
        var unknown = await PostJsonAsync(_fx.NewClient(), "/api/account/request-password-change", new { email = "nobody-" + Unique() + "@test.local" });

        Assert.Equal(HttpStatusCode.OK, known.StatusCode);
        Assert.Equal(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
        Assert.Single(_fx.Email.To(email));
    }

    [Fact]
    public async Task Inactive_user_gets_no_reset_email()
    {
        var (login, email) = await NewResettableUserAsync(active: false);
        await PostJsonAsync(_fx.NewClient(), "/api/account/request-password-reset-by-login", new { loginId = login });
        Assert.Empty(_fx.Email.To(email));
    }

    [Fact]
    public async Task Reset_token_works_only_once()
    {
        var (login, email) = await NewResettableUserAsync();
        var token = await RequestTokenAsync(login, email);

        Assert.Equal(HttpStatusCode.OK, (await ResetAsync(token, "FirstUse#111")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ResetAsync(token, "SecondUse#222")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginRequestAsync(_fx.NewClient(), login, "FirstUse#111")).StatusCode);
    }

    [Fact]
    public async Task Expired_reset_token_is_rejected()
    {
        var (login, email) = await NewResettableUserAsync();
        var token = await RequestTokenAsync(login, email);
        await using (var db = _fx.NewDb())
        {
            var row = await db.PasswordResetTokens.OrderByDescending(t => t.Id).FirstAsync(t => t.User.LoginId == login);
            row.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await ResetAsync(token, "TooLate#1234")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginRequestAsync(_fx.NewClient(), login, Pw)).StatusCode);
    }

    [Fact]
    public async Task By_login_reset_token_expires_within_ten_minutes()
    {
        var (login, email) = await NewResettableUserAsync();
        await RequestTokenAsync(login, email);
        await using var db = _fx.NewDb();
        var row = await db.PasswordResetTokens.OrderByDescending(t => t.Id).FirstAsync(t => t.User.LoginId == login);
        Assert.InRange((row.ExpiresAt - row.CreatedAt).TotalMinutes, 9.9, 10.1);
    }

    [Fact]
    public async Task Requesting_a_new_link_cancels_the_previous_one()
    {
        var (login, email) = await NewResettableUserAsync();
        var first = await RequestTokenAsync(login, email);
        var second = await RequestTokenAsync(login, email);

        Assert.NotEqual(first, second);
        Assert.Equal(HttpStatusCode.BadRequest, (await ResetAsync(first, "OldLink#1234")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ResetAsync(second, "NewLink#1234")).StatusCode);
    }

    [Theory]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("not-a-token")]
    [InlineData("' OR 1=1--")]
    public async Task Guessed_reset_tokens_are_rejected(string token)
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await ResetAsync(token, "Whatever#2026")).StatusCode);
    }

    [Fact]
    public async Task Reset_enforces_minimum_password_length()
    {
        var (login, email) = await NewResettableUserAsync();
        var token = await RequestTokenAsync(login, email);

        Assert.Equal(HttpStatusCode.BadRequest, (await ResetAsync(token, "short")).StatusCode);
        // the token must survive a rejected attempt
        Assert.Equal(HttpStatusCode.OK, (await ResetAsync(token, "LongEnough#1")).StatusCode);
    }

    [Fact]
    public async Task Reset_tokens_are_long_random_and_stored_only_as_a_hash()
    {
        var (login, email) = await NewResettableUserAsync();
        var token = await RequestTokenAsync(login, email);

        Assert.Equal(64, token.Length); // 256 bits
        await using var db = _fx.NewDb();
        var row = await db.PasswordResetTokens.OrderByDescending(t => t.Id).FirstAsync(t => t.User.LoginId == login);
        Assert.NotEqual(token, row.TokenHash);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))), row.TokenHash);
    }

    [Fact]
    public async Task Reset_clears_the_must_change_flag()
    {
        var login = Unique("RST");
        var email = login.ToLowerInvariant() + "@test.local";
        await _fx.AddUserAsync(login, email: email, mustChange: true);
        var token = await RequestTokenAsync(login, email);
        await ResetAsync(token, "AfterReset#1");

        var r = await LoginRequestAsync(_fx.NewClient(), login, "AfterReset#1");
        Assert.Contains("\"mustChangePassword\":false", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Reset_form_validates_and_redirects()
    {
        var (login, email) = await NewResettableUserAsync();
        var token = await RequestTokenAsync(login, email);
        var c = _fx.NewClient();

        var mismatch = await PostFormAsync(c, "/api/account/reset-password-form", ("token", token), ("password", "Abcdefg#1"), ("confirmPassword", "Different#1"));
        Assert.Contains("resetErr=mismatch", mismatch.Headers.Location!.OriginalString);

        var tooShort = await PostFormAsync(c, "/api/account/reset-password-form", ("token", token), ("password", "abc"), ("confirmPassword", "abc"));
        Assert.Contains("resetErr=short", tooShort.Headers.Location!.OriginalString);

        var missing = await PostFormAsync(c, "/api/account/reset-password-form", ("token", ""), ("password", "Abcdefg#1"), ("confirmPassword", "Abcdefg#1"));
        Assert.Contains("resetErr=missing", missing.Headers.Location!.OriginalString);

        var bad = await PostFormAsync(c, "/api/account/reset-password-form", ("token", "ABCDEF"), ("password", "Abcdefg#1"), ("confirmPassword", "Abcdefg#1"));
        Assert.Contains("resetErr=invalid", bad.Headers.Location!.OriginalString);

        var ok = await PostFormAsync(c, "/api/account/reset-password-form", ("token", token), ("password", "Abcdefg#1"), ("confirmPassword", "Abcdefg#1"));
        Assert.Equal("/login?reset=sent", ok.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await LoginRequestAsync(_fx.NewClient(), login, "Abcdefg#1")).StatusCode);
    }

    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example/")]
    [InlineData("evil.example")]
    public async Task Forgot_password_form_never_redirects_off_site(string redirectAfter)
    {
        var r = await PostFormAsync(_fx.NewClient(), "/api/account/request-password-reset-by-login-form",
            ("loginId", Unique("GHOST")), ("redirectAfter", redirectAfter));
        Assert.StartsWith("/login", r.Headers.Location!.OriginalString);
    }

    [Fact]
    [Trait(Category.Name, Category.KnownIssue)]
    public async Task Forgot_password_form_rejects_backslash_redirect_trick()
    {
        // Browsers treat "/\evil.example" like "//evil.example" (another site).
        var r = await PostFormAsync(_fx.NewClient(), "/api/account/request-password-reset-by-login-form",
            ("loginId", Unique("GHOST")), ("redirectAfter", "/\\evil.example/phish"));
        Assert.StartsWith("/login", r.Headers.Location!.OriginalString);
    }

    [Fact]
    [Trait(Category.Name, Category.KnownIssue)]
    public async Task Reset_emails_are_throttled()
    {
        // Anyone can type a login id; without a limit they can flood that person's inbox.
        var (login, email) = await NewResettableUserAsync();
        var c = _fx.NewClient();
        for (var i = 0; i < 10; i++)
            await PostJsonAsync(c, "/api/account/request-password-reset-by-login", new { loginId = login });

        Assert.True(_fx.Email.To(email).Count <= 3, $"10 requests produced {_fx.Email.To(email).Count} emails.");
    }

    [Fact]
    [Trait(Category.Name, Category.KnownIssue)]
    public async Task Malformed_reset_body_is_a_400_not_a_server_error()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/account/reset-password")
        {
            Content = new StringContent("null", Encoding.UTF8, "application/json")
        };
        req.Headers.Add("X-Requested-With", "fetch");
        var r = await _fx.NewClient().SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }
}
