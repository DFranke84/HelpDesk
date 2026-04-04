using Microsoft.Extensions.Configuration;

namespace HelpDesk.Models.External;

/// <summary>
/// Resolve external SQL connection strings from configuration keys.
/// </summary>
public sealed class ExternalDatabaseContextFactory : IExternalDatabaseContextFactory
{
    private readonly IConfiguration _configuration;

    /// <summary>
    /// Create factory with application configuration.
    /// </summary>
    /// <param name="configuration">Application configuration <see cref="IConfiguration"/>.</param>
    public ExternalDatabaseContextFactory(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// Look up a connection string by configuration key name.
    /// </summary>
    /// <param name="configurationKeyName">Connection string key or path.</param>
    /// <returns>Connection string text, or null when not configured.</returns>
    public string? GetConnectionString(string? configurationKeyName)
    {
        if (string.IsNullOrWhiteSpace(configurationKeyName))
            return null;
        return _configuration.GetConnectionString(configurationKeyName)
               ?? _configuration[configurationKeyName];
    }
}

