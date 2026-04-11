namespace HelpDesk.Models;

/// <summary>Defines whether a User entity represents an individual person or a Windows group.</summary>
public enum UserType
{
    /// <summary>Individual person user with login credentials.</summary>
    User = 0,
    
    /// <summary>Windows/AD group treated as a user for permission assignment.</summary>
    Group = 1
}