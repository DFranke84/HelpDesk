using System.Reflection;
using System.Security.Claims;
using HelpDesk.Models;
using HelpDesk.Logging;
using HelpDesk.Models.Data;
using HelpDesk.Models.External;
using HelpDesk.Web.Data;
using HelpDesk.Web.Configuration;
using HelpDesk.Web.Middleware;
using HelpDesk.Web.Services;
using HelpDesk.Books.Extensions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NLog.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddInMemoryCollection(SmtpActiveProfile.GetMergedEntries(builder.Configuration));

builder.Logging.ClearProviders();
builder.Host.UseNLog();
// Without a console logger, `dotnet run` shows almost no output (NLog writes to file only). Restore console in Development so URLs and errors are visible.
if (builder.Environment.IsDevelopment())
    builder.Logging.AddConsole();

builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<ICurrentUserContext, HttpCurrentUserContext>();
builder.Services.AddScoped<ICorrelationContext, CorrelationContext>();
builder.Services.AddScoped<EventLogManager>();
builder.Services.AddScoped<IMenuService, MenuService>();
builder.Services.AddScoped<IPagePermissionService, PagePermissionService>();
builder.Services.AddScoped<IWindowsGroupService, WindowsGroupService>();
builder.Services.AddScoped<IUserGroupMembershipService, UserGroupMembershipService>();
builder.Services.AddScoped<IUserValidationService, UserValidationService>();
builder.Services.AddSingleton<IMenuRefreshNotifier, MenuRefreshNotifier>();
builder.Services.AddScoped<IStatusBarService, StatusBarService>();
builder.Services.AddScoped<PasswordResetService>();
builder.Services.AddScoped<IEmailSender, EmailSender>();
builder.Services.AddSingleton<PasswordHasher<User>>();
builder.Services.AddSingleton<LoginRateLimiter>();

var helpDeskCs = builder.Configuration.GetConnectionString("HelpDesk")
    ?? throw new InvalidOperationException("Connection string 'HelpDesk' is not configured.");
// Scoped factory so DbContext construction can use scoped deps (e.g. ICurrentUserContext). Singleton factory resolves from root and cannot inject scoped services.
// Do not also call AddDbContext for the same type — duplicate registration of options/factory for one context type causes DI issues.
builder.Services.AddDbContextFactory<HelpDeskDbContext>((sp, options) =>
{
    options.UseSqlServer(helpDeskCs);
}, ServiceLifetime.Scoped);

var booksCs = builder.Configuration.GetConnectionString("Books")
    ?? throw new InvalidOperationException("Connection string 'Books' is not configured.");
builder.Services.AddHelpDeskBooks(booksCs);

builder.Services.AddScoped<AuthenticationStateProvider, ServerAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.Cookie.Name = AuthCookieNames.Auth;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(60);
        options.SlidingExpiration = true;
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/access-denied";
    })
    .AddNegotiate();

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
});

builder.Services.AddSingleton<IExternalDatabaseContextFactory, ExternalDatabaseContextFactory>();

builder.Services.AddCascadingAuthenticationState();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddControllers();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseMiddleware<LoggingScopeMiddleware>();
app.UseMiddleware<WindowsAutoLoginMiddleware>();
app.UseMiddleware<MustChangePasswordMiddleware>();
app.UseAuthorization();
app.UseAntiforgery();

app.MapControllers();
app.MapHelpDeskBooks();
app.MapRazorComponents<HelpDesk.Web.Components.App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(HelpDesk.Books.Models.Data.BooksDbContext).Assembly);

await DbInitializer.MigrateAsync(app.Services).ConfigureAwait(false);

try
{
    app.Run();
}
finally
{
    NLog.LogManager.Shutdown();
}
