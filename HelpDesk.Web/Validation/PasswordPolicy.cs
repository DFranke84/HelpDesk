namespace HelpDesk.Web.Validation;

/// <summary>Shared password validation rules referenced by change-password pages and the reset service.</summary>
public static class PasswordPolicy
{
    public const int MinLength = 6;
    public const string MinLengthMessage = "New password must be at least 6 characters.";
}
