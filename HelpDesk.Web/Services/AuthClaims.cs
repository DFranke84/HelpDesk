namespace HelpDesk.Web.Services;

public static class AuthClaims
{
    public const string MustChangePassword = "must_change_password";

    public const string SessionKind = "session_kind";
    public const string SessionWindows = "windows";
    public const string SessionForms = "forms";

    public const string LoginId = "login_id";

    /// <summary>For Windows integrated sessions, the raw Windows identity (e.g. DOMAIN\user).</summary>
    public const string WindowsName = "windows_name";
}
