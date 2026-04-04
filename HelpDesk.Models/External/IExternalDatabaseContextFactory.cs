namespace HelpDesk.Models.External;

/// <summary>
/// Resolves optional per-application SQL connections by configuration key name.
/// Connection strings live in appsettings / secrets — not in the database.
/// </summary>
public interface IExternalDatabaseContextFactory
{
    /// <summary>
    /// Resolve a connection string when the named configuration key exists.
    /// </summary>
    /// <param name="configurationKeyName">Connection string key or path.</param>
    /// <returns>Connection string text, or null when not configured.</returns>
    string? GetConnectionString(string? configurationKeyName);
}

