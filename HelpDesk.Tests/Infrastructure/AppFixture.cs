using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Principal;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using HelpDesk.Books.Models;
using HelpDesk.Books.Models.Data;
using HelpDesk.Models;
using HelpDesk.Models.Data;
using HelpDesk.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HelpDesk.Tests.Infrastructure;

/// <summary>Test categories. Tag a test KnownIssue when it describes how the app SHOULD behave but does not yet (each failure is a finding).</summary>
public static class Category
{
    public const string Name = "Category";
    public const string KnownIssue = "KnownIssue";
    public const string Security = "Security";
}

[CollectionDefinition(Name)]
public sealed class AppCollection : ICollectionFixture<AppFixture>
{
    public const string Name = "App";
}

/// <summary>Captures outgoing mail instead of sending it.</summary>
public sealed class FakeEmailSender : IEmailSender
{
    public sealed record Mail(string To, string Subject, string Body);

    public ConcurrentQueue<Mail> Sent { get; } = new();

    public Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        Sent.Enqueue(new Mail(to, subject, body));
        return Task.CompletedTask;
    }

    public List<Mail> To(string address) =>
        Sent.Where(m => string.Equals(m.To, address, StringComparison.OrdinalIgnoreCase)).ToList();

    public static string ExtractToken(string body)
    {
        var m = Regex.Match(body, @"token=([0-9A-Fa-f]+)");
        Assert.True(m.Success, "No reset token found in email body: " + body);
        return m.Groups[1].Value;
    }
}

/// <summary>
/// Stands in for Windows Negotiate (which needs IIS/Kestrel + a real handshake).
/// Header <c>X-Test-Windows: current</c> = the real Windows identity running the tests;
/// <c>X-Test-Windows: name:DOMAIN\user</c> = a plain named identity.
/// </summary>
public sealed class FakeNegotiateHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string Header = "X-Test-Windows";

    public FakeNegotiateHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var v = Request.Headers[Header].ToString();
        if (string.IsNullOrEmpty(v))
            return Task.FromResult(AuthenticateResult.NoResult());

        ClaimsPrincipal principal;
        if (v == "current")
            principal = new WindowsPrincipal(WindowsIdentity.GetCurrent());
        else if (v.StartsWith("name:", StringComparison.Ordinal))
            principal = new ClaimsPrincipal(new GenericIdentity(v["name:".Length..], "Negotiate"));
        else
            return Task.FromResult(AuthenticateResult.NoResult());

        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Negotiate";
        return Task.CompletedTask;
    }
}

public sealed class HelpDeskFactory : WebApplicationFactory<HelpDesk.Web.Controllers.AccountController>
{
    private readonly FakeEmailSender _email;

    public HelpDeskFactory(FakeEmailSender email) => _email = email;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(_email);

            services.AddTransient<FakeNegotiateHandler>();
            services.Configure<AuthenticationOptions>(o =>
            {
                if (o.SchemeMap.TryGetValue(NegotiateDefaults.AuthenticationScheme, out var scheme))
                    scheme.HandlerType = typeof(FakeNegotiateHandler);
            });
        });
    }
}

public sealed class TestDbFactory : IDbContextFactory<HelpDeskDbContext>
{
    private readonly DbContextOptions<HelpDeskDbContext> _options;
    private readonly HelpDesk.Logging.ICurrentUserContext? _user;

    public TestDbFactory(string cs, HelpDesk.Logging.ICurrentUserContext? user = null)
    {
        _options = new DbContextOptionsBuilder<HelpDeskDbContext>().UseSqlServer(cs).Options;
        _user = user;
    }

    public HelpDeskDbContext CreateDbContext() => new(_options, _user);
}

public sealed class TestBooksDbFactory : IDbContextFactory<BooksDbContext>
{
    private readonly DbContextOptions<BooksDbContext> _options;

    public TestBooksDbFactory(string cs) =>
        _options = new DbContextOptionsBuilder<BooksDbContext>().UseSqlServer(cs).Options;

    public BooksDbContext CreateDbContext() => new(_options);
}

public sealed class FixedUser : HelpDesk.Logging.ICurrentUserContext
{
    public int? UserId { get; set; }
}

/// <summary>
/// Boots the real app (real pipeline, real EF migrations) against two throwaway LocalDB databases
/// that are dropped again when the run ends. The developer's own HelpDesk / Books databases are never touched.
/// </summary>
public sealed class AppFixture : IAsyncLifetime
{
    public const string Pw = "Passw0rd!Test";
    private const string Server = @"(localdb)\MSSQLLocalDB";

    private static int _counter;

    public string HelpDeskCs { get; }
    public string BooksCs { get; }
    public FakeEmailSender Email { get; } = new();
    public HelpDeskFactory Factory { get; private set; } = null!;
    public PasswordHasher<User> Hasher { get; } = new();

    // Seeded identities
    public User Admin1 = null!, Reader = null!, Editor = null!, NoPerm = null!, Outsider = null!, Newbie = null!, Inactive = null!;
    public DeskPage BooksPage = null!;
    public DeskGroup BooksGroup = null!;
    public int PublisherId;

    public AppFixture()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        HelpDeskCs = $"Server={Server};Database=HelpDeskTests_{suffix};Integrated Security=true;TrustServerCertificate=true;MultipleActiveResultSets=true";
        BooksCs = $"Server={Server};Database=BooksTests_{suffix};Integrated Security=true;TrustServerCertificate=true;MultipleActiveResultSets=true";
    }

    public async Task InitializeAsync()
    {
        // Program.cs reads these while building, so they must be in place before the host starts.
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        Environment.SetEnvironmentVariable("ConnectionStrings__HelpDesk", HelpDeskCs);
        Environment.SetEnvironmentVariable("ConnectionStrings__Books", BooksCs);
        Environment.SetEnvironmentVariable("App__DbInit", "true");
        Environment.SetEnvironmentVariable("App__WindowsAutoLoginChallenge", "false");

        Factory = new HelpDeskFactory(Email);
        _ = Factory.Server; // start host: applies migrations, seeds ADMIN + Books catalog rows

        await using (var books = NewBooksDb())
        {
            await books.Database.MigrateAsync();
            var pub = new Publisher { Name = "Test Press" };
            books.Publishers.Add(pub);
            await books.SaveChangesAsync();
            PublisherId = pub.PublisherId;
            for (var i = 1; i <= 3; i++)
                books.Books.Add(NewBook($"Seed Book {i}"));
            await books.SaveChangesAsync();
        }

        await using var db = NewDb();
        BooksPage = await db.Pages.SingleAsync(p => p.Route == "/user/books/books");
        BooksGroup = await db.Groups.SingleAsync(g => g.Name == "Books");

        Admin1 = await AddUserAsync("ADMIN1", UserRole.Admin);
        Reader = await AddUserAsync("READER");
        Editor = await AddUserAsync("EDITOR");
        NoPerm = await AddUserAsync("NOPERM");
        Outsider = await AddUserAsync("OUTSIDER");
        Newbie = await AddUserAsync("NEWBIE", mustChange: true);
        Inactive = await AddUserAsync("INACTIVE", active: false);

        await AddToGroupAsync(Reader.Id, BooksGroup.Id);
        await AddToGroupAsync(Editor.Id, BooksGroup.Id);
        await AddToGroupAsync(NoPerm.Id, BooksGroup.Id);
        await SetPermAsync(Reader.Id, BooksPage.Id, read: true);
        await SetPermAsync(Editor.Id, BooksPage.Id, true, true, true, true);
    }

    public async Task DisposeAsync()
    {
        try { Factory.Dispose(); } catch { /* best effort */ }
        try { NLog.LogManager.Shutdown(); } catch { /* best effort */ }
        SqlConnection.ClearAllPools();
        await using (var db = NewDb()) await db.Database.EnsureDeletedAsync();
        await using (var books = NewBooksDb()) await books.Database.EnsureDeletedAsync();
    }

    // ── database helpers ────────────────────────────────────────────

    public HelpDeskDbContext NewDb(HelpDesk.Logging.ICurrentUserContext? user = null) => new TestDbFactory(HelpDeskCs, user).CreateDbContext();
    public BooksDbContext NewBooksDb() => new TestBooksDbFactory(BooksCs).CreateDbContext();

    public static string Unique(string prefix = "U") =>
        prefix + Interlocked.Increment(ref _counter) + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    public Book NewBook(string title) => new()
    {
        Isbn13 = (9780000000000L + Random.Shared.NextInt64(0, 999_999_999)).ToString(),
        Title = title,
        PublicationYear = 2020,
        PublisherId = PublisherId,
        LanguageCode = "EN",
        CreatedAt = DateTime.UtcNow
    };

    public async Task<int> AddBookAsync(string title)
    {
        await using var books = NewBooksDb();
        var b = NewBook(title);
        books.Books.Add(b);
        await books.SaveChangesAsync();
        return b.BookId;
    }

    public async Task<User> AddUserAsync(
        string loginId,
        UserRole role = UserRole.User,
        bool active = true,
        bool mustChange = false,
        string? email = null,
        UserType type = UserType.User,
        string? sam = null,
        string? upnOrSid = null,
        string? password = Pw)
    {
        await using var db = NewDb();
        var u = new User
        {
            LoginId = loginId,
            FirstName = type == UserType.User ? "Test" : loginId,
            LastName = type == UserType.User ? loginId : null,
            Role = role,
            IsActive = active,
            MustChangePassword = mustChange,
            Email = email,
            UserType = type,
            DomainSamAccountName = sam,
            DomainUserPrincipalName = upnOrSid,
            CreatedAt = DateTime.UtcNow
        };
        if (type == UserType.User && password is not null)
            u.PasswordHash = Hasher.HashPassword(u, password);
        db.Users.Add(u);
        await db.SaveChangesAsync();
        return u;
    }

    /// <summary>Creates application + catalog group + link + one page.</summary>
    public async Task<(DeskApplication App, DeskGroup Group, DeskPage Page)> AddAppAsync(string? name = null, bool pageActive = true, bool appActive = true)
    {
        name ??= Unique("APP");
        await using var db = NewDb();
        var app = new DeskApplication { Name = name, IsActive = appActive, CreatedAt = DateTime.UtcNow };
        var group = new DeskGroup { Name = name + "_GRP", CreatedAt = DateTime.UtcNow };
        db.Applications.Add(app);
        db.Groups.Add(group);
        await db.SaveChangesAsync();
        db.DeskApplicationGroups.Add(new DeskApplicationGroup { DeskApplicationId = app.Id, DeskGroupId = group.Id });
        var page = new DeskPage
        {
            Name = name + "_PAGE",
            Route = "/user/" + name.ToLowerInvariant() + "/page",
            DeskApplicationId = app.Id,
            IsActive = pageActive,
            CreatedAt = DateTime.UtcNow
        };
        db.Pages.Add(page);
        await db.SaveChangesAsync();
        return (app, group, page);
    }

    public async Task AddToGroupAsync(int userId, int groupId)
    {
        await using var db = NewDb();
        db.UserGroups.Add(new UserGroup { UserId = userId, GroupId = groupId, AssignedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    public async Task SetPermAsync(int userId, int pageId, bool create = false, bool read = false, bool update = false, bool delete = false)
    {
        await using var db = NewDb();
        var row = await db.UserPagePermissions.FirstOrDefaultAsync(p => p.UserId == userId && p.PageId == pageId);
        if (row is null)
        {
            row = new UserPagePermission { UserId = userId, PageId = pageId, CreatedAt = DateTime.UtcNow };
            db.UserPagePermissions.Add(row);
        }
        row.CanCreate = create;
        row.CanRead = read;
        row.CanUpdate = update;
        row.CanDelete = delete;
        await db.SaveChangesAsync();
    }

    // ── HTTP helpers ────────────────────────────────────────────────

    /// <summary>A second copy of the app with some settings changed (own rate limiter, own singletons, same databases).</summary>
    public WebApplicationFactory<HelpDesk.Web.Controllers.AccountController> WithSettings(params (string Key, string Value)[] settings) =>
        Factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))));

    public HttpClient NewClient(WebApplicationFactory<HelpDesk.Web.Controllers.AccountController>? factory = null, string baseAddress = "https://localhost") =>
        (factory ?? Factory).CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(baseAddress),
            AllowAutoRedirect = false,
            HandleCookies = false
        });

    public static Task<HttpResponseMessage> PostJsonAsync(HttpClient c, string url, object? body, bool fetchHeader = true)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        if (fetchHeader)
            req.Headers.Add("X-Requested-With", "fetch");
        return c.SendAsync(req);
    }

    public static Task<HttpResponseMessage> PostFormAsync(HttpClient c, string url, params (string Key, string Value)[] fields) =>
        c.PostAsync(url, new FormUrlEncodedContent(fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value))));

    public static Task<HttpResponseMessage> LoginRequestAsync(HttpClient c, string loginId, string password, string? returnUrl = null) =>
        PostJsonAsync(c, "/api/account/login", new { loginId, password, returnUrl });

    /// <summary>The <c>name=value</c> pair of the auth cookie issued by a response, or null when none (or a deletion) was sent.</summary>
    public static string? AuthCookie(HttpResponseMessage r)
    {
        if (!r.Headers.TryGetValues("Set-Cookie", out var values))
            return null;
        foreach (var v in values)
        {
            if (!v.StartsWith(AuthCookieNames.Auth + "=", StringComparison.Ordinal))
                continue;
            var pair = v.Split(';')[0];
            if (pair.Length > AuthCookieNames.Auth.Length + 1)
                return pair;
        }
        return null;
    }

    public static string? RawSetCookie(HttpResponseMessage r, string cookieName) =>
        r.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(v => v.StartsWith(cookieName + "=", StringComparison.Ordinal))
            : null;

    public static void UseCookie(HttpClient c, string cookie)
    {
        c.DefaultRequestHeaders.Remove("Cookie");
        c.DefaultRequestHeaders.Add("Cookie", cookie);
    }

    /// <summary>Signs in through the real login endpoint and returns a client that carries the session cookie.</summary>
    public async Task<HttpClient> LoginAsync(string loginId, string password = Pw,
        WebApplicationFactory<HelpDesk.Web.Controllers.AccountController>? factory = null)
    {
        var c = NewClient(factory);
        var r = await LoginRequestAsync(c, loginId, password);
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"Login as {loginId} failed: {(int)r.StatusCode}");
        var cookie = AuthCookie(r);
        Assert.NotNull(cookie);
        UseCookie(c, cookie!);
        return c;
    }

    /// <summary>True for a redirect to a path on THIS site starting with <paramref name="pathPrefix"/> (relative, or absolute to https://localhost).</summary>
    public static bool IsRedirectTo(HttpResponseMessage r, string pathPrefix)
    {
        if ((int)r.StatusCode is < 300 or >= 400)
            return false;
        var location = r.Headers.Location?.OriginalString ?? "";
        const string self = "https://localhost/";
        if (location.StartsWith(self, StringComparison.OrdinalIgnoreCase))
            location = location[(self.Length - 1)..];
        return location.StartsWith(pathPrefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when the caller was refused: challenged, forbidden, or bounced to login / access-denied.</summary>
    public static bool IsDenied(HttpResponseMessage r) =>
        r.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
        || IsRedirectTo(r, "/login") || IsRedirectTo(r, "/access-denied");

    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HelpDesk.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("HelpDesk.sln not found above " + AppContext.BaseDirectory);
    }
}
