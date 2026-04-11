using HelpDesk.Books.Models.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDesk.Books.Extensions;

/// <summary>
/// Extension methods for registering HelpDesk.Books services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds HelpDesk.Books services to the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The Books database connection string.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddHelpDeskBooks(this IServiceCollection services, string connectionString)
    {
        // Register BooksDbContext with factory pattern
        services.AddDbContextFactory<BooksDbContext>(options =>
            options.UseSqlServer(connectionString), ServiceLifetime.Scoped);

        return services;
    }
}

/// <summary>
/// Extension methods for configuring HelpDesk.Books in the application pipeline.
/// </summary>
public static class WebApplicationExtensions
{
    /// <summary>
    /// Maps HelpDesk.Books routes and static files.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <returns>The web application for chaining.</returns>
    public static WebApplication MapHelpDeskBooks(this WebApplication app)
    {
        // Configure static files to serve from HelpDesk.Books library
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new Microsoft.Extensions.FileProviders.EmbeddedFileProvider(
                typeof(ServiceCollectionExtensions).Assembly,
                "HelpDesk.Books.wwwroot"),
            RequestPath = new Microsoft.AspNetCore.Http.PathString("/books-assets")
        });

        return app;
    }
}