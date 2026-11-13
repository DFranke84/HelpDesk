using System.Net;
using System.Security.Claims;
using Bunit;
using HelpDesk.Models;
using HelpDesk.Models.Data;
using HelpDesk.Tests.Infrastructure;
using HelpDesk.Web.Components.Pages.Admin;
using HelpDesk.Web.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static HelpDesk.Tests.Infrastructure.AppFixture;

namespace HelpDesk.Tests.Security;

/// <summary>
/// Defects found by clicking through the running application under IIS Express. Each test pins one of them down.
/// </summary>
[Collection(AppCollection.Name)]
[Trait(Category.Name, Category.Security)]
public sealed class ClickThroughRegressionTests
{
    private readonly AppFixture _fx;

    public ClickThroughRegressionTests(AppFixture fx) => _fx = fx;

    // ── mail outage ─────────────────────────────────────────────────

    [Fact]
    public async Task Mail_server_outage_does_not_turn_forgot_password_into_an_error_page()
    {
        // With the mail server down, a real account produced a 500 page while an unknown login got the normal
        // "sent" message, which also revealed which logins exist.
        var login = Unique("RST");
        var email = login.ToLowerInvariant() + "@test.local";
        await _fx.AddUserAsync(login, email: email);
        _fx.Email.FailFor[email] = true;
        try
        {
            var form = await PostFormAsync(_fx.NewClient(), "/api/account/request-password-reset-by-login-form",
                ("loginId", login), ("redirectAfter", "/login"));
            var unknown = await PostFormAsync(_fx.NewClient(), "/api/account/request-password-reset-by-login-form",
                ("loginId", Unique("GHOST")), ("redirectAfter", "/login"));
            Assert.Equal(HttpStatusCode.Redirect, form.StatusCode);
            Assert.Equal(unknown.Headers.Location!.OriginalString, form.Headers.Location!.OriginalString);

            var json = await PostJsonAsync(_fx.NewClient(), "/api/account/request-password-reset-by-login", new { loginId = login });
            Assert.Equal(HttpStatusCode.OK, json.StatusCode);

            var byEmail = await PostJsonAsync(_fx.NewClient(), "/api/account/request-password-change", new { email });
            Assert.Equal(HttpStatusCode.OK, byEmail.StatusCode);
        }
        finally
        {
            _fx.Email.FailFor.TryRemove(email, out _);
        }
    }

    // ── Windows sign-in under IIS ───────────────────────────────────

    [Fact]
    public async Task Form_posts_are_not_hijacked_by_windows_auto_login()
    {
        // A sign-in form post that arrived Windows-authenticated was answered with a redirect to
        // /api/account/login-form (a blank page), signed in as the Windows account instead of the one typed.
        var sam = "TESTDOM\\" + Unique("win");
        await _fx.AddUserAsync(Unique("WINUSER"), sam: sam);
        var typed = await _fx.AddUserAsync(Unique("TYPED"));

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/account/login-form")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["loginId"] = typed.LoginId, ["password"] = Pw })
        };
        req.Headers.Add(FakeNegotiateHandler.Header, "name:" + sam);
        var r = await _fx.NewClient().SendAsync(req);

        Assert.Equal("/", r.Headers.Location!.OriginalString);
        Assert.NotNull(AuthCookie(r));
        // A hijacked post would have redirected back to /api/account/login-form instead of completing the sign-in.
    }

    [Theory]
    [InlineData("GET", "/dashboard")]
    [InlineData("GET", "/api/grid/books/catalog")]
    [InlineData("POST", "/api/account/session/ping")]
    public async Task Windows_identity_without_a_help_desk_session_is_not_signed_in(string method, string path)
    {
        // Under IIS the request already carries the Windows account. That alone must never pass [Authorize]:
        // the visitor has either logged out (auto-login suppressed) or has no Help Desk account at all.
        foreach (var cookie in new[] { AuthCookieNames.SuppressWindowsAutoLogin + "=1", null })
        {
            var req = new HttpRequestMessage(new HttpMethod(method), path);
            req.Headers.Add("Accept", "text/html");
            req.Headers.Add("X-Requested-With", "fetch");
            req.Headers.Add(IisWindowsUserStartupFilter.Header, "TESTDOM\\" + Unique("nobody"));
            if (cookie is not null) req.Headers.Add("Cookie", cookie);

            var r = await _fx.NewClient().SendAsync(req);

            Assert.True(IsDenied(r),
                $"{method} {path} (cookie: {cookie ?? "none"}) answered {(int)r.StatusCode} to a Windows user with no Help Desk session.");
        }
    }

    // ── last administrator ──────────────────────────────────────────

    [Fact]
    public async Task Last_admin_guard_only_fires_for_the_final_active_administrator()
    {
        // Uses its own empty database so the fixture's other administrators do not interfere.
        var cs = _fx.HelpDeskCs.Replace("Database=HelpDeskTests_", "Database=HelpDeskTestsGuard_");
        var factory = new TestDbFactory(cs);
        await using var db = factory.CreateDbContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
        try
        {
            User Make(string login, UserRole role, bool active = true) => new()
            {
                LoginId = login, FirstName = "T", LastName = login, Role = role, IsActive = active, CreatedAt = DateTime.UtcNow
            };
            var only = Make("ONLY", UserRole.Admin);
            var inactiveAdmin = Make("OLDADMIN", UserRole.Admin, active: false);
            var plain = Make("PLAIN", UserRole.User);
            db.Users.AddRange(only, inactiveAdmin, plain);
            await db.SaveChangesAsync();

            Assert.True(await LastAdminGuard.IsLastActiveAdminAsync(db, only.Id));        // an inactive admin does not count
            Assert.False(await LastAdminGuard.IsLastActiveAdminAsync(db, plain.Id));      // not an admin
            Assert.False(await LastAdminGuard.IsLastActiveAdminAsync(db, inactiveAdmin.Id));

            db.Users.Add(Make("SECOND", UserRole.Admin));
            await db.SaveChangesAsync();
            Assert.False(await LastAdminGuard.IsLastActiveAdminAsync(db, only.Id));       // someone else can still administer
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    // ── Groups screen ───────────────────────────────────────────────

    private sealed class FixedAuthState : AuthenticationStateProvider
    {
        private readonly ClaimsPrincipal _user;
        public FixedAuthState(ClaimsPrincipal user) => _user = user;
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(_user));
    }

    private Bunit.TestContext AdminContext()
    {
        var ctx = new Bunit.TestContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.JSInterop.Setup<bool>("confirm", _ => true).SetResult(true); // the admin clicks OK on every prompt
        ctx.Services.AddSingleton<IDbContextFactory<HelpDeskDbContext>>(new TestDbFactory(_fx.HelpDeskCs));
        ctx.Services.AddSingleton<IMenuRefreshNotifier, MenuRefreshNotifier>();
        ctx.Services.AddSingleton<IStatusBarService, StatusBarService>();
        ctx.Services.AddSingleton<IUserValidationService, UserValidationService>();
        ctx.Services.AddSingleton(new PasswordHasher<User>());
        ctx.Services.AddSingleton<AuthenticationStateProvider>(
            new FixedAuthState(HelpDesk.Tests.Services.Principals.For(_fx.Admin1.Id, UserRole.Admin)));
        return ctx;
    }

    [Fact]
    public async Task Deleting_a_group_that_is_linked_to_an_application_works_and_removes_the_links()
    {
        // The prompt promises the links are removed, but the delete failed on a foreign key.
        var (app, group, _) = await _fx.AddAppAsync();
        var member = await _fx.AddUserAsync(Unique());
        await _fx.AddToGroupAsync(member.Id, group.Id);

        using var ctx = AdminContext();
        var cut = ctx.RenderComponent<AdminGroups>();
        await cut.InvokeAsync(() => cut.Instance.PromptDelete(group.Id));

        await using var db = _fx.NewDb();
        Assert.False(await db.Groups.AnyAsync(g => g.Id == group.Id), "The group is still there.");
        Assert.False(await db.DeskApplicationGroups.AnyAsync(l => l.DeskGroupId == group.Id));
        Assert.False(await db.UserGroups.AnyAsync(l => l.GroupId == group.Id));
        Assert.True(await db.Applications.AnyAsync(a => a.Id == app.Id), "The application must be kept.");
        Assert.True(await db.Users.AnyAsync(u => u.Id == member.Id), "The member must be kept.");
    }

    [Fact]
    public async Task A_second_group_with_the_same_name_is_refused()
    {
        var (_, group, _) = await _fx.AddAppAsync();

        using var ctx = AdminContext();
        var cut = ctx.RenderComponent<AdminGroups>();
        await cut.Find("button.hd-btn.primary").ClickAsync(new MouseEventArgs()); // Add
        cut.Find("div.hd-modal input.hd-input").Input(group.Name);
        await cut.FindAll("div.hd-modal-actions button").First(b => b.TextContent == "Save").ClickAsync(new MouseEventArgs());

        Assert.Contains("already exists", cut.Markup);
        await using var db = _fx.NewDb();
        Assert.Equal(1, await db.Groups.CountAsync(g => g.Name == group.Name));
    }

    [Fact]
    public async Task A_second_application_with_the_same_name_is_refused()
    {
        var (app, _, _) = await _fx.AddAppAsync();

        using var ctx = AdminContext();
        var cut = ctx.RenderComponent<AdminApplications>();
        await cut.Find("button.hd-btn.primary").ClickAsync(new MouseEventArgs()); // Add
        cut.Find("div.hd-modal input.hd-input").Input(app.Name);
        await cut.FindAll("div.hd-modal-actions button").First(b => b.TextContent == "Save").ClickAsync(new MouseEventArgs());

        Assert.Contains("already exists", cut.Markup);
        await using var db = _fx.NewDb();
        Assert.Equal(1, await db.Applications.CountAsync(a => a.Name == app.Name));
    }

    [Fact]
    public async Task The_last_administrator_cannot_be_deleted_from_the_users_screen()
    {
        // In the shared test database ADMIN1 is not the last admin, so make it so for the duration of this test.
        await using var db = _fx.NewDb();
        var others = await db.Users.Where(u => u.Role == UserRole.Admin && u.IsActive && u.Id != _fx.Admin1.Id).ToListAsync();
        foreach (var o in others) o.IsActive = false;
        await db.SaveChangesAsync();
        try
        {
            using var ctx = AdminContext();
            ctx.Services.AddSingleton<AuthenticationStateProvider>(
                new FixedAuthState(HelpDesk.Tests.Services.Principals.For(_fx.Editor.Id, UserRole.Admin))); // someone else is clicking
            var cut = ctx.RenderComponent<AdminUsers>();
            await cut.InvokeAsync(() => cut.Instance.PromptDelete(_fx.Admin1.Id));

            await using var check = _fx.NewDb();
            Assert.True(await check.Users.AnyAsync(u => u.Id == _fx.Admin1.Id), "The last administrator was deleted.");
            Assert.Contains("last active administrator", cut.Markup);
        }
        finally
        {
            foreach (var o in others) o.IsActive = true;
            await db.SaveChangesAsync();
        }
    }

    // ── grids ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("HelpDesk.Web/wwwroot/js/adminGrids.js")]
    [InlineData("HelpDesk.Web/wwwroot/js/diagGrids.js")]
    [InlineData("HelpDesk.Books/wwwroot/js/booksCatalogGrids.js")]
    public void Grid_columns_never_return_database_text_as_raw_html(string file)
    {
        // A group description containing HTML ran as script when the Groups grid loaded.
        // Custom column renderers must escape the value; "render: function (d) { return ... : d; }" is the unsafe shape.
        var js = File.ReadAllText(Path.Combine(RepoRoot(), file.Replace('/', Path.DirectorySeparatorChar)));
        var unsafeReturns = System.Text.RegularExpressions.Regex.Matches(js, @"return[^;\n]*[:?]\s*(d|data)\s*;");
        Assert.True(unsafeReturns.Count == 0,
            $"{file} returns raw cell text in {unsafeReturns.Count} renderer(s): {string.Join(" | ", unsafeReturns.Select(m => m.Value))}");
        Assert.Contains("helpdeskEscapeHtml", js);
    }
}
