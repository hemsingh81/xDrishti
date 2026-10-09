using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace XDrishti.Infrastructure.Persistence;

/// <summary>Applies pending EF Core migrations. Run once per deploy (`xd-api migrate`), never on every app start.</summary>
public static partial class DatabaseMigrator
{
    public static async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseMigrator));

        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        LogPending(logger, pending.Count);
        await db.Database.MigrateAsync(cancellationToken);
        LogDone(logger);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Applying {Count} pending migration(s)")]
    private static partial void LogPending(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Database is up to date")]
    private static partial void LogDone(ILogger logger);
}
