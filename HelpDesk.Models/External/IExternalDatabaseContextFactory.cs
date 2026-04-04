namespace HelpDesk.Models.External;

/// <summary>
/// Resolves optional per-application SQL connections by configuration key name.
/// Connection strings live in appsettings / secrets — not in the database.
/// </summary>
public interface IExternalDatabaseContextFactory
{
    /// <summary>Returns a connection string if the named key exists; otherwise null.</summary>
    string? GetConnectionString(string? configurationKeyName);
}

