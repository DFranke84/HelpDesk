using System.Net;
using System.Text.RegularExpressions;
using HelpDesk.Models;
using HelpDesk.Tests.Infrastructure;
using HelpDesk.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static HelpDesk.Tests.Infrastructure.AppFixture;

namespace HelpDesk.Tests.Security;

/// <summary>Who may reach what: anonymous visitors, ordinary users, administrators.</summary>
[Collection(AppCollection.Name)]
[Trait(Category.Name, Category.Security)]
public sealed class AccessControlTests
{
    private readonly AppFixture _fx;

    public AccessControlTests(AppFixture fx) => _fx = fx;

    private sealed record Route(string Method, string Path, bool Anonymous, bool AdminOnly);

    /// <summary>Every route the running app actually exposes (controllers + Razor pages), read from the live routing table.</summary>
    private List<Route> AllRoutes()
    {
        var sources = _fx.Factory.Services.GetServices<EndpointDataSource>();
        var list = new List<Route>();
        foreach (var e in sources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>())
        {
            var raw = (e.RoutePattern.RawText ?? "").Trim('/');
            if (raw.StartsWith('_') || raw.Contains("{*"))
                continue; // Blazor framework plumbing and catch-alls
            var path = "/" + Regex.Replace(raw, @"\{[^}]+\}", "1");
            var anonymous = e.Metadata.GetMetadata<IAllowAnonymous>() is not null;
            var adminOnly = e.Metadata.GetOrderedMetadata<IAuthorizeData>()
                .Any(a => (a.Roles ?? "").Split(',').Any(r => r.Trim() == "Admin"));
            var methods = e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? new[] { "GET" };
            foreach (var m in methods.Where(m => m is "GET" or "POST"))
                list.Add(new Route(m, path, anonymous, adminOnly));
        }
        return list.Distinct().ToList();
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient c, Route r)
    {
        var req = new HttpRequestMessage(new HttpMethod(r.Method), r.Path);
        req.Headers.Add("X-Requested-With", "fetch");
        req.Headers.Add("Accept", "text/html,application/json");
        if (r.Method == "POST")
            req.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        return c.SendAsync(req);
    }

    // ── anonymous surface ───────────────────────────────────────────

    [Fact]
    public void Routing_table_was_discovered()
    {
        var routes = AllRoutes();
        Assert.Contains(routes, r => r.Path == "/api/grid/admin/users");
        Assert.Contains(routes, r => r.Path == "/admin/users");
        Assert.Contains(routes, r => r.Path == "/user/books/books");
        Assert.True(routes.Count >= 30, "Expected at least 30 routes, found " + routes.Count);
    }

    [Fact]
    public void Only_the_expected_routes_are_open_to_anonymous_visitors()
    {
        var expected = new[]
        {
            "/access-denied", "/account/reset-password", "/Error", "/login",
            "/api/account/login", "/api/account/login-form",
            "/api/account/request-password-change", "/api/account/request-password-reset-by-login",
            "/api/account/request-password-reset-by-login-form",
            "/api/account/reset-password", "/api/account/reset-password-form",
            "/api/diag/blazor-client"
        };
        var actual = AllRoutes().Where(r => r.Anonymous).Select(r => r.Path).Distinct().OrderBy(p => p, StringComparer.OrdinalIgnoreCase);

        Assert.Equal(expected.OrderBy(p => p, StringComparer.OrdinalIgnoreCase), actual, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Every_protected_route_refuses_anonymous_visitors()
    {
        var c = _fx.NewClient();
        var leaks = new List<string>();
        foreach (var r in AllRoutes().Where(r => !r.Anonymous))
        {
            var resp = await SendAsync(c, r);
            if (!IsDenied(resp))
                leaks.Add($"{r.Method} {r.Path} -> {(int)resp.StatusCode}");
        }
        Assert.True(leaks.Count == 0, "Reachable without signing in:\n" + string.Join("\n", leaks));
    }

    [Fact]
    public async Task Anonymous_page_request_is_sent_to_login_with_return_url()
    {
        var r = await _fx.NewClient().GetAsync("/admin/users");
        Assert.True(IsRedirectTo(r, "/login"));
        Assert.Contains("ReturnUrl=%2Fadmin%2Fusers", r.Headers.Location!.OriginalString);
    }

    [Theory]
    [InlineData("/login")]
    [InlineData("/access-denied")]
    [InlineData("/account/reset-password")]
    public async Task Public_pages_load_without_signing_in(string path)
    {
        var r = await _fx.NewClient().GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Theory]
    [InlineData("/css/helpdesk.css")]
    [InlineData("/js/auth.js")]
    [InlineData("/_framework/blazor.web.js")]
    public async Task Static_assets_needed_by_the_login_page_are_public(string path)
    {
        var r = await _fx.NewClient().GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Theory]
    [InlineData("/appsettings.json")]
    [InlineData("/appsettings.Development.json")]
    [InlineData("/web.config")]
    [InlineData("/logs/helpdesk.log")]
    [InlineData("/.git/config")]
    [InlineData("/HelpDesk.Web.dll")]
    public async Task Configuration_and_binaries_are_never_served(string path)
    {
        var r = await _fx.NewClient().GetAsync(path);
        Assert.NotEqual(HttpStatusCode.OK, r.StatusCode);
    }

    // ── role enforcement ────────────────────────────────────────────

    [Fact]
    public void Every_admin_route_is_marked_admin_only()
    {
        var unguarded = AllRoutes()
            .Where(r => r.Path.StartsWith("/admin", StringComparison.OrdinalIgnoreCase)
                        || r.Path.StartsWith("/api/grid/admin", StringComparison.OrdinalIgnoreCase)
                        || r.Path.Equals("/api/grid/nlog", StringComparison.OrdinalIgnoreCase))
            .Where(r => !r.AdminOnly)
            .Select(r => r.Path)
            .ToList();
        Assert.True(unguarded.Count == 0, "Admin routes without [Authorize(Roles=\"Admin\")]: " + string.Join(", ", unguarded));
    }

    [Fact]
    public async Task Ordinary_user_is_refused_on_every_admin_route()
    {
        var c = await _fx.LoginAsync("READER");
        var leaks = new List<string>();
        foreach (var r in AllRoutes().Where(r => r.AdminOnly))
        {
            var resp = await SendAsync(c, r);
            if (!IsDenied(resp))
                leaks.Add($"{r.Method} {r.Path} -> {(int)resp.StatusCode}");
        }
        Assert.True(leaks.Count == 0, "Admin routes reachable by a normal user:\n" + string.Join("\n", leaks));
    }

    [Fact]
    public async Task Administrator_can_load_every_admin_route()
    {
        var c = await _fx.LoginAsync("ADMIN1");
        var failures = new List<string>();
        foreach (var r in AllRoutes().Where(r => r.AdminOnly && r.Method == "GET"))
        {
            var resp = await SendAsync(c, r);
            if (resp.StatusCode != HttpStatusCode.OK)
                failures.Add($"{r.Path} -> {(int)resp.StatusCode}");
        }
        Assert.True(failures.Count == 0, "Admin could not open:\n" + string.Join("\n", failures));
    }

    [Fact]
    public async Task Signed_in_user_can_load_every_ordinary_page()
    {
        var c = await _fx.LoginAsync("EDITOR");
        var failures = new List<string>();
        foreach (var r in AllRoutes().Where(r => !r.AdminOnly && r.Method == "GET" && !r.Path.StartsWith("/api", StringComparison.OrdinalIgnoreCase)))
        {
            var resp = await SendAsync(c, r);
            // /app/1/page/1 may legitimately redirect; anything 5xx is a crash.
            if ((int)resp.StatusCode >= 500)
                failures.Add($"{r.Path} -> {(int)resp.StatusCode}");
        }
        Assert.True(failures.Count == 0, "Pages that crashed:\n" + string.Join("\n", failures));
    }

    [Fact]
    public async Task Admin_page_html_is_not_rendered_for_a_normal_user()
    {
        var c = await _fx.LoginAsync("READER");
        var r = await c.GetAsync("/admin/users");
        var html = await r.Content.ReadAsStringAsync();
        Assert.DoesNotContain("tbl-admin-users", html);
    }

    [Fact]
    public async Task Admin_grids_return_data_for_admin()
    {
        var c = await _fx.LoginAsync("ADMIN1");
        var users = await c.GetStringAsync("/api/grid/admin/users");
        Assert.Contains("READER", users);
        Assert.Contains("ADMIN1", users);
    }

    [Fact]
    public async Task User_grid_never_exposes_password_hashes()
    {
        var c = await _fx.LoginAsync("ADMIN1");
        var users = await c.GetStringAsync("/api/grid/admin/users");
        Assert.DoesNotContain("passwordHash", users, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AQAAAA", users); // ASP.NET Identity hash prefix
    }

    [Fact]
    public async Task Change_history_never_exposes_password_hashes()
    {
        var c = await _fx.LoginAsync("ADMIN1");
        var history = await c.GetStringAsync("/api/grid/admin/change-history");
        Assert.Contains("User", history);
        Assert.DoesNotContain("PasswordHash", history, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AQAAAA", history);
    }

    [Theory]
    [InlineData("/api/grid/nlog?levels=' OR 1=1--")]
    [InlineData("/api/grid/nlog?activityCategories=x');DROP TABLE NLOG;--")]
    [InlineData("/api/grid/admin/change-history?filterType=%25' OR '1'='1")]
    public async Task Grid_filters_are_not_injectable(string url)
    {
        var c = await _fx.LoginAsync("ADMIN1");
        var r = await c.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("[]", await r.Content.ReadAsStringAsync());
        await using var db = _fx.NewDb();
        _ = await db.NLogEntries.CountAsync(); // table still exists
    }

    [Fact]
    public async Task Nlog_grid_rejects_a_bad_correlation_id()
    {
        var c = await _fx.LoginAsync("ADMIN1");
        var r = await c.GetAsync("/api/grid/nlog?correlationId=not-a-guid");
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    // ── Books data ──────────────────────────────────────────────────

    [Fact]
    public async Task Books_api_serves_a_user_with_read_permission()
    {
        var c = await _fx.LoginAsync("READER");
        var json = await c.GetStringAsync("/api/grid/books/catalog");
        Assert.Contains("Seed Book 1", json);
    }

    [Fact]
    public async Task Books_api_refuses_a_user_outside_the_books_group()
    {
        var c = await _fx.LoginAsync("OUTSIDER");
        var r = await c.GetAsync("/api/grid/books/catalog");
        Assert.True(IsDenied(r), "A user with no access to the Books application downloaded the catalog: " + (int)r.StatusCode);
    }

    [Fact]
    public async Task Books_api_refuses_a_group_member_without_read_permission()
    {
        var c = await _fx.LoginAsync("NOPERM");
        var r = await c.GetAsync("/api/grid/books/catalog");
        Assert.True(IsDenied(r), "A user without Read permission downloaded the catalog: " + (int)r.StatusCode);
    }

    // ── session integrity ───────────────────────────────────────────

    [Fact]
    public async Task Tampered_cookie_is_treated_as_anonymous()
    {
        var login = await LoginRequestAsync(_fx.NewClient(), "ADMIN1", Pw);
        var cookie = AuthCookie(login)!;
        var mid = cookie.Length / 2;
        var flipped = cookie[mid] == 'A' ? 'B' : 'A';
        var tampered = cookie[..mid] + flipped + cookie[(mid + 1)..];

        var c = _fx.NewClient();
        UseCookie(c, tampered);
        Assert.True(IsDenied(await c.GetAsync("/api/grid/admin/users")));
    }

    [Fact]
    public async Task Made_up_cookie_is_treated_as_anonymous()
    {
        var c = _fx.NewClient();
        UseCookie(c, AuthCookieNames.Auth + "=CfDJ8FakeFakeFakeFakeFake");
        Assert.True(IsDenied(await c.GetAsync("/api/grid/admin/users")));
    }

    [Fact]
    public async Task Cookie_from_another_app_instance_with_different_keys_is_rejected()
    {
        // Cookies are encrypted with this machine/app's data-protection keys; a plain-text "role=Admin" style cookie cannot work.
        var c = _fx.NewClient();
        UseCookie(c, AuthCookieNames.Auth + "=" + Convert.ToBase64String("role=Admin;id=1"u8.ToArray()));
        Assert.True(IsDenied(await c.GetAsync("/api/grid/admin/users")));
    }

    [Fact]
    public async Task Deactivating_a_user_ends_their_existing_session()
    {
        var login = Unique("STALE");
        var u = await _fx.AddUserAsync(login);
        await _fx.AddToGroupAsync(u.Id, _fx.BooksGroup.Id);
        var c = await _fx.LoginAsync(login);
        Assert.Equal(HttpStatusCode.OK, (await PostJsonAsync(c, "/api/account/session/ping", new { })).StatusCode);

        await using (var db = _fx.NewDb())
        {
            (await db.Users.SingleAsync(x => x.Id == u.Id)).IsActive = false;
            await db.SaveChangesAsync();
        }

        var after = await PostJsonAsync(c, "/api/account/session/ping", new { });
        Assert.True(IsDenied(after), "A deactivated user's cookie still works: " + (int)after.StatusCode);
    }

    [Fact]
    public async Task Demoting_an_admin_removes_admin_access_from_their_existing_session()
    {
        var login = Unique("EXADMIN");
        var u = await _fx.AddUserAsync(login, UserRole.Admin);
        var c = await _fx.LoginAsync(login);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/grid/admin/users")).StatusCode);

        await using (var db = _fx.NewDb())
        {
            (await db.Users.SingleAsync(x => x.Id == u.Id)).Role = UserRole.User;
            await db.SaveChangesAsync();
        }

        var after = await c.GetAsync("/api/grid/admin/users");
        Assert.True(IsDenied(after), "A demoted admin can still read the user list: " + (int)after.StatusCode);
    }

    [Fact]
    public async Task Deleting_a_user_ends_their_existing_session()
    {
        var login = Unique("GONE");
        var u = await _fx.AddUserAsync(login);
        var c = await _fx.LoginAsync(login);

        await using (var db = _fx.NewDb())
        {
            db.Users.Remove(await db.Users.SingleAsync(x => x.Id == u.Id));
            await db.SaveChangesAsync();
        }

        var after = await PostJsonAsync(c, "/api/account/session/ping", new { });
        Assert.True(IsDenied(after), "A deleted user's cookie still works: " + (int)after.StatusCode);
    }

    // ── logout ──────────────────────────────────────────────────────

    [Fact]
    public async Task Logout_clears_the_cookie_and_suppresses_windows_auto_login()
    {
        var c = await _fx.LoginAsync("READER");
        var r = await PostJsonAsync(c, "/api/account/logout", null);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var authSet = RawSetCookie(r, AuthCookieNames.Auth)!;
        Assert.StartsWith(AuthCookieNames.Auth + "=;", authSet);
        Assert.Contains("expires=Thu, 01 Jan 1970", authSet);
        var suppress = RawSetCookie(r, AuthCookieNames.SuppressWindowsAutoLogin)!;
        Assert.Contains("httponly", suppress.ToLowerInvariant());
        Assert.Contains("secure", suppress.ToLowerInvariant());
    }

    [Fact]
    public async Task Idle_logout_does_not_suppress_windows_auto_login()
    {
        var c = await _fx.LoginAsync("READER");
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/account/logout");
        req.Headers.Add("X-Requested-With", "fetch");
        req.Headers.Add("X-Helpdesk-Logout-Reason", "idle");
        var r = await c.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Null(RawSetCookie(r, AuthCookieNames.SuppressWindowsAutoLogin));
    }

    [Fact]
    public async Task Logout_requires_the_fetch_header()
    {
        // Stops another site from logging the user out with a hidden form.
        var c = await _fx.LoginAsync("READER");
        var r = await c.PostAsync("/api/account/logout", null);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Cookie_captured_before_logout_no_longer_works_after_logout()
    {
        var login = Unique("REPLAY");
        await _fx.AddUserAsync(login);
        var c = await _fx.LoginAsync(login);
        await PostJsonAsync(c, "/api/account/logout", null);

        // Same cookie replayed (e.g. copied from a shared PC before the user logged out).
        var after = await PostJsonAsync(c, "/api/account/session/ping", new { });
        Assert.True(IsDenied(after), "The old cookie is still accepted after logout: " + (int)after.StatusCode);
    }

    [Fact]
    public async Task Session_ping_works_when_signed_in()
    {
        var c = await _fx.LoginAsync("READER");
        Assert.Equal(HttpStatusCode.OK, (await PostJsonAsync(c, "/api/account/session/ping", new { })).StatusCode);
    }
}
