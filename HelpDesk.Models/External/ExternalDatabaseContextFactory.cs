using Microsoft.Extensions.Configuration;

namespace HelpDesk.Models.External;

public sealed class ExternalDatabaseContextFactory : IExternalDatabaseContextFactory
{
    private readonly IConfiguration _configuration;

    public ExternalDatabaseContextFactory(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string? GetConnectionString(string? configurationKeyName)
    {
        if (string.IsNullOrWhiteSpace(configurationKeyName))
            return null;
        return _configuration.GetConnectionString(configurationKeyName)
               ?? _configuration[configurationKeyName];
    }
}

