using HelpDesk.Models;
using HelpDesk.Models.Data;
using HelpDesk.Web.Validation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HelpDesk.Web.Data;

/// <summary>
/// Startup database initialization: applies EF migrations, seeds catalog data for BOOKS, and ensures a single initial admin exists when the database has no users.
/// Called from <c>Program.cs</c> after the host is built: <c>await DbInitializer.MigrateAsync(app.Services);</c>
/// </summary>
public static class DbInitializer
{
    /// <summary>Applies migrations, ensures BOOKS application/group/page links, then seeds an initial admin only when the database has no users.</summary>
    public static async Task MigrateAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var hasher = scope.ServiceProvider.GetRequiredService<PasswordHasher<User>>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(DbInitializer).FullName!);

        try
        {
            await db.Database.MigrateAsync(ct).ConfigureAwait(false);

            await EnsureBooksCatalogAsync(db, ct).ConfigureAwait(false);

            if (await db.Users.AnyAsync(ct).ConfigureAwait(false))
                return;

            var rawLogin = config["App:InitialAdminLoginId"] ?? config["App:InitialAdminUserName"] ?? "ADMIN";
            var loginId = AdminNameValidation.ToUpperNoSpaces(rawLogin);
            var password = config["App:InitialAdminPassword"] ?? "ChangeMe!123";
            var email = string.IsNullOrWhiteSpace(config["App:InitialAdminEmail"])
                ? "admin@localhost"
                : config["App:InitialAdminEmail"]!.Trim();

            var admin = new User
            {
                LoginId = loginId,
                Email = email,
                Role = UserRole.Admin,
                IsActive = true,
                MustChangePassword = true,
                CreatedAt = DateTime.UtcNow
            };
            admin.PasswordHash = hasher.HashPassword(admin, password);
            db.Users.Add(admin);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Database initialization failed");
            throw;
        }
    }

    /// <summary>
    /// Ensures group BOOKS, application BOOKS (external DB connection key Books), application↔group link,
    /// Pages row <c>Books</c> → <c>/user/books/catalog</c> (migrates legacy <c>Book Users</c> / <c>/user/books/users</c>),
    /// and TESTUSER membership in BOOKS when that user exists.
    /// </summary>
    private static async Task EnsureBooksCatalogAsync(HelpDeskDbContext db, CancellationToken ct)
    {
        const string booksGroupName = "BOOKS";
        const string booksAppName = "BOOKS";
        const string booksCsName = "Books";
        const string booksPageName = "Books";
        const string booksRoute = "/user/books/catalog";
        const string legacyPageName = "Book Users";
        const string legacyRoute = "/user/books/users";
        var testUserLoginId = AdminNameValidation.ToUpperNoSpaces("TESTUser");

        var group = await db.Groups.AsQueryable()
            .FirstOrDefaultAsync(g => g.Name == booksGroupName, ct)
            .ConfigureAwait(false);
        if (group is null)
        {
            group = new DeskGroup
            {
                Name = booksGroupName,
                Description = "Access to the BOOKS application and its pages.",
                CreatedAt = DateTime.UtcNow
            };
            db.Groups.Add(group);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        var app = await db.Applications.AsQueryable()
            .FirstOrDefaultAsync(a => a.Name == booksAppName, ct)
            .ConfigureAwait(false);
        if (app is null)
        {
            app = new DeskApplication
            {
                Name = booksAppName,
                Description = "Books library data (Books SQL database).",
                UsesExternalDatabase = true,
                ExternalConnectionStringName = booksCsName,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            db.Applications.Add(app);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        else
        {
            var changed = false;
            if (!app.UsesExternalDatabase)
            {
                app.UsesExternalDatabase = true;
                changed = true;
            }

            if (!string.Equals(app.ExternalConnectionStringName, booksCsName, StringComparison.Ordinal))
            {
                app.ExternalConnectionStringName = booksCsName;
                changed = true;
            }

            if (!app.IsActive)
            {
                app.IsActive = true;
                changed = true;
            }

            if (changed)
            {
                app.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
        }

        var link = await db.DeskApplicationGroups.AsQueryable()
            .FirstOrDefaultAsync(ag => ag.DeskApplicationId == app.Id && ag.DeskGroupId == group.Id, ct)
            .ConfigureAwait(false);
        if (link is null)
        {
            db.DeskApplicationGroups.Add(new DeskApplicationGroup
            {
                DeskApplicationId = app.Id,
                DeskGroupId = group.Id
            });
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        // Upgrade old "Book Users" menu entries to the books grid page
        var legacyPages = await db.Pages
            .Where(p => p.DeskApplicationId == app.Id && (p.Route == legacyRoute || p.Name == legacyPageName))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        foreach (var lp in legacyPages)
        {
            lp.Name = booksPageName;
            lp.Route = booksRoute;
            lp.UpdatedAt = DateTime.UtcNow;
        }

        if (legacyPages.Count > 0)
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var page = await db.Pages.AsQueryable()
            .FirstOrDefaultAsync(p => p.DeskApplicationId == app.Id && p.Route == booksRoute, ct)
            .ConfigureAwait(false);
        if (page is null)
        {
            page = await db.Pages.AsQueryable()
                .FirstOrDefaultAsync(p => p.DeskApplicationId == app.Id && p.Name == booksPageName, ct)
                .ConfigureAwait(false);
        }

        if (page is null)
        {
            db.Pages.Add(new DeskPage
            {
                Name = booksPageName,
                Route = booksRoute,
                DisplayOrder = 10,
                IsActive = true,
                DeskApplicationId = app.Id,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        else
        {
            var changed = false;
            if (page.Name != booksPageName)
            {
                page.Name = booksPageName;
                changed = true;
            }

            if (page.Route != booksRoute)
            {
                page.Route = booksRoute;
                changed = true;
            }

            if (!page.IsActive)
            {
                page.IsActive = true;
                changed = true;
            }

            if (changed)
            {
                page.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
        }

        var testUser = await db.Users.AsQueryable()
            .FirstOrDefaultAsync(u => u.LoginId == testUserLoginId, ct)
            .ConfigureAwait(false);
        if (testUser is null)
            return;

        var ug = await db.UserGroups.AsQueryable()
            .FirstOrDefaultAsync(x => x.UserId == testUser.Id && x.GroupId == group.Id, ct)
            .ConfigureAwait(false);
        if (ug is not null)
            return;

        db.UserGroups.Add(new UserGroup
        {
            UserId = testUser.Id,
            GroupId = group.Id,
            AssignedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
