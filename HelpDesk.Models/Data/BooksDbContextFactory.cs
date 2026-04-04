using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace HelpDesk.Models.Data;

/// <summary>
/// Design-time factory for Books database migrations and tooling.
/// </summary>
public class BooksDbContextFactory : IDesignTimeDbContextFactory<BooksDbContext>
{
    /// <summary>
    /// Build a Books context using web project configuration.
    /// </summary>
    /// <param name="args">Unused design-time arguments.</param>
    /// <returns>Configured <see cref="BooksDbContext"/>.</returns>
    public BooksDbContext CreateDbContext(string[] args)
    {
        var basePath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "HelpDesk.Web"));
        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddUserSecrets(typeof(BooksDbContextFactory).Assembly)
            .AddEnvironmentVariables()
            .Build();

        var cs = configuration.GetConnectionString("Books")
            ?? throw new InvalidOperationException("Connection string 'Books' is not configured.");

        var options = new DbContextOptionsBuilder<BooksDbContext>()
            .UseSqlServer(cs)
            .Options;

        return new BooksDbContext(options);
    }
}
