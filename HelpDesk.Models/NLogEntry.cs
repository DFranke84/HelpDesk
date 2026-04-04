using System.ComponentModel.DataAnnotations.Schema;

namespace HelpDesk.Models;

/// <summary>Database-backed log record written by NLog.</summary>
public sealed class NLogEntry
{
    public long Id { get; set; }

    public string MachineName { get; set; } = string.Empty;

    public DateTime Logged { get; set; }

    public string Level { get; set; } = "Info";

    public string Message { get; set; } = string.Empty;

    public string? Logger { get; set; }

    public string? Exception { get; set; }

    public decimal? ElapsedTime { get; set; }

    public int? NumIterations { get; set; }

    public string? ActivityCategory { get; set; }

    public Guid? CorrelationId { get; set; }

    /// <summary>Login ID at time of log entry (no FK — row survives user delete).</summary>
    [Column("LoginID")]
    public string? LoginId { get; set; }

    /// <summary>For Windows integrated sessions, the raw Windows identity (e.g. DOMAIN\user).</summary>
    public string? DomainLogin { get; set; }

    public int? ReturnCode { get; set; }
}

