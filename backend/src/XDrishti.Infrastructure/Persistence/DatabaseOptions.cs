using System.ComponentModel.DataAnnotations;

namespace XDrishti.Infrastructure.Persistence;

/// <summary>
/// Database settings. The password is never part of the connection string in configuration:
/// it comes from the secret file mounted at /run/secrets (KeyPerFile provider) or user-secrets in development.
/// </summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Connection string without a password, e.g. "Host=xd-db;Database=xdrishti;Username=xd_app".</summary>
    [Required]
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Password, supplied by a secret. Optional only for trust/peer authentication.</summary>
    public string? Password { get; set; }

    /// <summary>Command timeout in seconds for normal requests.</summary>
    [Range(1, 600)]
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>Upper bound of pooled physical connections.</summary>
    [Range(1, 500)]
    public int MaxPoolSize { get; set; } = 50;
}
