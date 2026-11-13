using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Shadow;
using XDrishti.Domain.Shadow;

namespace XDrishti.Infrastructure.Paper;

/// <summary>
/// The shadow record behind <see cref="IShadowStore"/> (spec CF-08, ADR 0211): raw SQL, select and insert only (the runtime role has no more). A session and its rows are stored in one transaction, so a session never exists without its rows; the database stamps
/// <c>written_at</c> and refuses a session that has opened, a row stamped after the open and a chain that does not link. A session stored already is left alone.
/// </summary>
internal sealed class ShadowStore(NpgsqlDataSource dataSource) : IShadowStore
{
    private const string EntryColumns = "session_date, plan_id, plan_item_id, trade_key, ticket_group, cell_key, strategy_hash, symbol, side, entry_limit, stop, target, time_rule, config_hash, model_id, p_win, feature_version, features::text, counts_for_edge, edge_epoch, row_hash";

    public async ValueTask<ShadowHead?> HeadAsync(CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("SELECT session_date, digest FROM paper.shadow_sessions ORDER BY session_date DESC LIMIT 1");
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? new ShadowHead(DateOnly.FromDateTime(reader.GetDateTime(0)), reader.GetString(1)) : null;
    }

    public async ValueTask<bool> WriteSessionAsync(NewShadowSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            if (session.EvidenceSet is not null || session.Entries.Count > 0)
            {
                // Taken first, before any other statement, whenever the session carries a set or entries (so a session with a set and no entries cannot replace an uncommitted owner set, and the lock order is the same as the guard's). Serialised against the owner's replace of the epoch's evidence set (the trigger of paper.evidence_sets takes the same lock): either the owner's set commits first and is seen below, or these entries commit first and the replace is refused. Held to the end of the transaction.
                await using var gate = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended('paper.evidence_sets', @key))", connection, transaction);
                gate.Parameters.AddWithValue("key", NpgsqlDbType.Bigint, session.EvidenceSet?.EdgeKey ?? session.Entries[0].EdgeEpoch ?? 0L);
                await gate.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var head = new NpgsqlCommand("INSERT INTO paper.shadow_sessions (session_date, n_rows, status, digest, prev_digest) VALUES (@date, @n, @status, @digest, @prev)", connection, transaction))
            {
                head.Parameters.AddWithValue("date", NpgsqlDbType.Date, session.SessionDate);
                head.Parameters.AddWithValue("n", NpgsqlDbType.Integer, session.Entries.Count);
                head.Parameters.AddWithValue("status", NpgsqlDbType.Varchar, session.Status);
                head.Parameters.AddWithValue("digest", NpgsqlDbType.Char, session.Digest);
                head.Parameters.AddWithValue("prev", NpgsqlDbType.Char, (object?)session.PrevDigest ?? DBNull.Value);
                await head.ExecuteNonQueryAsync(cancellationToken);
            }

            // The epoch's evidence set goes in first, in this transaction, so a first write never exists without its set (spec AM-01 GAP-09). A set stored meanwhile (the owner's) is left as it is.
            if (session.EvidenceSet is { } evidenceSet)
            {
                await EvidenceSetStore.InsertAsync(connection, transaction, evidenceSet, firstWrite: true, cancellationToken);
            }

            foreach (var e in session.Entries)
            {
                await using var insert = new NpgsqlCommand($"""
                    INSERT INTO paper.shadow_entries ({EntryColumns.Replace("features::text", "features", StringComparison.Ordinal)})
                    VALUES (@date, @plan, @item, @key, @group, @cell, @hash, @symbol, @side, @limit, @stop, @target, @rule, @config, @model, @pwin, @version, @features, @edge, @epoch, @row)
                    """, connection, transaction);
                var p = insert.Parameters;
                p.AddWithValue("date", NpgsqlDbType.Date, e.SessionDate);
                p.AddWithValue("plan", NpgsqlDbType.Bigint, e.PlanId);
                p.AddWithValue("item", NpgsqlDbType.Bigint, e.PlanItemId);
                p.AddWithValue("key", NpgsqlDbType.Varchar, e.TradeKey);
                p.AddWithValue("group", NpgsqlDbType.Varchar, e.TicketGroup);
                p.AddWithValue("cell", NpgsqlDbType.Varchar, e.CellKey);
                p.AddWithValue("hash", NpgsqlDbType.Char, e.StrategyHash);
                p.AddWithValue("symbol", NpgsqlDbType.Varchar, e.Symbol);
                p.AddWithValue("side", NpgsqlDbType.Varchar, e.Side);
                p.AddWithValue("limit", NpgsqlDbType.Numeric, (object?)e.EntryLimit ?? DBNull.Value);
                p.AddWithValue("stop", NpgsqlDbType.Numeric, (object?)e.Stop ?? DBNull.Value);
                p.AddWithValue("target", NpgsqlDbType.Numeric, (object?)e.Target ?? DBNull.Value);
                p.AddWithValue("rule", NpgsqlDbType.Varchar, e.TimeRule);
                p.AddWithValue("config", NpgsqlDbType.Char, (object?)e.ConfigHash ?? DBNull.Value);
                p.AddWithValue("model", NpgsqlDbType.Varchar, (object?)e.ModelId ?? DBNull.Value);
                p.AddWithValue("pwin", NpgsqlDbType.Double, (object?)e.PWin ?? DBNull.Value);
                p.AddWithValue("version", NpgsqlDbType.Varchar, (object?)e.FeatureVersion ?? DBNull.Value);
                p.AddWithValue("features", NpgsqlDbType.Jsonb, e.Features is null ? DBNull.Value : JsonSerializer.Serialize(new SortedDictionary<string, double>(new Dictionary<string, double>(e.Features), StringComparer.Ordinal)));
                p.AddWithValue("edge", NpgsqlDbType.Boolean, e.CountsForEdge);
                p.AddWithValue("epoch", NpgsqlDbType.Bigint, (object?)e.EdgeEpoch ?? DBNull.Value);
                p.AddWithValue("row", NpgsqlDbType.Char, e.RowHash);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            if (session.Scores is { Count: > 0 } scores)
            {
                // The scores are extra evidence and must never cost the session: they sit behind a savepoint, and a refusal (the 09:00 guard, a check, a missing model) rolls back to it so that the session is committed without them.
                await transaction.SaveAsync("scores", cancellationToken);
                try
                {
                    foreach (var score in scores)
                    {
                        // the entry is found by its key in this session, inside the transaction; the database stamps the time and refuses a score at or after 09:00 India time of the session
                        await using var insert = new NpgsqlCommand("""
                            INSERT INTO paper.shadow_scores (entry_id, model_id, model_sha256, feature_version, p_win, features_hash)
                            SELECT id, @model, @sha, @version, @pwin, @features FROM paper.shadow_entries WHERE session_date = @date AND trade_key = @key
                            """, connection, transaction);
                        var p = insert.Parameters;
                        p.AddWithValue("model", NpgsqlDbType.Bigint, score.ModelId);
                        p.AddWithValue("sha", NpgsqlDbType.Char, score.ModelSha256);
                        p.AddWithValue("version", NpgsqlDbType.Varchar, score.FeatureVersion);
                        p.AddWithValue("pwin", NpgsqlDbType.Double, score.PWin);
                        p.AddWithValue("features", NpgsqlDbType.Char, score.FeaturesHash);
                        p.AddWithValue("date", NpgsqlDbType.Date, session.SessionDate);
                        p.AddWithValue("key", NpgsqlDbType.Varchar, score.TradeKey);
                        await insert.ExecuteNonQueryAsync(cancellationToken);
                    }
                }
                catch (PostgresException)
                {
                    await transaction.RollbackAsync("scores", cancellationToken);
                }
            }

            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (PostgresException failure) when (failure.SqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.CheckViolation)
        {
            // Of two writers racing for one date the second is refused (the date is stored or the chain moved on): it stored nothing, which is the idempotent answer. Any other refusal is the database's guard and is thrown.
            await using var exists = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM paper.shadow_sessions WHERE session_date = @date)", connection);
            exists.Parameters.AddWithValue("date", NpgsqlDbType.Date, session.SessionDate);
            if ((bool)(await exists.ExecuteScalarAsync(cancellationToken))!)
            {
                return false;
            }

            throw;
        }
    }

    public async ValueTask<IReadOnlyList<ShadowChainSession>> ReadChainAsync(CancellationToken cancellationToken)
    {
        var entries = new Dictionary<DateOnly, List<StoredShadowEntry>>();
        await using (var select = dataSource.CreateCommand($"SELECT {EntryColumns} FROM paper.shadow_entries ORDER BY session_date, id"))
        await using (var reader = await select.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var date = DateOnly.FromDateTime(reader.GetDateTime(0));
                var entry = new ShadowEntry(
                    date, reader.GetInt64(1), reader.GetInt64(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetString(8),
                    Decimal(reader, 9), Decimal(reader, 10), Decimal(reader, 11), reader.GetString(12), reader.IsDBNull(13) ? null : reader.GetString(13), reader.IsDBNull(14) ? null : reader.GetString(14),
                    reader.IsDBNull(15) ? null : reader.GetDouble(15), reader.IsDBNull(16) ? null : reader.GetString(16), reader.IsDBNull(17) ? null : JsonSerializer.Deserialize<Dictionary<string, double>>(reader.GetString(17)),
                    reader.GetBoolean(18), reader.IsDBNull(19) ? null : reader.GetInt64(19));
                if (!entries.TryGetValue(date, out var list))
                {
                    entries[date] = list = [];
                }

                list.Add(new StoredShadowEntry(entry, reader.GetString(20)));
            }
        }

        var result = new List<ShadowChainSession>();
        await using var sessions = dataSource.CreateCommand("SELECT session_date, status, prev_digest, digest FROM paper.shadow_sessions ORDER BY session_date");
        await using var rows = await sessions.ExecuteReaderAsync(cancellationToken);
        while (await rows.ReadAsync(cancellationToken))
        {
            var date = DateOnly.FromDateTime(rows.GetDateTime(0));
            result.Add(new ShadowChainSession(date, rows.GetString(1), rows.IsDBNull(2) ? null : rows.GetString(2), rows.GetString(3), entries.GetValueOrDefault(date) ?? []));
        }

        return result;
    }

    private static decimal? Decimal(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);
}
