using HelpDesk.Models;

namespace HelpDesk.Web.Services;

/// <summary>Service for validating user and group business rules.</summary>
public interface IUserValidationService
{
    /// <summary>Validate a user entity before creating or updating.</summary>
    ValidationResult ValidateUser(User user, bool isNewUser, string? password = null);
    
    /// <summary>Check if a user type supports password operations.</summary>
    bool SupportsPasswordOperations(UserType userType);
    
    /// <summary>Check if a user type supports email operations.</summary>
    bool SupportsEmailOperations(UserType userType);
    
    /// <summary>Check if a user type can login with credentials.</summary>
    bool SupportsCredentialLogin(UserType userType);
}

public class UserValidationService : IUserValidationService
{
    public ValidationResult ValidateUser(User user, bool isNewUser, string? password = null)
    {
        var result = new ValidationResult();

        // Validate LoginId (required for all types)
        if (string.IsNullOrWhiteSpace(user.LoginId))
        {
            result.AddError("Login ID is required.");
        }

        if (user.UserType == UserType.User)
        {
            ValidateUserEntity(user, isNewUser, password, result);
        }
        else if (user.UserType == UserType.Group)
        {
            ValidateGroupEntity(user, result);
        }

        return result;
    }

    public bool SupportsPasswordOperations(UserType userType) => userType == UserType.User;

    public bool SupportsEmailOperations(UserType userType) => userType == UserType.User;

    public bool SupportsCredentialLogin(UserType userType) => userType == UserType.User;

    private void ValidateUserEntity(User user, bool isNewUser, string? password, ValidationResult result)
    {
        // Users require first and last names
        if (string.IsNullOrWhiteSpace(user.FirstName))
        {
            result.AddError("First name is required for users.");
        }

        if (string.IsNullOrWhiteSpace(user.LastName))
        {
            result.AddError("Last name is required for users.");
        }

        // Password validation for new users
        if (isNewUser && string.IsNullOrWhiteSpace(password))
        {
            result.AddError("Password is required for new users.");
        }

        // Users should not have MustChangePassword = true if they're groups (this is enforced by DB constraints too)
        // Email validation could be added here if needed
    }

    private void ValidateGroupEntity(User group, ValidationResult result)
    {
        // Groups should have a display name in the FirstName field
        if (string.IsNullOrWhiteSpace(group.FirstName))
        {
            result.AddError("Group name is required for groups.");
        }

        // Groups should not have certain fields
        if (!string.IsNullOrEmpty(group.Email))
        {
            result.AddError("Groups cannot have email addresses.");
        }

        if (!string.IsNullOrEmpty(group.PasswordHash))
        {
            result.AddError("Groups cannot have passwords.");
        }

        if (group.MustChangePassword)
        {
            result.AddError("Groups cannot be required to change passwords.");
        }

        // LastName should be null for groups
        if (!string.IsNullOrEmpty(group.LastName))
        {
            result.AddError("Groups should not have a last name.");
        }
    }
}

/// <summary>Represents validation results with any errors found.</summary>
public class ValidationResult
{
    private readonly List<string> _errors = new();

    public bool IsValid => !_errors.Any();
    public IReadOnlyList<string> Errors => _errors.AsReadOnly();

    public void AddError(string error)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            _errors.Add(error);
        }
    }

    public string GetErrorMessage() => string.Join(" ", _errors);
}