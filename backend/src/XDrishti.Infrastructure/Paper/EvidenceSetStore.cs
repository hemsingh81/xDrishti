using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Shadow;

namespace XDrishti.Infrastructure.Paper;

/// <summary>
/// The frozen evidence sets behind <see cref="IEvidenceSetStore"/> (spec AM-01 GAP-09, ADR 0282): raw SQL, select and insert only (the runtime role has no more). A set and its ledger rows are stored in one transaction. The database refuses a set once a shadow entry of the
/// epoch exists, so a replace after the first write fails there as well as in the handler. <see cref="InsertAsync"/> is also what <see cref="ShadowStore"/> calls for the automatic set, inside the first session's transaction.
/// </summary>
internal sealed class EvidenceSetStore(NpgsqlDataSource dataSource) : IEvidenceSetStore
{
    public async ValueTask<StoredEvidenceSet?> CurrentAsync(long edgeKey, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("""
            SELECT s.id, s.edge_epoch, s.versions::text, s.set_hash, s.mode, s.actor, s.reason, s.created_at, s.first_write_at,
                   EXISTS (SELECT 1 FROM paper.shadow_entries e WHERE e.edge_epoch = s.edge_epoch OR (s.edge_epoch = 0 AND e.edge_epoch IS NULL))
            FROM paper.evidence_sets s WHERE s.edge_epoch = @key ORDER BY s.id DESC LIMIT 1
            """);
        select.Parameters.AddWithValue("key", NpgsqlDbType.Bigint, edgeKey);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var versions = JsonSerializer.Deserialize<string[]>(reader.GetString(2)) ?? [];
        return new StoredEvidenceSet(
            reader.GetInt64(0), reader.GetInt64(1), versions, reader.GetString(3).Trim(), reader.GetString(4), reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.GetFieldValue<DateTimeOffset>(7), reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8), reader.GetBoolean(9));
    }

    public async ValueTask<bool> ReplaceAsync(NewEvidenceSet set, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(set);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await InsertAsync(connection, transaction, set, firstWrite: false, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (PostgresException failure) when (failure.SqlState == PostgresErrorCodes.CheckViolation && failure.MessageText.Contains("evidence set", StringComparison.Ordinal))
        {
            return false; // a shadow entry of the epoch exists: the set is frozen
        }
    }

    /// <summary>
    /// Inserts the set and registers its versions in the trial ledger (<c>trial_kind = evidence_set</c>, one row per stored version, idempotent on a derived run id). With <paramref name="firstWrite"/> the row is inserted only when the epoch has no set yet and carries
    /// <c>first_write_at</c>; false is returned when another set was stored first (nothing inserted, nothing registered). A refusal of the database (the epoch has shadow entries) is thrown.
    /// </summary>
    internal static async ValueTask<bool> InsertAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, NewEvidenceSet set, bool firstWrite, CancellationToken cancellationToken)
    {
        var versions = JsonSerializer.Serialize(set.Versions);
        await using (var insert = new NpgsqlCommand($"""
            INSERT INTO paper.evidence_sets (edge_epoch, versions, set_hash, mode, actor, reason, first_write_at)
            SELECT @key, @versions, @hash, @mode, @actor, @reason, {(firstWrite ? "now()" : "NULL")}
            {(firstWrite ? "WHERE NOT EXISTS (SELECT 1 FROM paper.evidence_sets WHERE edge_epoch = @key)" : string.Empty)}
            """, connection, transaction))
        {
            insert.Parameters.AddWithValue("key", NpgsqlDbType.Bigint, set.EdgeKey);
            insert.Parameters.AddWithValue("versions", NpgsqlDbType.Jsonb, versions);
            insert.Parameters.AddWithValue("hash", NpgsqlDbType.Char, set.SetHash);
            insert.Parameters.AddWithValue("mode", NpgsqlDbType.Varchar, set.Mode);
            insert.Parameters.AddWithValue("actor", NpgsqlDbType.Varchar, set.Actor);
            insert.Parameters.AddWithValue("reason", NpgsqlDbType.Varchar, (object?)set.Reason ?? DBNull.Value);
            if (await insert.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                return false;
            }
        }

        // The ledger rows: a registration, not a look (the variant counts leave the kind out). The version's family and number come from the stored version; a hash that is not stored is skipped (the handler refuses an unknown one for the owner, and the default reads stored hashes).
        await using var ledger = new NpgsqlCommand("""
            INSERT INTO lab.test_ledger (strategy_id, version, strategy_hash, parent_hash, lab_run_id, run_key, universe_label, window_from, window_to, method, created_at, trial_kind, extra_trials)
            SELECT v.strategy_id, v.version, v.hash, v.parent_hash, md5(@hash || v.hash::text)::uuid, encode(sha256(convert_to(@hash || v.hash::text, 'UTF8')), 'hex'),
                   'evidence_set', d.day, d.day, 'evidence_set', now(), 'evidence_set', 0
            FROM lab.strategy_versions v CROSS JOIN (SELECT (now() AT TIME ZONE 'Asia/Kolkata')::date AS day) d
            WHERE v.hash::text = ANY(@hashes)
            ON CONFLICT (lab_run_id) DO NOTHING
            """, connection, transaction);
        ledger.Parameters.AddWithValue("hash", NpgsqlDbType.Text, set.SetHash);
        ledger.Parameters.AddWithValue("hashes", NpgsqlDbType.Array | NpgsqlDbType.Text, set.Versions.ToArray());
        await ledger.ExecuteNonQueryAsync(cancellationToken);
        return true;
    }
}
