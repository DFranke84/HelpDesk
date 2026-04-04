using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace HelpDesk.Models.Data;

/// <summary>
/// Design-time factory for Help Desk migrations and tooling.
/// </summary>
public class HelpDeskDbContextFactory : IDesignTimeDbContextFactory<HelpDeskDbContext>
{
    /// <summary>
    /// Build a Help Desk context using web project configuration.
    /// </summary>
    /// <param name="args">Unused design-time arguments.</param>
    /// <returns>Configured <see cref="HelpDeskDbContext"/>.</returns>
    public HelpDeskDbContext CreateDbContext(string[] args)
    {
        var basePath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "HelpDesk.Web"));
        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddUserSecrets(typeof(HelpDeskDbContextFactory).Assembly)
            .AddEnvironmentVariables()
            .Build();

        var options = new DbContextOptionsBuilder<HelpDeskDbContext>()
            .UseSqlServer(configuration.GetConnectionString("HelpDesk"))
            .Options;

        return new HelpDeskDbContext(options);
    }
}

