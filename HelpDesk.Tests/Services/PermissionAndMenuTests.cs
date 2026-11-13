using System.Security.Claims;
using System.Text.Json;
using HelpDesk.Models;
using HelpDesk.Tests.Infrastructure;
using HelpDesk.Web.Authorization;
using HelpDesk.Web.Models;
using HelpDesk.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using static HelpDesk.Tests.Infrastructure.AppFixture;

namespace HelpDesk.Tests.Services;

internal static class Principals
{
    /// <summary>A signed-in principal; <paramref name="windowsGroupUserIds"/> mimics the group list a Windows login stores in the cookie.</summary>
    public static ClaimsPrincipal For(int userId, UserRole role = UserRole.User, params int[] windowsGroupUserIds)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Role, role.ToString())
        };
        if (windowsGroupUserIds.Length > 0)
        {
            var json = JsonSerializer.Serialize(windowsGroupUserIds.Select(id => new { Id = id, Name = "G" + id, AccessLevel = 1 }));
            claims.Add(new Claim(AuthClaims.HelpdeskGroups, json));
        }
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookies"));
    }

    public static IHttpContextAccessor Accessor(ClaimsPrincipal? user) =>
        new HttpContextAccessor { HttpContext = user is null ? null : new DefaultHttpContext { User = user } };
}

[Collection(AppCollection.Name)]
[Trait(Category.Name, Category.Security)]
public sealed class PagePermissionServiceTests
{
    private readonly AppFixture _fx;

    public PagePermissionServiceTests(AppFixture fx) => _fx = fx;

    private PagePermissionService Service(ClaimsPrincipal? user) =>
        new(new TestDbFactory(_fx.HelpDeskCs), Principals.Accessor(user));

    private static void AssertPerms(PagePermissions p, bool c, bool r, bool u, bool d)
    {
        Assert.Equal((c, r, u, d), (p.CanCreate, p.CanRead, p.CanUpdate, p.CanDelete));
    }

    [Fact]
    public async Task Admin_has_full_access_everywhere()
    {
        var (_, _, page) = await _fx.AddAppAsync();
        var p = await Service(Principals.For(_fx.Admin1.Id, UserRole.Admin)).GetUserPagePermissionsAsync(_fx.Admin1.Id, page.Id);
        AssertPerms(p, true, true, true, true);
    }

    [Fact]
    public async Task Admin_status_comes_from_the_database_not_from_the_caller()
    {
        // The service looks the role up itself, so passing a normal user's id never yields admin rights.
        var (_, _, page) = await _fx.AddAppAsync();
        var p = await Service(Principals.For(_fx.Outsider.Id, UserRole.Admin)).GetUserPagePermissionsAsync(_fx.Outsider.Id, page.Id);
        AssertPerms(p, false, false, false, false);
    }

    [Fact]
    public async Task User_outside_the_application_group_has_nothing_even_with_a_permission_row()
    {
        var (_, _, page) = await _fx.AddAppAsync();
        var u = await _fx.AddUserAsync(Unique());
        await _fx.SetPermAsync(u.Id, page.Id, true, true, true, true); // stray row, but no group membership

        var svc = Service(Principals.For(u.Id));
        AssertPerms(await svc.GetUserPagePermissionsAsync(u.Id, page.Id), false, false, false, false);
        Assert.False(await svc.CanUserPerformActionAsync(u.Id, page.Id, CrudAction.Read));
    }

    [Fact]
    public async Task Group_member_without_a_permission_row_has_nothing()
    {
        var (_, group, page) = await _fx.AddAppAsync();
        var u = await _fx.AddUserAsync(Unique());
        await _fx.AddToGroupAsync(u.Id, group.Id);

        AssertPerms(await Service(Principals.For(u.Id)).GetUserPagePermissionsAsync(u.Id, page.Id), false, false, false, false);
    }

    [Fact]
    public async Task Group_member_gets_exactly_the_granted_permissions()
    {
        var (_, group, page) = await _fx.AddAppAsync();
        var u = await _fx.AddUserAsync(Unique());
        await _fx.AddToGroupAsync(u.Id, group.Id);
        await _fx.SetPermAsync(u.Id, page.Id, read: true, update: true);

        var svc = Service(Principals.For(u.Id));
        AssertPerms(await svc.GetUserPagePermissionsAsync(u.Id, page.Id), false, true, true, false);
        Assert.True(await svc.CanUserPerformActionAsync(u.Id, page.Id, CrudAction.Update));
        Assert.False(await svc.CanUserPerformActionAsync(u.Id, page.Id, CrudAction.Delete));
        Assert.False(await svc.CanUserPerformActionAsync(u.Id, page.Id, CrudAction.Create));
    }

    [Fact]
    public async Task Inactive_page_grants_nothing()
    {
        var (_, group, page) = await _fx.AddAppAsync(pageActive: false);
        var u = await _fx.AddUserAsync(Unique());
        await _fx.AddToGroupAsync(u.Id, group.Id);
        await _fx.SetPermAsync(u.Id, page.Id, true, true, true, true);

        AssertPerms(await Service(Principals.For(u.Id)).GetUserPagePermissionsAsync(u.Id, page.Id), false, false, false, false);
    }

    [Fact]
    public async Task Unknown_page_or_user_grants_nothing()
    {
        var svc = Service(Principals.For(987654));
        AssertPerms(await svc.GetUserPagePermissionsAsync(987654, 987654), false, false, false, false);
    }

    [Fact]
    public async Task Windows_group_permissions_are_combined_with_the_users_own()
    {
        var (_, group, page) = await _fx.AddAppAsync();
        var u = await _fx.AddUserAsync(Unique());
        var winGroup = await _fx.AddUserAsync(Unique("WG"), type: UserType.Group);
        await _fx.AddToGroupAsync(winGroup.Id, group.Id);          // access to the app comes via the Windows group
        await _fx.SetPermAsync(u.Id, page.Id, read: true);
        await _fx.SetPermAsync(winGroup.Id, page.Id, read: true, delete: true);

        var p = await Service(Principals.For(u.Id, UserRole.User, winGroup.Id)).GetUserPagePermissionsAsync(u.Id, page.Id);
        AssertPerms(p, false, true, false, true);
    }

    [Fact]
    public async Task Admin_windows_group_makes_the_member_an_admin()
    {
        var (_, _, page) = await _fx.AddAppAsync();
        var u = await _fx.AddUserAsync(Unique());
        var adminGroup = await _fx.AddUserAsync(Unique("WG"), type: UserType.Group, role: UserRole.Admin);

        var p = await Service(Principals.For(u.Id, UserRole.User, adminGroup.Id)).GetUserPagePermissionsAsync(u.Id, page.Id);
        AssertPerms(p, true, true, true, true);
    }

    [Fact]
    public async Task Batch_lookup_matches_single_lookup()
    {
        var (_, group, page1) = await _fx.AddAppAsync();
        var (_, _, page2) = await _fx.AddAppAsync();
        var u = await _fx.AddUserAsync(Unique());
        await _fx.AddToGroupAsync(u.Id, group.Id);
        await _fx.SetPermAsync(u.Id, page1.Id, read: true, create: true);
        await _fx.SetPermAsync(u.Id, page2.Id, read: true); // no group access to app 2

        var batch = await Service(Principals.For(u.Id)).GetUserPagePermissionsAsync(u.Id, new[] { page1.Id, page2.Id, 987654 });
        AssertPerms(batch[page1.Id], true, true, false, false);
        AssertPerms(batch[page2.Id], false, false, false, false);
        AssertPerms(batch[987654], false, false, false, false);

        var svc = Service(Principals.For(u.Id));
        AssertPerms(await svc.GetUserPagePermissionsAsync(u.Id, page1.Id), true, true, false, false);
        AssertPerms(await svc.GetUserPagePermissionsAsync(u.Id, page2.Id), false, false, false, false);
    }

    [Fact]
    public async Task Saving_permissions_applies_the_hierarchy_and_takes_effect_immediately()
    {
        var (_, group, page) = await _fx.AddAppAsync();
        var u = await _fx.AddUserAsync(Unique());
        await _fx.AddToGroupAsync(u.Id, group.Id);
        var svc = Service(Principals.For(u.Id));

        Assert.False((await svc.GetUserPagePermissionsAsync(u.Id, page.Id)).CanRead); // caches "none"

        // Create without Read is not a valid state: Read is removed first, which removes Create too.
        await svc.SetUserPagePermissionsAsync(u.Id, page.Id, new PagePermissions { CanCreate = true });
        AssertPerms(await svc.GetUserPagePermissionsAsync(u.Id, page.Id), false, false, false, false);

        await svc.SetUserPagePermissionsAsync(u.Id, page.Id, new PagePermissions { CanRead = true, CanDelete = true });
        AssertPerms(await svc.GetUserPagePermissionsAsync(u.Id, page.Id), false, true, false, true);

        await svc.SetUserReadPermissionAsync(u.Id, page.Id, false);
        AssertPerms(await svc.GetUserPagePermissionsAsync(u.Id, page.Id), false, false, false, false);

        await svc.SetUserReadPermissionAsync(u.Id, page.Id, true);
        AssertPerms(await svc.GetUserPagePermissionsAsync(u.Id, page.Id), false, true, false, false);

        await svc.RemoveUserPagePermissionsAsync(u.Id, page.Id);
        AssertPerms(await svc.GetUserPagePermissionsAsync(u.Id, page.Id), false, false, false, false);
    }

    [Fact]
    public async Task Principal_based_lookup_matches_the_request_based_lookup()
    {
        // Blazor components pass the principal explicitly instead of relying on the HTTP context.
        var (_, group, page) = await _fx.AddAppAsync();
        var u = await _fx.AddUserAsync(Unique());
        var winGroup = await _fx.AddUserAsync(Unique("WG"), type: UserType.Group);
        await _fx.AddToGroupAsync(winGroup.Id, group.Id);
        await _fx.SetPermAsync(winGroup.Id, page.Id, read: true, update: true);
        var principal = Principals.For(u.Id, UserRole.User, winGroup.Id);

        var svc = Service(user: null); // no HTTP context at all
        AssertPerms(await svc.GetPermissionsAsync(principal, page.Id), false, true, true, false);
        AssertPerms(await svc.GetPermissionsAsync(new ClaimsPrincipal(new ClaimsIdentity()), page.Id), false, false, false, false);
    }

    [Fact]
    public async Task Removing_a_user_from_the_group_takes_effect_immediately()
    {
        var (_, group, page) = await _fx.AddAppAsync();
        var u = await _fx.AddUserAsync(Unique());
        await _fx.AddToGroupAsync(u.Id, group.Id);
        await _fx.SetPermAsync(u.Id, page.Id, true, true, true, true);
        var svc = Service(Principals.For(u.Id));
        Assert.True(await svc.CanUserPerformActionAsync(u.Id, page.Id, CrudAction.Delete));

        await using (var db = _fx.NewDb())
        {
            db.UserGroups.Remove(db.UserGroups.Single(x => x.UserId == u.Id && x.GroupId == group.Id));
            await db.SaveChangesAsync();
        }

        Assert.False(await svc.CanUserPerformActionAsync(u.Id, page.Id, CrudAction.Delete),
            "Access survived removal from the group.");
    }

    [Fact]
    public async Task Revoking_a_windows_groups_permission_takes_effect_immediately_for_its_members()
    {
        var (_, group, page) = await _fx.AddAppAsync();
        var u = await _fx.AddUserAsync(Unique());
        var winGroup = await _fx.AddUserAsync(Unique("WG"), type: UserType.Group);
        await _fx.AddToGroupAsync(winGroup.Id, group.Id);
        await _fx.SetPermAsync(winGroup.Id, page.Id, true, true, true, true);
        var svc = Service(Principals.For(u.Id, UserRole.User, winGroup.Id));
        Assert.True(await svc.CanUserPerformActionAsync(u.Id, page.Id, CrudAction.Delete));

        // Admin revokes the group's rights through the same service (as the admin page does).
        await Service(Principals.For(_fx.Admin1.Id, UserRole.Admin))
            .SetUserPagePermissionsAsync(winGroup.Id, page.Id, new PagePermissions());

        Assert.False(await svc.CanUserPerformActionAsync(u.Id, page.Id, CrudAction.Delete),
            "The member kept Delete after it was revoked from their Windows group.");
    }

    [Fact]
    public async Task Permissions_do_not_leak_between_two_windows_users_bound_to_the_same_group()
    {
        // Two domain users without their own profile both sign in "as" the same Group row (same user id).
        // User A is also in a second Windows group that grants Delete; user B is not.
        var (_, group, page) = await _fx.AddAppAsync();
        var shared = await _fx.AddUserAsync(Unique("WG"), type: UserType.Group);
        var extra = await _fx.AddUserAsync(Unique("WG"), type: UserType.Group);
        await _fx.AddToGroupAsync(shared.Id, group.Id);
        await _fx.SetPermAsync(shared.Id, page.Id, read: true);
        await _fx.SetPermAsync(extra.Id, page.Id, read: true, delete: true);

        var userA = Service(Principals.For(shared.Id, UserRole.User, shared.Id, extra.Id));
        Assert.True(await userA.CanUserPerformActionAsync(shared.Id, page.Id, CrudAction.Delete));

        var userB = Service(Principals.For(shared.Id, UserRole.User, shared.Id));
        Assert.False(await userB.CanUserPerformActionAsync(shared.Id, page.Id, CrudAction.Delete),
            "User B inherited user A's Delete permission.");
    }

    // ── PageCrudAuthorize attribute ─────────────────────────────────

    private sealed class StubPermissions : IPagePermissionService
    {
        public bool Allow;
        public Task<bool> CanUserPerformActionAsync(int userId, int pageId, CrudAction action) => Task.FromResult(Allow);
        public Task<PagePermissions> GetUserPagePermissionsAsync(int userId, int pageId) => Task.FromResult(new PagePermissions());
        public Task<PagePermissions> GetPermissionsAsync(ClaimsPrincipal user, int pageId) => Task.FromResult(new PagePermissions());
        public Task<Dictionary<int, PagePermissions>> GetUserPagePermissionsAsync(int userId, IEnumerable<int> pageIds) => Task.FromResult(new Dictionary<int, PagePermissions>());
        public Task SetUserPagePermissionsAsync(int userId, int pageId, PagePermissions permissions) => Task.CompletedTask;
        public Task RemoveUserPagePermissionsAsync(int userId, int pageId) => Task.CompletedTask;
        public Task SetUserReadPermissionAsync(int userId, int pageId, bool canRead) => Task.CompletedTask;
    }

    private static AuthorizationFilterContext FilterContext(ClaimsPrincipal user, string? query, bool allow)
    {
        var services = new ServiceCollection().AddSingleton<IPagePermissionService>(new StubPermissions { Allow = allow }).BuildServiceProvider();
        var http = new DefaultHttpContext { User = user, RequestServices = services };
        if (query is not null) http.Request.QueryString = new QueryString(query);
        return new AuthorizationFilterContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>());
    }

    [Fact]
    public async Task Crud_attribute_rejects_anonymous_callers()
    {
        var ctx = FilterContext(new ClaimsPrincipal(new ClaimsIdentity()), "?pageId=1", allow: true);
        await new PageDeleteAuthorizeAttribute().OnAuthorizationAsync(ctx);
        Assert.IsType<UnauthorizedResult>(ctx.Result);
    }

    [Fact]
    public async Task Crud_attribute_forbids_when_the_permission_is_missing()
    {
        var ctx = FilterContext(Principals.For(5), "?pageId=1", allow: false);
        await new PageDeleteAuthorizeAttribute().OnAuthorizationAsync(ctx);
        Assert.IsType<ForbidResult>(ctx.Result);
    }

    [Fact]
    public async Task Crud_attribute_allows_when_the_permission_is_present_and_for_admins()
    {
        var ok = FilterContext(Principals.For(5), "?pageId=1", allow: true);
        await new PageUpdateAuthorizeAttribute().OnAuthorizationAsync(ok);
        Assert.Null(ok.Result);

        var admin = FilterContext(Principals.For(1, UserRole.Admin), "?pageId=1", allow: false);
        await new PageUpdateAuthorizeAttribute().OnAuthorizationAsync(admin);
        Assert.Null(admin.Result);
    }

    [Fact]
    [Trait(Category.Name, Category.KnownIssue)]
    public async Task Crud_attribute_does_not_wave_through_requests_that_omit_the_page_id()
    {
        // Leaving "?pageId=" off the URL currently skips the permission check entirely.
        var ctx = FilterContext(Principals.For(5), query: null, allow: false);
        await new PageDeleteAuthorizeAttribute().OnAuthorizationAsync(ctx);
        Assert.NotNull(ctx.Result);
    }
}

[Collection(AppCollection.Name)]
[Trait(Category.Name, Category.Security)]
public sealed class MenuServiceTests
{
    private readonly AppFixture _fx;

    public MenuServiceTests(AppFixture fx) => _fx = fx;

    private MenuService Service(ClaimsPrincipal? user) => new(new TestDbFactory(_fx.HelpDeskCs), Principals.Accessor(user));

    [Fact]
    public async Task User_sees_only_applications_of_their_groups()
    {
        var (mine, group, _) = await _fx.AddAppAsync();
        var (other, _, _) = await _fx.AddAppAsync();
        var u = await _fx.AddUserAsync(Unique());
        await _fx.AddToGroupAsync(u.Id, group.Id);

        var apps = await Service(Principals.For(u.Id)).GetAccessibleApplicationsAsync(u.Id, isAdmin: false);

        Assert.Contains(apps, a => a.Id == mine.Id);
        Assert.DoesNotContain(apps, a => a.Id == other.Id);
    }

    [Fact]
    public async Task User_with_no_groups_sees_no_applications()
    {
        var apps = await Service(Principals.For(_fx.Outsider.Id)).GetAccessibleApplicationsAsync(_fx.Outsider.Id, false);
        Assert.Empty(apps);
    }

    [Fact]
    public async Task Admin_sees_all_active_applications_but_not_inactive_ones()
    {
        var (active, _, _) = await _fx.AddAppAsync();
        var (inactive, _, _) = await _fx.AddAppAsync(appActive: false);

        var apps = await Service(Principals.For(_fx.Admin1.Id, UserRole.Admin)).GetAccessibleApplicationsAsync(_fx.Admin1.Id, true);

        Assert.Contains(apps, a => a.Id == active.Id);
        Assert.DoesNotContain(apps, a => a.Id == inactive.Id);
    }

    [Fact]
    public async Task Inactive_application_is_hidden_from_its_group_members()
    {
        var (app, group, _) = await _fx.AddAppAsync(appActive: false);
        var u = await _fx.AddUserAsync(Unique());
        await _fx.AddToGroupAsync(u.Id, group.Id);

        var apps = await Service(Principals.For(u.Id)).GetAccessibleApplicationsAsync(u.Id, false);
        Assert.DoesNotContain(apps, a => a.Id == app.Id);
    }

    [Fact]
    public async Task Pages_of_another_groups_application_are_not_listed()
    {
        var (app, _, _) = await _fx.AddAppAsync();
        var u = await _fx.AddUserAsync(Unique());
        var svc = Service(Principals.For(u.Id));

        Assert.Empty(await svc.GetAccessiblePagesAsync(app.Id, u.Id, false));
        Assert.Empty(await svc.GetAccessiblePagesByAppsAsync(new[] { app.Id }, u.Id, false));
        Assert.Single(await svc.GetAccessiblePagesAsync(app.Id, _fx.Admin1.Id, true));
    }

    [Fact]
    public async Task Inactive_pages_are_not_listed()
    {
        var (app, group, _) = await _fx.AddAppAsync(pageActive: false);
        var u = await _fx.AddUserAsync(Unique());
        await _fx.AddToGroupAsync(u.Id, group.Id);

        Assert.Empty(await Service(Principals.For(u.Id)).GetAccessiblePagesAsync(app.Id, u.Id, false));
    }

    [Fact]
    public async Task Windows_group_membership_opens_the_application()
    {
        var (app, group, _) = await _fx.AddAppAsync();
        var u = await _fx.AddUserAsync(Unique());
        var winGroup = await _fx.AddUserAsync(Unique("WG"), type: UserType.Group);
        await _fx.AddToGroupAsync(winGroup.Id, group.Id);

        Assert.Empty(await Service(Principals.For(u.Id)).GetAccessiblePagesAsync(app.Id, u.Id, false));
        Assert.Single(await Service(Principals.For(u.Id, UserRole.User, winGroup.Id)).GetAccessiblePagesAsync(app.Id, u.Id, false));
    }

    [Theory]
    [InlineData("{0}")]
    [InlineData("{0}/")]
    [InlineData("{0}?x=1")]
    [InlineData("{0}/?x=1#frag")]
    public async Task Route_guard_blocks_a_catalog_route_for_non_members_in_every_spelling(string format)
    {
        var (_, group, page) = await _fx.AddAppAsync();
        var member = await _fx.AddUserAsync(Unique());
        await _fx.AddToGroupAsync(member.Id, group.Id);
        var stranger = await _fx.AddUserAsync(Unique());
        var path = string.Format(format, page.Route!.TrimStart('/'));

        Assert.False(await Service(Principals.For(stranger.Id)).CanAccessCatalogRoutePathAsync(path, stranger.Id, false));
        Assert.True(await Service(Principals.For(member.Id)).CanAccessCatalogRoutePathAsync(path, member.Id, false));
        Assert.True(await Service(Principals.For(_fx.Admin1.Id, UserRole.Admin)).CanAccessCatalogRoutePathAsync(path, _fx.Admin1.Id, true));
    }

    [Fact]
    public async Task Route_guard_is_not_fooled_by_upper_case()
    {
        var (_, _, page) = await _fx.AddAppAsync();
        var stranger = await _fx.AddUserAsync(Unique());
        Assert.False(await Service(Principals.For(stranger.Id)).CanAccessCatalogRoutePathAsync(page.Route!.ToUpperInvariant(), stranger.Id, false));
    }

    [Fact]
    public async Task Routes_that_are_not_in_the_catalog_are_left_to_normal_authorization()
    {
        Assert.True(await Service(Principals.For(_fx.Outsider.Id)).CanAccessCatalogRoutePathAsync("/dashboard", _fx.Outsider.Id, false));
        Assert.True(await Service(Principals.For(_fx.Outsider.Id)).CanAccessCatalogRoutePathAsync("", _fx.Outsider.Id, false));
    }
}

[Trait(Category.Name, Category.Security)]
public sealed class GroupMembershipServiceTests
{
    private sealed class StubWindowsGroups : IWindowsGroupService
    {
        public Task<List<UserGroupInfo>> GetUserHelpdeskGroupsAsync(int userId) =>
            Task.FromResult(new List<UserGroupInfo> { new() { Id = 1000 + userId, Name = "DB-" + userId, AccessLevel = 1 } });
        public Task<List<WindowsGroupInfo>> GetUserWindowsGroupsAsync(System.Security.Principal.WindowsIdentity identity) => throw new NotSupportedException();
        public Task<List<WindowsGroupInfo>> GetUserWindowsGroupsMatchingHelpdeskAsync(System.Security.Principal.WindowsIdentity identity) => throw new NotSupportedException();
        public Task<List<User>> GetHelpdeskGroupEntitiesAsync(List<WindowsGroupInfo> windowsGroups) => throw new NotSupportedException();
        public Task<int> CalculateGroupAccessLevelAsync(int groupId) => Task.FromResult(0);
    }

    private static UserGroupMembershipService Service(ClaimsPrincipal user, int? currentUserId) =>
        new(Principals.Accessor(user), new StubWindowsGroups(), new FixedUser { UserId = currentUserId });

    [Fact]
    public void Groups_are_read_from_the_cookie_ordered_by_access_level()
    {
        var json = JsonSerializer.Serialize(new[] { new { Id = 1, Name = "LOW", AccessLevel = 1 }, new { Id = 2, Name = "HIGH", AccessLevel = 50 } });
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(AuthClaims.HelpdeskGroups, json) }, "Cookies"));
        var svc = Service(user, 7);

        Assert.Equal(new[] { "HIGH", "LOW" }, svc.GetGroupsFromClaims().Select(g => g.Name));
        Assert.True(svc.HasMultipleGroups());
    }

    [Fact]
    public void Corrupt_group_claim_is_ignored_rather_than_crashing()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(AuthClaims.HelpdeskGroups, "{not json") }, "Cookies"));
        Assert.Empty(Service(user, 7).GetGroupsFromClaims());
    }

    [Fact]
    public async Task Without_a_group_claim_the_database_is_used()
    {
        var groups = await Service(Principals.For(7), 7).GetCurrentUserGroupsAsync();
        Assert.Equal("DB-7", Assert.Single(groups).Name);
    }

    [Fact]
    [Trait(Category.Name, Category.KnownIssue)]
    public async Task Asking_for_another_users_groups_does_not_return_my_own()
    {
        var groups = await Service(Principals.For(7, UserRole.User, 55), 7).GetUserGroupsAsync(userId: 99);
        Assert.Equal("DB-99", Assert.Single(groups).Name);
    }
}
