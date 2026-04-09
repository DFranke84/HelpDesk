namespace HelpDesk.Models;

/// <summary>Types that participate in EntityChangeHistory auditing.</summary>
public static class TrackedEntityType
{
    public const string User = nameof(User);
    public const string DeskApplication = nameof(DeskApplication);
    public const string DeskPage = nameof(DeskPage);
    public const string DeskGroup = nameof(DeskGroup);
    public const string UserGroup = nameof(UserGroup);
    public const string DeskApplicationGroup = nameof(DeskApplicationGroup);
    public const string UserPagePermission = nameof(UserPagePermission);
}

