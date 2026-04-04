using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace HelpDesk.Models.Data;

public class BooksDbContextFactory : IDesignTimeDbContextFactory<BooksDbContext>
{
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
