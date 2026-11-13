using System.Net;
using System.Security.Principal;
using System.Text.Json;
using HelpDesk.Models;
using HelpDesk.Tests.Infrastructure;
using HelpDesk.Web.Services;
using Microsoft.EntityFrameworkCore;
using static HelpDesk.Tests.Infrastructure.AppFixture;

namespace HelpDesk.Tests.Security;

/// <summary>Windows auto-login (with a stand-in for the Negotiate handshake).</summary>
[Collection(AppCollection.Name)]
[Trait(Category.Name, Category.Security)]
public sealed class WindowsLoginTests
{
    private readonly AppFixture _fx;

    public WindowsLoginTests(AppFixture fx) => _fx = fx;

    private static HttpRequestMessage Get(string path, string? windows, string? cookie = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Add("Accept", "text/html");
        if (windows is not null) req.Headers.Add(FakeNegotiateHandler.Header, windows);
        if (cookie is not null) req.Headers.Add("Cookie", cookie);
        return req;
    }

    [Fact]
    public async Task Domain_user_with_matching_profile_is_signed_in_automatically()
    {
        var sam = @"TESTDOM\" + Unique("win");
        await _fx.AddUserAsync(Unique("WINUSER"), sam: sam);
        var c = _fx.NewClient();

        var r = await c.SendAsync(Get("/dashboard?x=1", "name:" + sam));

        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal("/dashboard?x=1", r.Headers.Location!.OriginalString);
        var cookie = AuthCookie(r);
        Assert.NotNull(cookie);
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Get("/dashboard", null, cookie))).StatusCode);
    }

    [Fact]
    public async Task Sam_account_match_is_case_insensitive()
    {
        var sam = @"TESTDOM\" + Unique("Mixed");
        await _fx.AddUserAsync(Unique("WINUSER"), sam: sam.ToUpperInvariant());
        var r = await _fx.NewClient().SendAsync(Get("/", "name:" + sam.ToLowerInvariant()));
        Assert.NotNull(AuthCookie(r));
    }

    [Fact]
    public async Task Upn_style_name_matches_the_upn_column()
    {
        var upn = Unique("user").ToLowerInvariant() + "@corp.test";
        await _fx.AddUserAsync(Unique("WINUSER"), upnOrSid: upn);
        var r = await _fx.NewClient().SendAsync(Get("/", "name:" + upn));
        Assert.NotNull(AuthCookie(r));
    }

    [Fact]
    public async Task Domain_user_without_a_profile_is_not_signed_in()
    {
        var r = await _fx.NewClient().SendAsync(Get("/", @"name:TESTDOM\" + Unique("stranger")));
        Assert.Null(AuthCookie(r));
        Assert.True(IsRedirectTo(r, "/login"));
    }

    [Fact]
    public async Task Inactive_profile_is_not_signed_in_by_windows()
    {
        var sam = @"TESTDOM\" + Unique("off");
        await _fx.AddUserAsync(Unique("WINUSER"), sam: sam, active: false);
        var r = await _fx.NewClient().SendAsync(Get("/", "name:" + sam));
        Assert.Null(AuthCookie(r));
    }

    [Fact]
    public async Task Windows_user_cannot_impersonate_by_matching_a_login_id_only()
    {
        // Matching is on the Domain SAM / UPN columns, not on LoginID - otherwise DOMAIN\ADMIN1 could become ADMIN1.
        var r = await _fx.NewClient().SendAsync(Get("/api/grid/admin/users", "name:ADMIN1"));
        Assert.Null(AuthCookie(r));
        Assert.True(IsDenied(r));
    }

    [Fact]
    public async Task Manual_logout_cookie_suppresses_auto_login()
    {
        var sam = @"TESTDOM\" + Unique("win");
        await _fx.AddUserAsync(Unique("WINUSER"), sam: sam);
        var r = await _fx.NewClient().SendAsync(Get("/", "name:" + sam, AuthCookieNames.SuppressWindowsAutoLogin + "=1"));
        Assert.Null(AuthCookie(r));
        Assert.True(IsRedirectTo(r, "/login"));
    }

    [Fact]
    public async Task Windows_session_cannot_use_password_change_for_a_group_login()
    {
        // Sign in as the real Windows account running the tests, bound to a Group row (no person row).
        var (sid, _) = FirstTokenGroup();
        var groupLogin = Unique("WINGRP");
        var g = await _fx.AddUserAsync(groupLogin, type: UserType.Group, upnOrSid: sid);
        try
        {
            var c = _fx.NewClient();
            var r = await c.SendAsync(Get("/", "current"));
            var cookie = AuthCookie(r);
            Assert.NotNull(cookie);
            UseCookie(c, cookie!);

            var set = await PostJsonAsync(c, "/api/account/set-password", new { currentPassword = "", newPassword = "Whatever#2026" });
            Assert.Equal(HttpStatusCode.BadRequest, set.StatusCode);
        }
        finally { await DeactivateAsync(g.Id); }
    }

    [Fact]
    public async Task Windows_group_with_user_role_gets_no_admin_access()
    {
        var (sid, _) = FirstTokenGroup();
        var g = await _fx.AddUserAsync(Unique("WINGRP"), type: UserType.Group, upnOrSid: sid, role: UserRole.User);
        try
        {
            var c = _fx.NewClient();
            var r = await c.SendAsync(Get("/", "current"));
            UseCookie(c, AuthCookie(r)!);
            Assert.True(IsDenied(await c.GetAsync("/api/grid/admin/users")));
        }
        finally { await DeactivateAsync(g.Id); }
    }

    [Fact]
    public async Task Windows_group_with_admin_role_grants_admin_access()
    {
        var (sid, _) = FirstTokenGroup();
        var g = await _fx.AddUserAsync(Unique("WINGRP"), type: UserType.Group, upnOrSid: sid, role: UserRole.Admin);
        try
        {
            var c = _fx.NewClient();
            var r = await c.SendAsync(Get("/", "current"));
            UseCookie(c, AuthCookie(r)!);
            Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/grid/admin/users")).StatusCode);
        }
        finally { await DeactivateAsync(g.Id); }
    }

    [Fact]
    public async Task Inactive_windows_group_row_grants_nothing()
    {
        var (sid, _) = FirstTokenGroup();
        await _fx.AddUserAsync(Unique("WINGRP"), type: UserType.Group, upnOrSid: sid, role: UserRole.Admin, active: false);
        var r = await _fx.NewClient().SendAsync(Get("/api/grid/admin/users", "current"));
        // Other tests may have an active group row for a moment; what matters is that THIS inactive admin row never yields admin data.
        if (AuthCookie(r) is { } cookie)
        {
            var c = _fx.NewClient();
            UseCookie(c, cookie);
            Assert.True(IsDenied(await c.GetAsync("/api/grid/admin/users")));
        }
    }

    [Fact]
    [Trait(Category.Name, Category.KnownIssue)]
    public async Task Windows_auto_login_never_redirects_off_site()
    {
        var sam = @"TESTDOM\" + Unique("win");
        await _fx.AddUserAsync(Unique("WINUSER"), sam: sam);

        // A link such as https://helpdesk//evil.example/x sent to a domain user.
        var req = Get("/", "name:" + sam);
        req.RequestUri = new Uri("https://localhost//evil.example/x");
        var r = await _fx.NewClient().SendAsync(req);
        Assert.NotNull(AuthCookie(r)); // the sign-in itself happened

        var location = r.Headers.Location?.OriginalString ?? "";
        Assert.False(location.StartsWith("//", StringComparison.Ordinal), "Redirected to another site: " + location);
    }

    [Fact]
    public async Task Negotiate_challenge_is_offered_on_pages_but_not_on_login_or_api()
    {
        using var factory = _fx.WithSettings(("App:WindowsAutoLoginChallenge", "true"));
        var c = _fx.NewClient(factory);

        var page = await c.SendAsync(Get("/dashboard", null));
        Assert.Equal(HttpStatusCode.Unauthorized, page.StatusCode);
        Assert.Contains("Negotiate", page.Headers.WwwAuthenticate.ToString());

        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Get("/login", null))).StatusCode);

        var api = await c.SendAsync(Get("/api/grid/admin/users", null));
        Assert.DoesNotContain("Negotiate", api.Headers.WwwAuthenticate.ToString());

        var suppressed = await c.SendAsync(Get("/dashboard", null, AuthCookieNames.SuppressWindowsAutoLogin + "=1"));
        Assert.True(IsRedirectTo(suppressed, "/login"));
    }

    private static (string Sid, string Name) FirstTokenGroup()
    {
        var id = WindowsIdentity.GetCurrent();
        var sid = id.Groups!.Select(g => g.Value).FirstOrDefault(v => v == "S-1-5-32-545") // BUILTIN\Users
                  ?? id.Groups!.First().Value;
        return (sid, sid);
    }

    private async Task DeactivateAsync(int userId)
    {
        await using var db = _fx.NewDb();
        (await db.Users.SingleAsync(u => u.Id == userId)).IsActive = false;
        await db.SaveChangesAsync();
    }
}

/// <summary>Transport, headers, error handling, configuration hygiene.</summary>
[Collection(AppCollection.Name)]
[Trait(Category.Name, Category.Security)]
public sealed class HardeningTests
{
    private readonly AppFixture _fx;

    public HardeningTests(AppFixture fx) => _fx = fx;

    [Theory]
    [InlineData("/login")]
    [InlineData("/api/account/session/ping")]
    [InlineData("/css/helpdesk.css")]
    public async Task Plain_http_is_never_served(string path)
    {
        var c = _fx.NewClient(baseAddress: "http://localhost");
        var r = await c.GetAsync(path);
        Assert.True(r.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect or HttpStatusCode.MovedPermanently,
            $"HTTP request answered with {(int)r.StatusCode}");
    }

    [Fact]
    public async Task Requests_for_an_unexpected_host_name_are_rejected()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/login");
        req.Headers.Host = "evil.example";
        var r = await _fx.NewClient().SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    [Trait(Category.Name, Category.KnownIssue)]
    public async Task Reset_link_does_not_trust_the_host_header()
    {
        // appsettings.Development.json sets AllowedHosts "*"; the same happens in production if AllowedHosts is loosened.
        using var factory = _fx.WithSettings(("AllowedHosts", "*"), ("App:BaseUrl", "https://helpdesk.test"));
        var login = Unique("RST");
        var email = login.ToLowerInvariant() + "@test.local";
        await _fx.AddUserAsync(login, email: email);

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/account/request-password-reset-by-login")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { loginId = login })
        };
        req.Headers.Add("X-Requested-With", "fetch");
        req.Headers.Host = "evil.example";
        await _fx.NewClient(factory).SendAsync(req);

        var mail = Assert.Single(_fx.Email.To(email));
        Assert.DoesNotContain("evil.example", mail.Body);
    }

    [Fact]
    [Trait(Category.Name, Category.KnownIssue)]
    public async Task Responses_carry_basic_security_headers()
    {
        var r = await _fx.NewClient().GetAsync("/login");
        var missing = new List<string>();
        if (!r.Headers.Contains("X-Content-Type-Options")) missing.Add("X-Content-Type-Options: nosniff");
        var csp = r.Headers.TryGetValues("Content-Security-Policy", out var v) ? string.Join(";", v) : "";
        if (!r.Headers.Contains("X-Frame-Options") && !csp.Contains("frame-ancestors")) missing.Add("X-Frame-Options / CSP frame-ancestors (clickjacking)");
        if (csp.Length == 0) missing.Add("Content-Security-Policy");
        Assert.True(missing.Count == 0, "Missing: " + string.Join("; ", missing));
    }

    [Fact]
    public async Task Server_errors_do_not_leak_stack_traces_outside_development()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/account/login")
        {
            Content = new StringContent("null", System.Text.Encoding.UTF8, "application/json")
        };
        req.Headers.Add("X-Requested-With", "fetch");
        var r = await _fx.NewClient().SendAsync(req);
        var body = await r.Content.ReadAsStringAsync();

        Assert.DoesNotContain("   at ", body);
        Assert.DoesNotContain("NullReferenceException", body);
        Assert.DoesNotContain("HelpDesk.Web.Controllers", body);
    }

    [Fact]
    public async Task Browser_diagnostics_endpoint_is_disabled_outside_development()
    {
        var r = await PostJsonAsync(_fx.NewClient(), "/api/diag/blazor-client", new { spam = "x" });
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task Login_page_does_not_reflect_script_from_the_query_string()
    {
        var r = await _fx.NewClient().GetAsync("/login?loginError=%3Cscript%3Ealert(1)%3C/script%3E&returnUrl=%22%3E%3Cscript%3Ealert(2)%3C/script%3E");
        var html = await r.Content.ReadAsStringAsync();
        Assert.DoesNotContain("<script>alert(", html);
    }

    [Fact]
    public async Task Antiforgery_and_auth_cookies_are_never_sent_without_secure_flag()
    {
        var r = await LoginRequestAsync(_fx.NewClient(), "READER", Pw);
        foreach (var setCookie in r.Headers.GetValues("Set-Cookie").Where(v => !v.Contains("expires=Thu, 01 Jan 1970")))
            Assert.Contains("secure", setCookie.ToLowerInvariant());
    }

    // ── repository hygiene ──────────────────────────────────────────

    private static JsonElement ReadSettings(string file)
    {
        var path = Path.Combine(RepoRoot(), "HelpDesk.Web", file);
        var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        return doc.RootElement;
    }

    private static string? Value(JsonElement root, params string[] path)
    {
        var e = root;
        foreach (var p in path)
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(p, out e))
                return null;
        return e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString();
    }

    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("appsettings.Development.json")]
    public void No_smtp_password_is_committed_to_source_control(string file)
    {
        var root = ReadSettings(file);
        Assert.True(string.IsNullOrEmpty(Value(root, "Smtp", "Direct", "Password")), $"{file} contains a real SMTP password (Smtp:Direct:Password).");
        Assert.True(string.IsNullOrEmpty(Value(root, "Smtp", "Standard", "Password")), $"{file} contains a real SMTP password (Smtp:Standard:Password).");
    }

    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("appsettings.Development.json")]
    public void Connection_strings_in_source_control_carry_no_sql_password(string file)
    {
        var root = ReadSettings(file);
        foreach (var name in new[] { "HelpDesk", "Books" })
        {
            var cs = Value(root, "ConnectionStrings", name) ?? "";
            Assert.DoesNotContain("password", cs, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("pwd=", cs, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Production_settings_restrict_allowed_hosts()
    {
        Assert.NotEqual("*", Value(ReadSettings("appsettings.json"), "AllowedHosts"));
    }

    [Fact]
    public void Production_settings_do_not_enable_detailed_errors()
    {
        var v = Value(ReadSettings("appsettings.json"), "DetailedErrors");
        Assert.True(v is null || v.Equals("false", StringComparison.OrdinalIgnoreCase));
    }
}
