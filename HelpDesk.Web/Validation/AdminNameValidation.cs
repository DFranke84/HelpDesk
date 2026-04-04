using System.Text;

namespace HelpDesk.Web.Validation;

/// <summary>Catalog identifiers: uppercase, no whitespace (enforced in UI while typing and on save).</summary>
public static class AdminNameValidation
{
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

    /// <summary>Strips whitespace and uppercases each remaining character (login ID, app/group/page names).</summary>
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

    public static string? ErrorIfHasSpaces(string? value, string fieldLabel) =>
        ContainsDisallowedWhitespace(value) ? $"{fieldLabel} cannot contain spaces." : null;

    public static string? ErrorIfEmptyCatalogName(string? value, string fieldLabel) =>
        string.IsNullOrWhiteSpace(ToUpperNoSpaces(value)) ? $"{fieldLabel} is required." : null;
}
