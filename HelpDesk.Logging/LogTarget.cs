namespace HelpDesk.Logging;

/// <summary>Controls which NLog targets receive a manually-initiated log entry.</summary>
public enum LogTarget
{
    /// <summary>Write to the NLog database table only (default). Error/Fatal still reach the file and email targets via NLog rules.</summary>
    Database,

    /// <summary>Write to the log file only.</summary>
    File,

    /// <summary>Write to both the log file and the NLog database table.</summary>
    Both
}
