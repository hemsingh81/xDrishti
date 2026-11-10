using Npgsql;
using XDrishti.Application.Abstractions.Auto;

namespace XDrishti.Infrastructure.Auto;

/// <summary>
/// The select-only reads of the AU programme check (spec AU-19, ADR 0253): recovery ledger, switch log, plans. One read-only transaction per fact; a table that cannot be read (missing, or no privilege) makes that one fact null (NOT EVALUABLE), never zero.
/// <paramref name="adrFolder"/> is the repository's docs/adr folder when the host can see it, else null.
/// </summary>
internal sealed class AutoProgrammeFactsReader(NpgsqlDataSource dataSource, string? adrFolder) : IAutoProgrammeFactsReader
{
    internal const string RemovalAdrPattern = "0245-*.md";

    private const string RecoveryPasses = "SELECT count(*) FROM platform.recovery_passes";

    // a multi-day recovery: an item that succeeded in a pass, made a run, and covers more than one session
    private const string MultiDay = "SELECT count(*) FROM platform.recovery_items WHERE state = 'succeeded' AND run_id IS NOT NULL AND session_from < session_to";

    private const string SwitchChanges = "SELECT count(*) FROM planning.auto_switch_changes";

    // a published, not recovered, run whose newest switch change before it said ON, with at least one primary ticket
    private const string Nights = """
        SELECT count(*) FROM trading.plan_runs r
        WHERE r.outcome = 'published' AND NOT r.recovered
          AND COALESCE((SELECT c.kind FROM planning.auto_switch_changes c WHERE c.at <= r.started_at ORDER BY c.at DESC, c.id DESC LIMIT 1), 'off') = 'on'
          AND EXISTS (SELECT 1 FROM trading.plan_items i WHERE i.plan_id = r.plan_id AND i.item_group = 'primary')
        """;

    public async ValueTask<AutoStoredFacts> ReadAsync(CancellationToken cancellationToken)
    {
        // each read has its own read-only transaction; a table that cannot be read leaves that one fact null
        var passes = await CountAsync(RecoveryPasses, cancellationToken);
        var multi = await CountAsync(MultiDay, cancellationToken);
        var changes = await CountAsync(SwitchChanges, cancellationToken);
        var nights = await CountAsync(Nights, cancellationToken);
        return new AutoStoredFacts(passes, multi, RemovalAdr(), changes, nights);
    }

    private bool? RemovalAdr() =>
        adrFolder is { Length: > 0 } && Directory.Exists(adrFolder) ? Directory.EnumerateFiles(adrFolder, RemovalAdrPattern).Any() : null;

    private async ValueTask<int?> CountAsync(string sql, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction))
            {
                await readOnly.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var command = new NpgsqlCommand(sql, connection, transaction);
            var count = await command.ExecuteScalarAsync(cancellationToken);
            return count is long n ? (int)Math.Min(n, int.MaxValue) : null;
        }
        catch (PostgresException failure) when (failure.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedColumn or PostgresErrorCodes.InsufficientPrivilege)
        {
            return null;
        }
    }
}
