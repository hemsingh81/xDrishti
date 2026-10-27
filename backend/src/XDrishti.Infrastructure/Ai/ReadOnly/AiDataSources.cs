using Microsoft.Extensions.Options;
using Npgsql;

namespace XDrishti.Infrastructure.Ai.ReadOnly;

/// <summary>
/// The two pooled data sources of the tool server, one per login (spec P9-02). Created on first use, so the host starts (and answers its health check, red) even when the roles have no password yet. The pools are as small as the roles' connection limits (3 and 6), so the
/// server never asks for more than the database lets the role have.
/// </summary>
internal sealed class AiDataSources(IOptions<ReadOnlyDatabaseOptions> options) : IAsyncDisposable
{
    private const string ApplicationName = "xd-mcp";

    private readonly Lazy<NpgsqlDataSource> _read = new(() => Build(options.Value.ReadConnectionString, options.Value.ReadPassword, 6), LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly Lazy<NpgsqlDataSource> _sql = new(() => Build(options.Value.SqlConnectionString, options.Value.SqlPassword, 3), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The login of the typed tools (<c>xd_mcp_read</c>).</summary>
    public NpgsqlDataSource Read => _read.Value;

    /// <summary>The login of <c>run_sql</c> (<c>xd_mcp_sql</c>).</summary>
    public NpgsqlDataSource Sql => _sql.Value;

    public async ValueTask DisposeAsync()
    {
        if (_read.IsValueCreated)
        {
            await _read.Value.DisposeAsync();
        }

        if (_sql.IsValueCreated)
        {
            await _sql.Value.DisposeAsync();
        }
    }

    private static NpgsqlDataSource Build(string connectionString, string? password, int maxPoolSize)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            ApplicationName = ApplicationName,
            Pooling = true,
            MinPoolSize = 0,
            MaxPoolSize = maxPoolSize,
            Timeout = 5,
            CommandTimeout = 15,
        };
        if (!string.IsNullOrEmpty(password))
        {
            builder.Password = password;
        }

        return new NpgsqlDataSourceBuilder(builder.ConnectionString).Build();
    }
}
