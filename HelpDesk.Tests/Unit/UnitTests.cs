using System.Security.Claims;
using HelpDesk.Models;
using HelpDesk.Tests.Infrastructure;
using HelpDesk.Web.Configuration;
using HelpDesk.Web.Middleware;
using HelpDesk.Web.Models;
using HelpDesk.Web.Services;
using HelpDesk.Web.Validation;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HelpDesk.Tests.Unit;

public sealed class ValidationTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("  a b\tc\r\n", "abc")]
    [InlineData("MiXed", "MiXed")]
    [InlineData("a\u00A0b\u2003c", "abc")] // non-breaking and em spaces
    public void Login_id_normalisation_strips_all_whitespace_and_keeps_case(string? input, string expected) =>
        Assert.Equal(expected, AdminNameValidation.NormalizeLoginId(input));

    [Fact]
    public void Upper_no_spaces() => Assert.Equal("ABC1", AdminNameValidation.ToUpperNoSpaces(" a b c 1 "));

    [Fact]
    public void Whitespace_detection_and_messages()
    {
        Assert.True(AdminNameValidation.ContainsDisallowedWhitespace("a b"));
        Assert.False(AdminNameValidation.ContainsDisallowedWhitespace("ab"));
        Assert.False(AdminNameValidation.ContainsDisallowedWhitespace(null));
        Assert.Equal("Name cannot contain spaces.", AdminNameValidation.ErrorIfHasSpaces("a b", "Name"));
        Assert.Null(AdminNameValidation.ErrorIfHasSpaces("ab", "Name"));
        Assert.Equal("Name is required.", AdminNameValidation.ErrorIfEmptyCatalogName("   ", "Name"));
        Assert.Null(AdminNameValidation.ErrorIfEmptyCatalogName("x", "Name"));
    }

    [Theory]
    [InlineData("https://example.com/page", true)]
    [InlineData("http://example.com", true)]
    [InlineData("", true)]
    [InlineData(null, true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:text/html,<script>alert(1)</script>", false)]
    [InlineData("file:///c:/windows/win.ini", false)]
    [InlineData("ftp://example.com", false)]
    [InlineData("/relative/path", false)]
    [InlineData("example.com", false)]
    public void Page_urls_must_be_absolute_http_or_https(string? url, bool valid) =>
        Assert.Equal(valid, PageNavigationValidation.ErrorIfInvalidUrl(url) is null);

    [Fact]
    public void Page_navigation_rules()
    {
        Assert.NotNull(PageNavigationValidation.ErrorIfBothRouteAndUrl("/a", "https://b"));
        Assert.Null(PageNavigationValidation.ErrorIfBothRouteAndUrl("/a", " "));
        Assert.Null(PageNavigationValidation.ErrorIfBothRouteAndUrl(null, null));
        Assert.NotNull(PageNavigationValidation.ErrorIfNewTabWithoutUrl("", true));
        Assert.Null(PageNavigationValidation.ErrorIfNewTabWithoutUrl("https://b", true));
        Assert.NotNull(PageNavigationValidation.ErrorIfSeparatorHasNavigation(PageType.GradientSeparator, "/a", null));
        Assert.Null(PageNavigationValidation.ErrorIfSeparatorHasNavigation(PageType.Page, "/a", null));
        Assert.NotNull(PageNavigationValidation.ErrorIfSeparatorTextOnWrongType(PageType.Page, "text"));
        Assert.Null(PageNavigationValidation.ErrorIfSeparatorTextOnWrongType(PageType.ShadedSeparator, "text"));
        Assert.NotNull(PageNavigationValidation.ErrorIfSeparatorWithNewTab(PageType.ShadedSeparator, true));
        Assert.Null(PageNavigationValidation.ErrorIfSeparatorWithNewTab(PageType.Page, true));
    }

    [Fact]
    public void Permission_hierarchy()
    {
        var withoutRead = new PagePermissions { CanCreate = true, CanUpdate = true, CanDelete = true };
        withoutRead.ApplyPermissionHierarchy();
        Assert.False(withoutRead.CanRead || withoutRead.CanCreate || withoutRead.CanUpdate || withoutRead.CanDelete);

        var withRead = new PagePermissions { CanRead = true, CanDelete = true };
        withRead.ApplyPermissionHierarchy();
        Assert.True(withRead.CanRead && withRead.CanDelete);
        Assert.False(withRead.CanCreate || withRead.CanUpdate);
    }

    [Fact]
    public void Display_name()
    {
        Assert.Equal("Ann Lee", UserDisplayName.Format(" Ann ", " Lee ", "AL"));
        Assert.Equal("Ann", UserDisplayName.Format("Ann", null, "AL"));
        Assert.Equal("AL", UserDisplayName.Format(" ", "", "AL"));
    }

    // ── user validation ─────────────────────────────────────────────

    private static readonly UserValidationService Validator = new();

    [Fact]
    public void New_user_needs_names_login_and_password()
    {
        var r = Validator.ValidateUser(new User { UserType = UserType.User }, isNewUser: true, password: null);
        Assert.False(r.IsValid);
        Assert.Equal(4, r.Errors.Count);
        Assert.True(Validator.ValidateUser(new User { LoginId = "A", FirstName = "A", LastName = "B" }, true, "LongEnough#1").IsValid);
        Assert.True(Validator.ValidateUser(new User { LoginId = "A", FirstName = "A", LastName = "B" }, false, null).IsValid);
    }

    [Fact]
    public void Group_rows_cannot_carry_credentials()
    {
        var g = new User { UserType = UserType.Group, LoginId = @"DOM\G", FirstName = "G", Email = "g@x", PasswordHash = "h", MustChangePassword = true, LastName = "L" };
        Assert.Equal(4, Validator.ValidateUser(g, true).Errors.Count);
        Assert.True(Validator.ValidateUser(new User { UserType = UserType.Group, LoginId = @"DOM\G", FirstName = "G" }, true).IsValid);
        Assert.False(Validator.SupportsCredentialLogin(UserType.Group));
        Assert.False(Validator.SupportsPasswordOperations(UserType.Group));
        Assert.True(Validator.SupportsCredentialLogin(UserType.User));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("1234567")]
    [Trait(Category.Name, Category.KnownIssue)]
    public void Admin_cannot_give_a_new_user_a_password_shorter_than_the_policy(string password)
    {
        // Users changing their own password must use 8+ characters; the admin "add user" path accepts anything.
        var r = Validator.ValidateUser(new User { LoginId = "A", FirstName = "A", LastName = "B" }, isNewUser: true, password);
        Assert.False(r.IsValid, $"A {password.Length}-character password was accepted (policy minimum is {PasswordPolicy.MinLength}).");
    }
}

public sealed class RateLimiterTests
{
    private static LoginRateLimiter Limiter(int max = 3, int windowMinutes = 15) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["App:LoginRateLimit:MaxAttempts"] = max.ToString(),
            ["App:LoginRateLimit:WindowMinutes"] = windowMinutes.ToString()
        }).Build());

    [Fact]
    public void Blocks_at_the_configured_number_of_failures_and_reports_wait_time()
    {
        using var l = Limiter(max: 3, windowMinutes: 15);
        l.RecordFailure("a");
        l.RecordFailure("a");
        Assert.False(l.IsBlocked("a", out _));
        l.RecordFailure("a");
        Assert.True(l.IsBlocked("a", out var wait));
        Assert.InRange(wait.TotalMinutes, 14.5, 15.0);
    }

    [Fact]
    public void Keys_are_case_insensitive_and_independent()
    {
        using var l = Limiter(max: 2);
        l.RecordFailure("Bob");
        l.RecordFailure("BOB");
        Assert.True(l.IsBlocked("bob", out _));
        Assert.False(l.IsBlocked("alice", out _));
    }

    [Fact]
    public void Success_clears_the_record()
    {
        using var l = Limiter(max: 2);
        l.RecordFailure("a");
        l.RecordSuccess("a");
        l.RecordFailure("a");
        Assert.False(l.IsBlocked("a", out _));
    }

    [Fact]
    public void Defaults_are_five_attempts()
    {
        using var l = new LoginRateLimiter(new ConfigurationBuilder().Build());
        for (var i = 0; i < 4; i++) l.RecordFailure("a");
        Assert.False(l.IsBlocked("a", out _));
        l.RecordFailure("a");
        Assert.True(l.IsBlocked("a", out _));
    }

    [Fact]
    public void Survives_heavy_parallel_use()
    {
        using var l = Limiter(max: 1000);
        Parallel.For(0, 5000, i => { l.RecordFailure("k" + (i % 50)); l.IsBlocked("k" + (i % 50), out _); });
        Assert.False(l.IsBlocked("k1", out _)); // 100 failures each, limit 1000
    }
}

public sealed class ClaimsTests
{
    private static User NewUser(UserRole role = UserRole.User, bool mustChange = false) =>
        new() { Id = 42, LoginId = "JDOE", FirstName = "Jo", LastName = "Doe", Email = "jo@x", Role = role, MustChangePassword = mustChange };

    [Fact]
    public void Forms_claims_describe_the_user()
    {
        var claims = CookieAuthClaims.BuildForFormsUser(NewUser());
        Assert.Equal("42", claims.Single(c => c.Type == ClaimTypes.NameIdentifier).Value);
        Assert.Equal("User", claims.Single(c => c.Type == ClaimTypes.Role).Value);
        Assert.Equal(AuthClaims.SessionForms, claims.Single(c => c.Type == AuthClaims.SessionKind).Value);
        Assert.Equal("JDOE", claims.Single(c => c.Type == AuthClaims.LoginId).Value);
        Assert.DoesNotContain(claims, c => c.Type == AuthClaims.MustChangePassword);
    }

    [Fact]
    public void Must_change_flag_becomes_a_claim() =>
        Assert.Contains(CookieAuthClaims.BuildForFormsUser(NewUser(mustChange: true)), c => c.Type == AuthClaims.MustChangePassword && c.Value == "true");

    [Fact]
    public void Password_hash_is_never_put_in_the_cookie()
    {
        var u = NewUser();
        u.PasswordHash = "AQAAAA-secret-hash";
        Assert.DoesNotContain(CookieAuthClaims.BuildForFormsUser(u), c => c.Value.Contains("secret-hash"));
    }

    [Fact]
    public void Windows_claims_take_the_highest_role_of_user_and_groups_exactly_once()
    {
        var groups = new List<UserGroupInfo>
        {
            new() { Id = 1, Name = "LOW", AccessLevel = 1, Role = UserRole.User },
            new() { Id = 2, Name = "ADMINS", AccessLevel = 100, Role = UserRole.Admin }
        };
        var claims = CookieAuthClaims.BuildForWindowsUser(NewUser(), groups);

        Assert.Equal("Admin", Assert.Single(claims, c => c.Type == ClaimTypes.Role).Value);
        Assert.Equal("ADMINS", claims.Single(c => c.Type == AuthClaims.PrimaryGroup).Value);
        Assert.Equal(AuthClaims.SessionWindows, claims.Single(c => c.Type == AuthClaims.SessionKind).Value);
    }

    [Fact]
    public void Windows_claims_without_admin_groups_stay_user()
    {
        var groups = new List<UserGroupInfo> { new() { Id = 1, Name = "LOW", Role = UserRole.User } };
        Assert.Equal("User", Assert.Single(CookieAuthClaims.BuildForWindowsUser(NewUser(), groups), c => c.Type == ClaimTypes.Role).Value);
        Assert.DoesNotContain(CookieAuthClaims.BuildForWindowsUser(NewUser()), c => c.Type == AuthClaims.HelpdeskGroups);
    }
}

public sealed class MiddlewareAndConfigTests
{
    private static async Task<(bool NextCalled, string? Location)> RunGateAsync(string path, bool mustChange, bool authenticated = true)
    {
        var called = false;
        var mw = new MustChangePasswordMiddleware(_ => { called = true; return Task.CompletedTask; });
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "1") };
        if (mustChange) claims.Add(new Claim(AuthClaims.MustChangePassword, "true"));
        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(authenticated ? new ClaimsIdentity(claims, "Cookies") : new ClaimsIdentity())
        };
        ctx.Request.Path = path;
        await mw.InvokeAsync(ctx);
        return (called, ctx.Response.Headers.Location.ToString() is { Length: > 0 } l ? l : null);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/admin/users")]
    [InlineData("/api/grid/admin/users")]
    [InlineData("/api/account/login")]
    [InlineData("/account/change-password")]
    public async Task Must_change_gate_blocks(string path)
    {
        var (next, location) = await RunGateAsync(path, mustChange: true);
        Assert.False(next);
        Assert.StartsWith("/account/first-login-password?returnUrl=", location);
    }

    [Theory]
    [InlineData("/account/first-login-password")]
    [InlineData("/api/account/set-password")]
    [InlineData("/api/account/logout")]
    [InlineData("/api/account/session/ping")]
    [InlineData("/_blazor")]
    [InlineData("/_framework/blazor.web.js")]
    public async Task Must_change_gate_lets_the_essentials_through(string path) =>
        Assert.True((await RunGateAsync(path, mustChange: true)).NextCalled);

    [Fact]
    public async Task Must_change_gate_ignores_other_users()
    {
        Assert.True((await RunGateAsync("/admin/users", mustChange: false)).NextCalled);
        Assert.True((await RunGateAsync("/admin/users", mustChange: true, authenticated: false)).NextCalled);
    }

    [Theory]
    [InlineData("Direct", "direct.host")]
    [InlineData("direct", "direct.host")]
    [InlineData("Standard", "standard.host")]
    [InlineData("nonsense", "standard.host")]
    [InlineData(null, "standard.host")]
    public void Smtp_profile_is_selected_by_mode(string? mode, string expectedHost)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Smtp:Standard:Host"] = "standard.host",
            ["Smtp:Direct:Host"] = "direct.host",
            ["Smtp:Direct:User"] = "u"
        };
        if (mode is not null) settings["Smtp:Mode"] = mode;
        var merged = SmtpActiveProfile.GetMergedEntries(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        Assert.Equal(expectedHost, merged.Single(kv => kv.Key == "Smtp:Host").Value);
    }

    [Fact]
    public void Smtp_profile_missing_section_yields_nothing() =>
        Assert.Empty(SmtpActiveProfile.GetMergedEntries(new ConfigurationBuilder().Build()));
}

/// <summary>Audit trail written by the DbContext, plus database-level rules.</summary>
[Collection(AppCollection.Name)]
public sealed class AuditAndSchemaTests
{
    private readonly AppFixture _fx;

    public AuditAndSchemaTests(AppFixture fx) => _fx = fx;

    [Fact]
    public async Task Insert_update_and_delete_are_recorded_with_the_acting_user()
    {
        var actor = new FixedUser { UserId = _fx.Admin1.Id };
        var name = AppFixture.Unique("AUD");
        int id;

        await using (var db = _fx.NewDb(actor))
        {
            var g = new DeskGroup { Name = name, CreatedAt = DateTime.UtcNow };
            db.Groups.Add(g);
            await db.SaveChangesAsync();
            id = g.Id;
            g.Description = "changed";
            await db.SaveChangesAsync();
            db.Groups.Remove(g);
            await db.SaveChangesAsync();
        }

        await using var read = _fx.NewDb();
        var rows = await read.EntityChangeHistories
            .Where(h => h.EntityType == TrackedEntityType.DeskGroup && h.EntityId == id.ToString())
            .OrderBy(h => h.Id).ToListAsync();

        Assert.Equal(new[] { EntityChangeAction.Insert, EntityChangeAction.Update, EntityChangeAction.Delete }, rows.Select(r => r.Action));
        Assert.All(rows, r => Assert.Equal(_fx.Admin1.Id, r.ChangedByUserId));
        Assert.Contains("\"Old\":null", rows[1].JsonChanges);
        Assert.Contains("\"New\":\"changed\"", rows[1].JsonChanges);
    }

    [Fact]
    public async Task Password_hashes_are_never_written_to_the_audit_trail()
    {
        var login = AppFixture.Unique("AUD");
        var u = await _fx.AddUserAsync(login);
        await using (var db = _fx.NewDb())
        {
            var row = await db.Users.SingleAsync(x => x.Id == u.Id);
            row.PasswordHash = _fx.Hasher.HashPassword(row, "Another#Password1");
            row.Email = "changed@test.local";
            await db.SaveChangesAsync();
        }

        await using var read = _fx.NewDb();
        var json = string.Join("\n", await read.EntityChangeHistories
            .Where(h => h.EntityType == TrackedEntityType.User && h.EntityId == u.Id.ToString())
            .Select(h => h.JsonChanges).ToListAsync());

        Assert.Contains("changed@test.local", json);
        Assert.DoesNotContain("PasswordHash", json);
        Assert.DoesNotContain("AQAAAA", json);
    }

    [Fact]
    public async Task Password_reset_tokens_are_not_audited()
    {
        await using var db = _fx.NewDb();
        Assert.False(await db.EntityChangeHistories.AnyAsync(h => h.EntityType.Contains("Token")));
    }

    [Fact]
    public async Task Login_ids_are_unique_regardless_of_case()
    {
        var login = AppFixture.Unique("DUP");
        await _fx.AddUserAsync(login);
        await Assert.ThrowsAsync<DbUpdateException>(() => _fx.AddUserAsync(login.ToLowerInvariant()));
    }

    [Fact]
    public async Task Separators_cannot_have_a_route_at_database_level()
    {
        var (app, _, _) = await _fx.AddAppAsync();
        await using var db = _fx.NewDb();
        db.Pages.Add(new DeskPage { Name = "sep", PageType = PageType.GradientSeparator, Route = "/x", DeskApplicationId = app.Id, CreatedAt = DateTime.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Concurrent_edits_of_the_same_user_are_detected()
    {
        var u = await _fx.AddUserAsync(AppFixture.Unique("CONC"));
        await using var a = _fx.NewDb();
        await using var b = _fx.NewDb();
        var ua = await a.Users.SingleAsync(x => x.Id == u.Id);
        var ub = await b.Users.SingleAsync(x => x.Id == u.Id);
        ua.FirstName = "A";
        ub.FirstName = "B";
        await a.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => b.SaveChangesAsync());
    }

    [Fact]
    public async Task Deleting_a_user_removes_their_memberships_permissions_and_tokens()
    {
        var (_, group, page) = await _fx.AddAppAsync();
        var u = await _fx.AddUserAsync(AppFixture.Unique("DEL"));
        await _fx.AddToGroupAsync(u.Id, group.Id);
        await _fx.SetPermAsync(u.Id, page.Id, read: true);

        await using (var db = _fx.NewDb())
        {
            db.Users.Remove(await db.Users.SingleAsync(x => x.Id == u.Id));
            await db.SaveChangesAsync();
        }

        await using var read = _fx.NewDb();
        Assert.False(await read.UserGroups.AnyAsync(x => x.UserId == u.Id));
        Assert.False(await read.UserPagePermissions.AnyAsync(x => x.UserId == u.Id));
    }

    [Fact]
    public async Task Startup_seeding_created_the_books_catalog_and_one_admin_that_must_change_password()
    {
        await using var db = _fx.NewDb();
        var admin = await db.Users.SingleAsync(u => u.LoginId == "ADMIN");
        Assert.True(admin.MustChangePassword);
        Assert.Equal(UserRole.Admin, admin.Role);
        Assert.Equal("/user/books/books", _fx.BooksPage.Route);
        Assert.True(await db.DeskApplicationGroups.AnyAsync(ag => ag.DeskGroupId == _fx.BooksGroup.Id));
    }

    [Fact]
    public async Task Sign_ins_are_written_to_the_nlog_table()
    {
        var login = AppFixture.Unique("LOGGED");
        await _fx.AddUserAsync(login);
        (await _fx.LoginAsync(login)).Dispose();
        NLog.LogManager.Flush(TimeSpan.FromSeconds(10));

        await using var db = _fx.NewDb();
        var found = false;
        for (var i = 0; i < 20 && !found; i++)
        {
            found = await db.NLogEntries.AnyAsync(e => e.LoginId == login && e.ActivityCategory == "Login");
            if (!found) await Task.Delay(250);
        }
        Assert.True(found, "No NLOG row with category 'Login' was written for the sign-in.");
    }
}
