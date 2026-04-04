namespace HelpDesk.Models;

public static class UserDisplayName
{
    public static string Format(string? firstName, string? lastName, string loginId)
    {
        var fn = firstName?.Trim() ?? "";
        var ln = lastName?.Trim() ?? "";
        if (fn.Length > 0 || ln.Length > 0)
            return $"{fn} {ln}".Trim();
        return loginId;
    }

    public static string Format(User user) => Format(user.FirstName, user.LastName, user.LoginId);
}

