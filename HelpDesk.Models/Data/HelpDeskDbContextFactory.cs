using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace HelpDesk.Models.Data;

public class HelpDeskDbContextFactory : IDesignTimeDbContextFactory<HelpDeskDbContext>
{
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

