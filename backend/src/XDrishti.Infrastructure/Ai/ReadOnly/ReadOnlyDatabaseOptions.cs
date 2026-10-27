using System.ComponentModel.DataAnnotations;

namespace XDrishti.Infrastructure.Ai.ReadOnly;

/// <summary>
/// Settings of the tool server's two read-only logins (spec P9-02), section <c>Database</c> of the <c>xd-mcp</c> host. The connection strings carry no password; each password comes from its own secret file (<c>Database__ReadPassword</c>, <c>Database__SqlPassword</c>). Not the
/// application's <see cref="Persistence.DatabaseOptions"/>: the tool server never holds the runtime role's connection.
/// </summary>
public sealed class ReadOnlyDatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Login <c>xd_mcp_read</c>: the typed tools, the catalog and the settings read.</summary>
    [Required]
    public string ReadConnectionString { get; set; } = string.Empty;

    /// <summary>Login <c>xd_mcp_sql</c>: <c>run_sql</c> only.</summary>
    [Required]
    public string SqlConnectionString { get; set; } = string.Empty;

    public string? ReadPassword { get; set; }

    public string? SqlPassword { get; set; }

    /// <summary>The options never show a password.</summary>
    public override string ToString() => "ReadOnlyDatabaseOptions { passwords hidden }";
}
