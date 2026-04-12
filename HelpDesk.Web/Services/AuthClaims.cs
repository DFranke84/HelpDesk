namespace HelpDesk.Web.Services;

public static class AuthClaims
{
    public const string MustChangePassword = "must_change_password";

    public const string SessionKind = "session_kind";
    public const string SessionWindows = "windows";
    public const string SessionForms = "forms";

    public const string LoginId = "login_id";

    /// <summary>Value matches <c>UserType</c> on the signed-in user row (person vs Windows group).</summary>
    public const string UserType = "user_type";

    /// <summary>For Windows integrated sessions, the raw Windows identity (e.g. DOMAIN\user).</summary>
    public const string WindowsName = "windows_name";

    /// <summary>JSON array of HelpDesk group IDs the user belongs to.</summary>
    public const string HelpdeskGroups = "helpdesk_groups";

    /// <summary>Primary group name (group with most access) for UI display.</summary>
    public const string PrimaryGroup = "primary_group";
}
