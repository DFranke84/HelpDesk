using System.Security.Claims;
using Bunit;
using HelpDesk.Books.Components;
using HelpDesk.Books.Models.Data;
using HelpDesk.Models;
using HelpDesk.Models.Data;
using HelpDesk.Tests.Infrastructure;
using HelpDesk.Books.Services;
using HelpDesk.Tests.Services;
using HelpDesk.Web.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static HelpDesk.Tests.Infrastructure.AppFixture;

namespace HelpDesk.Tests.Components;

/// <summary>
/// Renders the real Books page component for different users. The grid's Edit/Delete buttons call
/// <c>OpenEdit</c>/<c>PromptDelete</c> on the server through JS interop; any signed-in user can make the same
/// calls from the browser console, so the server-side methods themselves must check permissions.
/// </summary>
[Collection(AppCollection.Name)]
[Trait(Category.Name, Category.Security)]
public sealed class BooksCatalogTests
{
    private readonly AppFixture _fx;

    public BooksCatalogTests(AppFixture fx) => _fx = fx;

    private IRenderedComponent<BooksCatalog> Render(Bunit.TestContext ctx, ClaimsPrincipal user)
    {
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.JSInterop.Setup<bool>("confirm", _ => true).SetResult(true); // the user clicks OK on every prompt
        ctx.Services.AddSingleton<IDbContextFactory<BooksDbContext>>(new TestBooksDbFactory(_fx.BooksCs));
        // The real permission chain (page -> BooksAccess -> PagePermissionService -> database), with NO HTTP context:
        // inside a live Blazor circuit the page only has the authentication state to go on.
        var helpDesk = new TestDbFactory(_fx.HelpDeskCs);
        ctx.Services.AddSingleton<IBooksAccess>(
            new BooksAccess(helpDesk, new PagePermissionService(helpDesk, Principals.Accessor(null))));
        ctx.Services.AddSingleton<AuthenticationStateProvider>(new FixedAuthState(user));

        var cut = ctx.RenderComponent<BooksCatalog>();
        cut.WaitForState(() => !cut.Markup.Contains("Loading..."), TimeSpan.FromSeconds(20));
        return cut;
    }

    private sealed class FixedAuthState : AuthenticationStateProvider
    {
        private readonly ClaimsPrincipal _user;
        public FixedAuthState(ClaimsPrincipal user) => _user = user;
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(_user));
    }

    private async Task<bool> BookExistsAsync(int id)
    {
        await using var db = _fx.NewBooksDb();
        return await db.Books.AnyAsync(b => b.BookId == id);
    }

    [Fact]
    public void User_without_read_permission_sees_access_denied()
    {
        using var ctx = new Bunit.TestContext();
        var cut = Render(ctx, Principals.For(_fx.NoPerm.Id));
        Assert.Contains("Access Denied", cut.Markup);
        Assert.DoesNotContain("tbl-books-catalog", cut.Markup);
    }

    [Fact]
    public void Anonymous_principal_sees_access_denied()
    {
        using var ctx = new Bunit.TestContext();
        var cut = Render(ctx, new ClaimsPrincipal(new ClaimsIdentity()));
        Assert.Contains("Access Denied", cut.Markup);
    }

    [Fact]
    public void Reader_sees_the_grid_with_add_disabled()
    {
        using var ctx = new Bunit.TestContext();
        var cut = Render(ctx, Principals.For(_fx.Reader.Id));
        Assert.Contains("tbl-books-catalog", cut.Markup);
        Assert.True(cut.Find("button.hd-btn.primary").HasAttribute("disabled"));
    }

    [Fact]
    public void Editor_and_admin_see_add_enabled()
    {
        using (var ctx = new Bunit.TestContext())
            Assert.False(Render(ctx, Principals.For(_fx.Editor.Id)).Find("button.hd-btn.primary").HasAttribute("disabled"));
        using (var ctx = new Bunit.TestContext())
            Assert.False(Render(ctx, Principals.For(_fx.Admin1.Id, UserRole.Admin)).Find("button.hd-btn.primary").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Editor_can_delete_a_book()
    {
        var id = await _fx.AddBookAsync("Delete me (editor)");
        using var ctx = new Bunit.TestContext();
        var cut = Render(ctx, Principals.For(_fx.Editor.Id));

        await cut.InvokeAsync(() => cut.Instance.PromptDelete(id));

        Assert.False(await BookExistsAsync(id));
    }

    [Fact]
    public async Task Editor_can_edit_a_book()
    {
        var id = await _fx.AddBookAsync("Edit me (editor)");
        using var ctx = new Bunit.TestContext();
        var cut = Render(ctx, Principals.For(_fx.Editor.Id));

        await cut.InvokeAsync(() => cut.Instance.OpenEdit(id));
        cut.FindAll("div.hd-modal input.hd-input")[1].Change("Edited by editor");
        await cut.FindAll("div.hd-modal-actions button").First(b => b.TextContent == "Save").ClickAsync(new MouseEventArgs());

        await using var db = _fx.NewBooksDb();
        Assert.Equal("Edited by editor", (await db.Books.SingleAsync(b => b.BookId == id)).Title);
    }

    [Fact]
    public async Task Read_only_user_cannot_delete_a_book_by_calling_the_server_method_directly()
    {
        var id = await _fx.AddBookAsync("Must survive (reader)");
        using var ctx = new Bunit.TestContext();
        var cut = Render(ctx, Principals.For(_fx.Reader.Id));

        // Equivalent to typing in the browser console: helpdeskAdmin.invoke('booksCatalog','PromptDelete', id)
        await cut.InvokeAsync(() => cut.Instance.PromptDelete(id));

        Assert.True(await BookExistsAsync(id), "A user with Read-only permission deleted a book.");
    }

    [Fact]
    public async Task Read_only_user_cannot_change_a_book_by_calling_the_server_method_directly()
    {
        var id = await _fx.AddBookAsync("Original title (reader)");
        using var ctx = new Bunit.TestContext();
        var cut = Render(ctx, Principals.For(_fx.Reader.Id));

        await cut.InvokeAsync(() => cut.Instance.OpenEdit(id));
        var inputs = cut.FindAll("div.hd-modal input.hd-input");
        if (inputs.Count > 1) // if the editor opened at all for a read-only user
        {
            inputs[1].Change("Defaced");
            await cut.FindAll("div.hd-modal-actions button").First(b => b.TextContent == "Save").ClickAsync(new MouseEventArgs());
        }

        await using var db = _fx.NewBooksDb();
        Assert.Equal("Original title (reader)", (await db.Books.SingleAsync(b => b.BookId == id)).Title);
    }

    [Fact]
    public async Task User_denied_access_cannot_delete_a_book_by_calling_the_server_method_directly()
    {
        var id = await _fx.AddBookAsync("Must survive (no access)");
        using var ctx = new Bunit.TestContext();
        var cut = Render(ctx, Principals.For(_fx.Outsider.Id));
        Assert.Contains("Access Denied", cut.Markup);

        await cut.InvokeAsync(() => cut.Instance.PromptDelete(id));

        Assert.True(await BookExistsAsync(id), "A user who is shown 'Access Denied' deleted a book.");
    }

    [Fact]
    public async Task Stray_permission_row_without_group_membership_does_not_open_the_page()
    {
        // The central permission service says "no access" for this user (see PagePermissionServiceTests);
        // the Books page has its own copy of the rules and must agree.
        var u = await _fx.AddUserAsync(Unique());
        await _fx.SetPermAsync(u.Id, _fx.BooksPage.Id, true, true, true, true);

        using var ctx = new Bunit.TestContext();
        var cut = Render(ctx, Principals.For(u.Id));

        Assert.Contains("Access Denied", cut.Markup);
    }

    [Fact]
    public async Task Windows_group_permissions_open_the_page_like_everywhere_else()
    {
        // Permission granted to a Windows group; the member has no row of their own.
        var u = await _fx.AddUserAsync(Unique());
        var winGroup = await _fx.AddUserAsync(Unique("WG"), type: UserType.Group);
        await _fx.AddToGroupAsync(winGroup.Id, _fx.BooksGroup.Id);
        await _fx.SetPermAsync(winGroup.Id, _fx.BooksPage.Id, read: true);

        using var ctx = new Bunit.TestContext();
        var cut = Render(ctx, Principals.For(u.Id, UserRole.User, winGroup.Id));

        Assert.Contains("tbl-books-catalog", cut.Markup);
    }
}
