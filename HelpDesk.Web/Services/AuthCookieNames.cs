namespace HelpDesk.Web.Services;

/// <summary>Cookie names shared by auth middleware and account endpoints.</summary>
public static class AuthCookieNames
{
    public const string Auth = "HelpDesk.Auth";
    /// <summary>When set after an explicit logout, integrated Windows auto-login is skipped until expiry or successful forms login.</summary>
    public const string SuppressWindowsAutoLogin = "HelpDesk.SuppressWindowsAutoLogin";
}
