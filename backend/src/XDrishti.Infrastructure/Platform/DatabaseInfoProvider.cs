using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Platform;

internal sealed partial class DatabaseInfoProvider(NpgsqlDataSource dataSource, XDrishtiDbContext db, ILogger<DatabaseInfoProvider> logger)
    : IDatabaseInfoProvider
{
    private const string VersionSql =
        "select current_setting('server_version'), (select extversion from pg_extension where extname = 'timescaledb')";

    public async ValueTask<DatabaseInfo> GetAsync(CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            await using var command = dataSource.CreateCommand(VersionSql);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            var server = reader.GetString(0);
            var timescale = reader.IsDBNull(1) ? null : reader.GetString(1);
            var latency = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

            var applied = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();
            var pending = db.Database.GetMigrations().Except(applied, StringComparer.Ordinal).Count();

            return new DatabaseInfo(true, server, timescale, applied.LastOrDefault(), pending, Math.Round(latency, 2), null);
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException or InvalidOperationException)
        {
            LogDatabaseUnavailable(logger, ex);
            return new DatabaseInfo(false, null, null, null, 0, Stopwatch.GetElapsedTime(started).TotalMilliseconds, ex.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Database status check failed")]
    private static partial void LogDatabaseUnavailable(ILogger logger, Exception exception);
}
