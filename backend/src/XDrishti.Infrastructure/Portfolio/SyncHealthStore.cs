using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Portfolio;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Portfolio;

/// <summary>
/// The health figures of the syncs (<c>portfolio.broker_sync_state</c>) and the windows that dropped rows (<c>portfolio.broker_sync_windows</c>; ADR 0027). Every write is one guarded, set-based statement (the day arithmetic is done by the database in India time, see
/// <see cref="SyncHealthSql"/>), so two instances cannot lose an increment. The window notes run in the surrounding unit of work. Nothing is deleted.
/// </summary>
internal sealed class SyncHealthStore(XDrishtiDbContext db) : ISyncHealthStore
{
    public async ValueTask<SyncHealth> GetAsync(Guid accountId, SyncHealthKind kind, CancellationToken cancellationToken) =>
        await db.BrokerSyncStates.AsNoTracking()
            .Where(x => x.AccountId == accountId && x.Kind == kind)
            .Select(x => new SyncHealth(x.AccountId, x.Kind, x.ConsecutiveFailedDays, x.LastFailureReason, x.LastFailedAt, x.Attempts, x.DeepenedTo, x.DeepenExhausted, x.DeepenedOn, x.MapperVersion))
            .FirstOrDefaultAsync(cancellationToken)
        ?? SyncHealth.None(accountId, kind);

    public async ValueTask<IReadOnlyList<SyncHealth>> ListAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accountIds);
        if (accountIds.Count == 0)
        {
            return [];
        }

        return await db.BrokerSyncStates.AsNoTracking()
            .Where(x => accountIds.Contains(x.AccountId))
            .Select(x => new SyncHealth(x.AccountId, x.Kind, x.ConsecutiveFailedDays, x.LastFailureReason, x.LastFailedAt, x.Attempts, x.DeepenedTo, x.DeepenExhausted, x.DeepenedOn, x.MapperVersion))
            .ToListAsync(cancellationToken);
    }

    public async ValueTask RecordFailureAsync(Guid accountId, SyncHealthKind kind, string reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        await db.Database.ExecuteSqlRawAsync(
            SyncHealthSql.RecordFailure,
            [new NpgsqlParameter("account", accountId), new NpgsqlParameter("kind", Name(kind)), new NpgsqlParameter("reason", reason), new NpgsqlParameter("now", now.ToUniversalTime())],
            cancellationToken);
    }

    public async ValueTask RecordSuccessAsync(Guid accountId, SyncHealthKind kind, DateTimeOffset now, CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlRawAsync(
            SyncHealthSql.RecordSuccess,
            [new NpgsqlParameter("account", accountId), new NpgsqlParameter("kind", Name(kind)), new NpgsqlParameter("now", now.ToUniversalTime())],
            cancellationToken);

    public async ValueTask StampMapperVersionAsync(Guid accountId, SyncHealthKind kind, int version, DateTimeOffset now, CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO portfolio.broker_sync_state AS s (account_id, kind, mapper_version, updated_at)
            VALUES (@account, @kind, @version, @now)
            ON CONFLICT (account_id, kind) DO UPDATE SET mapper_version = @version, updated_at = @now
            WHERE s.mapper_version < @version
            """,
            [new NpgsqlParameter("account", accountId), new NpgsqlParameter("kind", Name(kind)), new NpgsqlParameter("version", version), new NpgsqlParameter("now", now.ToUniversalTime())],
            cancellationToken);

    public async ValueTask RecordDeepenedAsync(Guid accountId, DateOnly? deepenedTo, bool exhausted, DateOnly on, DateTimeOffset now, CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO portfolio.broker_sync_state AS s (account_id, kind, deepened_to, deepen_exhausted, deepened_on, updated_at)
            VALUES (@account, 'trades', @to, @exhausted, @on, @now)
            ON CONFLICT (account_id, kind) DO UPDATE SET
                deepened_to = LEAST(s.deepened_to, EXCLUDED.deepened_to),
                deepen_exhausted = s.deepen_exhausted OR EXCLUDED.deepen_exhausted,
                deepened_on = EXCLUDED.deepened_on,
                updated_at = EXCLUDED.updated_at
            """,
            [
                new NpgsqlParameter("account", accountId),
                new NpgsqlParameter("to", NpgsqlDbType.Date) { Value = deepenedTo.HasValue ? deepenedTo.Value : DBNull.Value },
                new NpgsqlParameter("exhausted", exhausted),
                new NpgsqlParameter("on", NpgsqlDbType.Date) { Value = on },
                new NpgsqlParameter("now", now.ToUniversalTime()),
            ],
            cancellationToken);

    public async ValueTask NoteWindowAsync(Guid accountId, SyncHealthKind kind, DateOnly from, DateOnly to, int dropped, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var name = Name(kind);
        var time = now.ToUniversalTime();

        // A read of these dates clears the marks of the windows it covers (they were read again) ...
        await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE portfolio.broker_sync_windows SET needs_reread = false, noted_at = @now
            WHERE account_id = @account AND kind = @kind AND needs_reread AND from_date >= @from AND to_date <= @to
            """,
            [new NpgsqlParameter("account", accountId), new NpgsqlParameter("kind", name), new NpgsqlParameter("from", NpgsqlDbType.Date) { Value = from }, new NpgsqlParameter("to", NpgsqlDbType.Date) { Value = to }, new NpgsqlParameter("now", time)],
            cancellationToken);
        if (dropped <= 0)
        {
            return;
        }

        // ... and a window that dropped rows for a data reason is marked for a new read once the mapping changes.
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO portfolio.broker_sync_windows (account_id, kind, from_date, to_date, dropped, needs_reread, noted_at)
            VALUES (@account, @kind, @from, @to, @dropped, true, @now)
            ON CONFLICT (account_id, kind, from_date) DO UPDATE SET to_date = EXCLUDED.to_date, dropped = EXCLUDED.dropped, needs_reread = true, noted_at = EXCLUDED.noted_at
            """,
            [
                new NpgsqlParameter("account", accountId),
                new NpgsqlParameter("kind", name),
                new NpgsqlParameter("from", NpgsqlDbType.Date) { Value = from },
                new NpgsqlParameter("to", NpgsqlDbType.Date) { Value = to },
                new NpgsqlParameter("dropped", dropped),
                new NpgsqlParameter("now", time),
            ],
            cancellationToken);
    }

    public async ValueTask<IReadOnlyList<BrokerDateRange>> ListRereadAsync(Guid accountId, SyncHealthKind kind, CancellationToken cancellationToken) =>
        [.. (await db.BrokerSyncWindows.AsNoTracking()
            .Where(x => x.AccountId == accountId && x.Kind == kind && x.NeedsReread)
            .OrderBy(x => x.FromDate)
            .Select(x => new { x.FromDate, x.ToDate })
            .ToListAsync(cancellationToken)).Select(x => new BrokerDateRange(x.FromDate, x.ToDate))];

    private static string Name(SyncHealthKind kind) => kind.ToString().ToLowerInvariant();
}
