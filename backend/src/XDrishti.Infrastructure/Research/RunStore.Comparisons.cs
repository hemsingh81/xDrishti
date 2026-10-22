using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;

namespace XDrishti.Infrastructure.Research;

/// <summary>
/// The comparisons of the result store (spec P4-05, D10): one insert of the header into <c>research.sim_comparisons</c> that does nothing on a stored comparison key, after a check inside the transaction that every member run is stored. The
/// members are runs of their own and are joined to one another on <c>trade_key</c> through the entries key. Insert-only; never logs a table or a trade.
/// </summary>
internal sealed partial class RunStore
{
    private const string InsertComparison = """
        INSERT INTO research.sim_comparisons (comparison_key, entries_key, strategy_id, unit, common_entries, member_run_keys, member_candidate_ids, member_gated_blocked,
            comparison_csv, diagnostics_csv, record_sha256, created_at, created_by)
        VALUES (@comparison_key, @entries_key, @strategy_id, @unit, @common_entries, @member_run_keys, @member_candidate_ids, @member_gated_blocked,
            @comparison_csv, @diagnostics_csv, @record_sha256, @created_at, @created_by)
        ON CONFLICT (comparison_key) DO NOTHING
        RETURNING id
        """;

    private const string SelectComparison = "SELECT id, entries_key, record_sha256, member_run_keys, member_candidate_ids, member_gated_blocked, common_entries, unit FROM research.sim_comparisons WHERE comparison_key = @comparison_key";

    public async ValueTask<Result<StoreOutcome>> StoreComparisonAsync(ComparisonToStore comparison, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            var keys = comparison.Members.Select(m => m.RunKey).ToArray();
            await using (var count = new NpgsqlCommand("SELECT count(*) FROM research.sim_runs WHERE run_key = ANY(@keys)", connection, transaction))
            {
                count.Parameters.AddWithValue("keys", NpgsqlDbType.Array | NpgsqlDbType.Char, keys);
                if ((long)(await count.ExecuteScalarAsync(cancellationToken))! != keys.Length)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Result.Failure<StoreOutcome>(StoreErrors.WriteFailed("A member run of the comparison is not stored; store the runs first. Nothing was stored."));
                }
            }

            long? id;
            await using (var insert = new NpgsqlCommand(InsertComparison, connection, transaction))
            {
                var p = insert.Parameters;
                p.AddWithValue("comparison_key", NpgsqlDbType.Char, comparison.ComparisonKey);
                p.AddWithValue("entries_key", NpgsqlDbType.Char, comparison.EntriesKey);
                p.AddWithValue("strategy_id", NpgsqlDbType.Varchar, comparison.StrategyId);
                p.AddWithValue("unit", NpgsqlDbType.Varchar, comparison.Unit);
                p.AddWithValue("common_entries", NpgsqlDbType.Integer, comparison.CommonEntries);
                p.AddWithValue("member_run_keys", NpgsqlDbType.Array | NpgsqlDbType.Char, keys);
                p.AddWithValue("member_candidate_ids", NpgsqlDbType.Array | NpgsqlDbType.Varchar, comparison.Members.Select(m => m.CandidateId).ToArray());
                p.AddWithValue("member_gated_blocked", NpgsqlDbType.Array | NpgsqlDbType.Bigint, comparison.Members.Select(m => m.GatedBlocked).ToArray());
                p.AddWithValue("comparison_csv", NpgsqlDbType.Text, comparison.Record.ComparisonCsv);
                p.AddWithValue("diagnostics_csv", NpgsqlDbType.Text, comparison.Record.DiagnosticsCsv);
                p.AddWithValue("record_sha256", NpgsqlDbType.Char, comparison.Record.RecordSha256);
                p.AddWithValue("created_at", NpgsqlDbType.TimestampTz, comparison.CreatedAt.ToUniversalTime());
                p.AddWithValue("created_by", NpgsqlDbType.Varchar, comparison.Actor);
                id = await insert.ExecuteScalarAsync(cancellationToken) as long?;
            }

            if (id is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return Result.Success(StoreOutcome.Stored(id.Value, 0));
            }

            await transaction.RollbackAsync(cancellationToken);
            return await CompareComparisonAsync(connection, comparison, cancellationToken);
        }
        catch (PostgresException failure)
        {
            return Result.Failure<StoreOutcome>(StoreErrors.WriteFailed(Describe(failure)));
        }
        catch (NpgsqlException failure) when (!cancellationToken.IsCancellationRequested)
        {
            return Result.Failure<StoreOutcome>(StoreErrors.WriteFailed("The database could not be used: " + failure.GetType().Name + "; nothing was stored."));
        }
    }

    public async ValueTask<StoredComparisonHeader?> FindComparisonAsync(string comparisonKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(comparisonKey);
        var row = await db.SimComparisons.AsNoTracking().Where(x => x.ComparisonKey == comparisonKey).FirstOrDefaultAsync(cancellationToken);
        return row is null
            ? null
            : new StoredComparisonHeader(
                row.Id, row.ComparisonKey, row.EntriesKey, row.StrategyId, row.Unit, row.CommonEntries,
                [.. row.MemberRunKeys.Select((key, i) => new ComparisonMemberToStore(key, row.MemberCandidateIds[i], row.MemberGatedBlocked[i]))],
                row.RecordSha256, row.CreatedAt, row.CreatedBy);
    }

    /// <summary>The comparison key is taken: the same comparison is a no-op, another record under it is a conflict (nothing is written either way).</summary>
    private static async Task<Result<StoreOutcome>> CompareComparisonAsync(NpgsqlConnection connection, ComparisonToStore comparison, CancellationToken cancellationToken)
    {
        await using var select = new NpgsqlCommand(SelectComparison, connection);
        select.Parameters.AddWithValue("comparison_key", NpgsqlDbType.Char, comparison.ComparisonKey);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return Result.Failure<StoreOutcome>(StoreErrors.WriteFailed("The comparison key was taken and then released while it was stored; store it again."));
        }

        var differing = new List<string>();
        if (!string.Equals(reader.GetString(1), comparison.EntriesKey, StringComparison.Ordinal))
        {
            differing.Add("entries_key");
        }

        if (!string.Equals(reader.GetString(2), comparison.Record.RecordSha256, StringComparison.Ordinal))
        {
            differing.Add("record");
        }

        if (!reader.GetFieldValue<string[]>(3).SequenceEqual(comparison.Members.Select(m => m.RunKey), StringComparer.Ordinal)
            || !reader.GetFieldValue<string[]>(4).SequenceEqual(comparison.Members.Select(m => m.CandidateId), StringComparer.Ordinal)
            || !reader.GetFieldValue<long[]>(5).SequenceEqual(comparison.Members.Select(m => m.GatedBlocked)))
        {
            differing.Add("members");
        }

        if (reader.GetInt32(6) != comparison.CommonEntries || !string.Equals(reader.GetString(7), comparison.Unit, StringComparison.Ordinal))
        {
            differing.Add("common_set");
        }

        return differing.Count == 0
            ? Result.Success(StoreOutcome.AlreadyStored(reader.GetInt64(0)))
            : Result.Failure<StoreOutcome>(StoreErrors.RunConflict(comparison.ComparisonKey, differing));
    }
}
