using System.Net;
using HelpDesk.Tests.Infrastructure;
using HelpDesk.Web.Services;
using static HelpDesk.Tests.Infrastructure.AppFixture;

namespace HelpDesk.Tests.Security;

[Collection(AppCollection.Name)]
[Trait(Category.Name, Category.Security)]
public sealed class LoginTests
{
    private readonly AppFixture _fx;

    public LoginTests(AppFixture fx) => _fx = fx;

    [Fact]
    public async Task Correct_credentials_sign_in_and_issue_cookie()
    {
        var c = _fx.NewClient();
        var r = await LoginRequestAsync(c, "READER", Pw);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.NotNull(AuthCookie(r));
        var body = await r.Content.ReadAsStringAsync();
        Assert.Contains("\"ok\":true", body);
        Assert.Contains("\"mustChangePassword\":false", body);
    }

    [Fact]
    public async Task Auth_cookie_is_secure_httponly_and_samesite()
    {
        var c = _fx.NewClient();
        var r = await LoginRequestAsync(c, "READER", Pw);
        var raw = RawSetCookie(r, AuthCookieNames.Auth)!.ToLowerInvariant();

        Assert.Contains("secure", raw);
        Assert.Contains("httponly", raw);
        Assert.Contains("samesite=lax", raw);
    }

    [Fact]
    public async Task Wrong_password_is_rejected_without_cookie()
    {
        var c = _fx.NewClient();
        var r = await LoginRequestAsync(c, "EDITOR", "not-the-password");

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Null(AuthCookie(r));
    }

    [Fact]
    public async Task Unknown_user_and_wrong_password_look_identical_to_the_caller()
    {
        var c = _fx.NewClient();
        var unknown = await LoginRequestAsync(c, Unique("GHOST"), "whatever1!");
        var wrongPw = await LoginRequestAsync(c, "NOPERM", "whatever1!");

        Assert.Equal(unknown.StatusCode, wrongPw.StatusCode);
        Assert.Equal(await unknown.Content.ReadAsStringAsync(), await wrongPw.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Inactive_user_cannot_sign_in()
    {
        var r = await LoginRequestAsync(_fx.NewClient(), "INACTIVE", Pw);
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Null(AuthCookie(r));
    }

    [Fact]
    public async Task Group_rows_cannot_sign_in_with_a_password()
    {
        // A Group row that somehow carries a password hash must still be refused.
        var login = Unique("GRP");
        await _fx.AddUserAsync(login, type: HelpDesk.Models.UserType.Group);
        await using (var db = _fx.NewDb())
        {
            var g = db.Users.Single(u => u.LoginId == login);
            g.PasswordHash = _fx.Hasher.HashPassword(g, Pw);
            await db.SaveChangesAsync();
        }

        var r = await LoginRequestAsync(_fx.NewClient(), login, Pw);
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task User_without_password_hash_cannot_sign_in()
    {
        var login = Unique("NOPW");
        await _fx.AddUserAsync(login, password: null);
        var r = await LoginRequestAsync(_fx.NewClient(), login, "");
        Assert.NotEqual(HttpStatusCode.OK, r.StatusCode);
        var r2 = await LoginRequestAsync(_fx.NewClient(), login, "anything");
        Assert.Equal(HttpStatusCode.Unauthorized, r2.StatusCode);
    }

    [Theory]
    [InlineData("reader")]
    [InlineData("Reader")]
    [InlineData(" READ ER ")]
    public async Task Login_id_is_case_insensitive_and_ignores_whitespace(string typed)
    {
        var r = await LoginRequestAsync(_fx.NewClient(), typed, Pw);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Password_is_case_sensitive()
    {
        var r = await LoginRequestAsync(_fx.NewClient(), "READER", Pw.ToUpperInvariant());
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Theory]
    [InlineData("", "x")]
    [InlineData("READER", "")]
    [InlineData("   ", "   ")]
    public async Task Missing_fields_are_a_bad_request(string login, string password)
    {
        var r = await LoginRequestAsync(_fx.NewClient(), login, password);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Json_login_without_fetch_header_is_refused()
    {
        // CSRF defence: cross-site forms cannot add custom headers.
        var r = await PostJsonAsync(_fx.NewClient(), "/api/account/login", new { loginId = "READER", password = Pw }, fetchHeader: false);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Null(AuthCookie(r));
    }

    [Theory]
    [InlineData("' OR '1'='1")]
    [InlineData("ADMIN1'--")]
    [InlineData("READER\";DROP TABLE Users;--")]
    public async Task Sql_injection_in_login_id_does_not_sign_in(string login)
    {
        var r = await LoginRequestAsync(_fx.NewClient(), login, Pw);
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        await using var db = _fx.NewDb();
        Assert.True(db.Users.Any()); // table still there
    }

    [Theory]
    [InlineData("https://evil.example/steal")]
    [InlineData("//evil.example/steal")]
    [InlineData("/\\evil.example/steal")]
    [InlineData("javascript:alert(1)")]
    public async Task Json_login_never_returns_an_external_return_url(string returnUrl)
    {
        var r = await LoginRequestAsync(_fx.NewClient(), "READER", Pw, returnUrl);
        var body = await r.Content.ReadAsStringAsync();
        Assert.Contains("\"returnUrl\":\"/\"", body);
    }

    [Fact]
    public async Task Json_login_keeps_a_local_return_url()
    {
        var r = await LoginRequestAsync(_fx.NewClient(), "READER", Pw, "/user/books/books");
        Assert.Contains("/user/books/books", await r.Content.ReadAsStringAsync());
    }

    // ── form login ──────────────────────────────────────────────────

    [Fact]
    public async Task Form_login_success_redirects_home_with_cookie()
    {
        var r = await PostFormAsync(_fx.NewClient(), "/api/account/login-form", ("loginId", "READER"), ("password", Pw));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal("/", r.Headers.Location!.OriginalString);
        Assert.NotNull(AuthCookie(r));
    }

    [Fact]
    public async Task Form_login_failure_redirects_back_to_login_without_cookie()
    {
        var r = await PostFormAsync(_fx.NewClient(), "/api/account/login-form", ("loginId", "EDITOR"), ("password", "nope"));
        Assert.Equal("/login?loginError=invalid", r.Headers.Location!.OriginalString);
        Assert.Null(AuthCookie(r));
    }

    [Fact]
    public async Task Form_login_with_missing_fields_reports_required()
    {
        var r = await PostFormAsync(_fx.NewClient(), "/api/account/login-form", ("loginId", ""), ("password", ""));
        Assert.Equal("/login?loginError=required", r.Headers.Location!.OriginalString);
    }

    [Theory]
    [InlineData("https://evil.example/steal")]
    [InlineData("//evil.example/steal")]
    [InlineData("/\\evil.example/steal")]
    public async Task Form_login_never_redirects_off_site(string returnUrl)
    {
        var r = await PostFormAsync(_fx.NewClient(), "/api/account/login-form",
            ("loginId", "READER"), ("password", Pw), ("returnUrl", returnUrl));
        Assert.Equal("/", r.Headers.Location!.OriginalString);
    }

    // ── lockout ─────────────────────────────────────────────────────

    [Fact]
    public async Task Five_failures_lock_the_account_even_for_the_right_password()
    {
        var login = Unique("LOCK");
        await _fx.AddUserAsync(login);
        var c = _fx.NewClient();

        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginRequestAsync(c, login, "bad" + i)).StatusCode);

        var blocked = await LoginRequestAsync(c, login, Pw);
        Assert.Equal(HttpStatusCode.Unauthorized, blocked.StatusCode);
        Assert.Null(AuthCookie(blocked));
        Assert.Contains("Too many failed attempts", await blocked.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Lockout_cannot_be_dodged_by_changing_case_or_adding_spaces()
    {
        var login = Unique("LOCK");
        await _fx.AddUserAsync(login);
        var c = _fx.NewClient();

        var variants = new[] { login.ToLowerInvariant(), login, " " + login + " ", login.ToLowerInvariant(), login };
        foreach (var v in variants)
            await LoginRequestAsync(c, v, "bad");

        var blocked = await LoginRequestAsync(c, login, Pw);
        Assert.Null(AuthCookie(blocked));
    }

    [Fact]
    public async Task Four_failures_then_success_resets_the_counter()
    {
        var login = Unique("LOCK");
        await _fx.AddUserAsync(login);
        var c = _fx.NewClient();

        for (var i = 0; i < 4; i++) await LoginRequestAsync(c, login, "bad");
        Assert.Equal(HttpStatusCode.OK, (await LoginRequestAsync(c, login, Pw)).StatusCode);
        for (var i = 0; i < 4; i++) await LoginRequestAsync(c, login, "bad");
        Assert.Equal(HttpStatusCode.OK, (await LoginRequestAsync(c, login, Pw)).StatusCode);
    }

    [Fact]
    public async Task Lockout_of_one_account_does_not_affect_another()
    {
        var victim = Unique("LOCK");
        await _fx.AddUserAsync(victim);
        var c = _fx.NewClient();
        for (var i = 0; i < 6; i++) await LoginRequestAsync(c, victim, "bad");

        Assert.Equal(HttpStatusCode.OK, (await LoginRequestAsync(c, "READER", Pw)).StatusCode);
    }

    [Fact]
    public async Task Form_login_reports_locked_after_too_many_failures()
    {
        var login = Unique("LOCK");
        await _fx.AddUserAsync(login);
        var c = _fx.NewClient();
        for (var i = 0; i < 5; i++)
            await PostFormAsync(c, "/api/account/login-form", ("loginId", login), ("password", "bad"));

        var r = await PostFormAsync(c, "/api/account/login-form", ("loginId", login), ("password", Pw));
        Assert.Equal("/login?loginError=locked", r.Headers.Location!.OriginalString);
        Assert.Null(AuthCookie(r));
    }

    [Fact]
    public async Task Unknown_login_ids_are_rate_limited_too()
    {
        // Otherwise the lockout message would reveal which accounts exist.
        var ghost = Unique("GHOST");
        var c = _fx.NewClient();
        for (var i = 0; i < 5; i++) await LoginRequestAsync(c, ghost, "bad");
        var r = await LoginRequestAsync(c, ghost, "bad");
        Assert.Contains("Too many failed attempts", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    [Trait(Category.Name, Category.KnownIssue)]
    public async Task Malformed_login_body_is_a_400_not_a_server_error()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/account/login")
        {
            Content = new StringContent("null", System.Text.Encoding.UTF8, "application/json")
        };
        req.Headers.Add("X-Requested-With", "fetch");
        var r = await _fx.NewClient().SendAsync(req);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }
}
