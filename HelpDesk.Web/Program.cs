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
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NLog.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddInMemoryCollection(SmtpActiveProfile.GetMergedEntries(builder.Configuration));

builder.Logging.ClearProviders();
builder.Host.UseNLog();
// Without a console logger, `dotnet run` shows almost no output (NLog writes to file only). Restore console in Development so URLs and errors are visible.
if (builder.Environment.IsDevelopment())
{
    builder.Logging.AddConsole();
    // Blazor/SignalR often log at Information/Debug; default Microsoft.* threshold can hide them.
    builder.Logging.AddFilter("Microsoft.AspNetCore.Components", LogLevel.Debug);
    builder.Logging.AddFilter("Microsoft.AspNetCore.Components.Server", LogLevel.Debug);
    builder.Logging.AddFilter("Microsoft.AspNetCore.SignalR", LogLevel.Debug);
}

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

builder.Services.AddScoped<AuthenticationStateProvider, HelpDesk.Web.Services.ServerAuthenticationStateProvider>();

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

builder.Services.Configure<AntiforgeryOptions>(options =>
{
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
});

// FallbackPolicy applies to Blazor's /_framework endpoints; without this, unauthenticated users get
// an HTML challenge response for blazor.web.js ("Unexpected token '<'" in the browser console).
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, BlazorInfrastructureAuthorizationMiddlewareResultHandler>();


builder.Services.AddSingleton<IExternalDatabaseContextFactory, ExternalDatabaseContextFactory>();

builder.Services.AddCascadingAuthenticationState();

builder.Services.AddSingleton<IErrorBoundaryLogger, HelpDeskErrorBoundaryLogger>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options =>
    {
        options.DetailedErrors = builder.Configuration.GetValue("DetailedErrors", false)
            || builder.Environment.IsDevelopment();
    });

builder.Services.AddControllers();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
// IIS / IIS Express should terminate TLS; reject any request that still appears as HTTP (misconfiguration or removed redirect).
app.Use(async (context, next) =>
{
    if (!context.Request.IsHttps)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }

    await next();
});
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
