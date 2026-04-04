namespace HelpDesk.Web.Configuration;

/// <summary>
/// Copies <c>Smtp:Standard</c> or <c>Smtp:Direct</c> into flat <c>Smtp:*</c> keys (Host, Port, EnableSsl, etc.)
/// based on <c>Smtp:Mode</c> so NLog, <see cref="Services.EmailSender"/>, and the rest of the app use one active connection.
/// </summary>
public static class SmtpActiveProfile
{
    public const string ModeStandard = "Standard";
    public const string ModeDirect = "Direct";

    /// <summary>
    /// Returns key/value pairs to merge (e.g. Smtp:Host, Smtp:Port) from the selected profile.
    /// Shared keys such as Smtp:From and Smtp:AlertTo remain on the root <c>Smtp</c> section in appsettings.
    /// </summary>
    /// <summary>
    /// Merge the selected SMTP profile into flat Smtp:* configuration keys.
    /// </summary>
    /// <param name="configuration">Application configuration <see cref="IConfiguration"/>.</param>
    /// <returns>Key/value pairs to overlay onto configuration.</returns>
    public static IReadOnlyList<KeyValuePair<string, string?>> GetMergedEntries(IConfiguration configuration)
    {
        var mode = configuration["Smtp:Mode"] ?? ModeStandard;
        var sectionName = mode.Equals(ModeDirect, StringComparison.OrdinalIgnoreCase) ? ModeDirect : ModeStandard;
        var profile = configuration.GetSection($"Smtp:{sectionName}");
        if (!profile.Exists())
            return Array.Empty<KeyValuePair<string, string?>>();

        var list = new List<KeyValuePair<string, string?>>();
        foreach (var child in profile.GetChildren())
            list.Add(new KeyValuePair<string, string?>($"Smtp:{child.Key}", child.Value));

        return list;
    }
}
