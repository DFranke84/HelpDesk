using System.Text;

namespace HelpDesk.Web.Validation;

/// <summary>
/// Catalog names: no whitespace (stripped in UI for applications, groups, pages). Login IDs use <see cref="NormalizeLoginId"/> (no spaces, casing preserved).
/// </summary>
public static class AdminNameValidation
{
    /// <summary>
    /// Detect whitespace characters in a catalog or login identifier.
    /// </summary>
    /// <param name="s">Candidate string.</param>
    /// <returns>True when any whitespace is present.</returns>
    public static bool ContainsDisallowedWhitespace(string? s)
    {
        if (string.IsNullOrEmpty(s))
            return false;
        foreach (var c in s)
        {
            if (char.IsWhiteSpace(c))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Strip whitespace and uppercase remaining characters for catalog identifiers.
    /// </summary>
    /// <param name="s">Raw input text.</param>
    /// <returns>Normalized uppercase string without spaces.</returns>
    public static string ToUpperNoSpaces(string? s)
    {
        if (string.IsNullOrEmpty(s))
            return "";
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (!char.IsWhiteSpace(c))
                sb.Append(char.ToUpperInvariant(c));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Strip whitespace from a login id; preserve character casing. Use for forms login, admin user edit, and DB lookups (match with case-insensitive equality).
    /// </summary>
    /// <param name="s">Raw input text.</param>
    /// <returns>String without whitespace characters.</returns>
    public static string NormalizeLoginId(string? s) => RemoveWhitespace(s);

    /// <summary>
    /// Remove all whitespace; preserve character casing (for application, group, and page names).
    /// </summary>
    /// <param name="s">Raw input text.</param>
    /// <returns>String without whitespace characters.</returns>
    public static string RemoveWhitespace(string? s)
    {
        if (string.IsNullOrEmpty(s))
            return "";
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (!char.IsWhiteSpace(c))
                sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Return a validation error when the value contains spaces.
    /// </summary>
    /// <param name="value">Field value to inspect.</param>
    /// <param name="fieldLabel">Human-readable field name for the message.</param>
    /// <returns>Error text, or null when valid.</returns>
    public static string? ErrorIfHasSpaces(string? value, string fieldLabel) =>
        ContainsDisallowedWhitespace(value) ? $"{fieldLabel} cannot contain spaces." : null;

    /// <summary>
    /// Return a validation error when the normalized catalog name is empty.
    /// </summary>
    /// <param name="value">Field value to inspect.</param>
    /// <param name="fieldLabel">Human-readable field name for the message.</param>
    /// <returns>Error text, or null when valid.</returns>
    public static string? ErrorIfEmptyCatalogName(string? value, string fieldLabel) =>
        string.IsNullOrEmpty(RemoveWhitespace(value)) ? $"{fieldLabel} is required." : null;
}
