using System.Data.Common;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Learning;

namespace XDrishti.Infrastructure.Learning;

/// <summary><c>research.hypotheses</c> behind <see cref="IHypothesisLog"/> (spec P6-10, ADR 0076): raw SQL, insert and select only (the runtime role has no more).</summary>
internal sealed class HypothesisLogStore(NpgsqlDataSource dataSource) : IHypothesisLog
{
    // One constant key for proposals: the open count is global, so proposals are serialised.
    private const long ProposeLockKey = 7_864_011_080;

    private const string Columns = "id, hypothesis_id, state, from_state, source, cell_key, knob_name, knob_kind, baseline_value, candidate_value, rationale, baseline_run_key, candidate_run_key, experiment_id, reason, actor, created_at";

    private const string Open = "('proposed', 'approved', 'running', 'judged')";

    // The newest row of each hypothesis.
    private const string Latest = $"""
        SELECT DISTINCT ON (COALESCE(hypothesis_id, id)) {Columns} FROM research.hypotheses ORDER BY COALESCE(hypothesis_id, id), id DESC
        """;

    public async ValueTask<HypothesisAppend> ProposeAsync(NewHypothesisRow row, int maxOpen, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(row);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var advisory = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@k)", connection, transaction))
        {
            advisory.Parameters.AddWithValue("k", NpgsqlDbType.Bigint, ProposeLockKey);
            await advisory.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var count = new NpgsqlCommand($"SELECT COUNT(*)::int FROM ({Latest}) l WHERE state IN {Open}", connection, transaction))
        {
            if ((int)(await count.ExecuteScalarAsync(cancellationToken))! >= maxOpen)
            {
                return new HypothesisAppend(0, HypothesisAppendStatus.OpenLimit);
            }
        }

        var id = await InsertAsync(connection, transaction, row, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new HypothesisAppend(id, HypothesisAppendStatus.Created);
    }

    public async ValueTask<HypothesisAppend> AppendAsync(NewHypothesisRow row, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(row);
        var hypothesisId = row.HypothesisId ?? throw new ArgumentException("A state change names its hypothesis.", nameof(row));
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var advisory = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtext(@k))", connection, transaction))
        {
            advisory.Parameters.AddWithValue("k", NpgsqlDbType.Text, "research.hypotheses:" + hypothesisId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await advisory.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var latest = new NpgsqlCommand("SELECT state FROM research.hypotheses WHERE (id = @id AND hypothesis_id IS NULL) OR hypothesis_id = @id ORDER BY id DESC LIMIT 1", connection, transaction))
        {
            latest.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, hypothesisId);
            if (await latest.ExecuteScalarAsync(cancellationToken) is not string state || state != row.FromState)
            {
                return new HypothesisAppend(0, HypothesisAppendStatus.Stale);
            }
        }

        var id = await InsertAsync(connection, transaction, row, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new HypothesisAppend(id, HypothesisAppendStatus.Created);
    }

    public async ValueTask<HypothesisRow?> FindAsync(long hypothesisId, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"SELECT {Columns} FROM research.hypotheses WHERE (id = @id AND hypothesis_id IS NULL) OR hypothesis_id = @id ORDER BY id DESC LIMIT 1");
        select.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, hypothesisId);
        var rows = await ReadAllAsync(select, cancellationToken);
        return rows.Count == 0 ? null : rows[0];
    }

    public async ValueTask<IReadOnlyList<HypothesisRow>> HistoryAsync(long hypothesisId, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"SELECT {Columns} FROM research.hypotheses WHERE (id = @id AND hypothesis_id IS NULL) OR hypothesis_id = @id ORDER BY id");
        select.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, hypothesisId);
        return await ReadAllAsync(select, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<HypothesisRow>> ListAsync(string? state, int limit, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"SELECT {Columns} FROM ({Latest}) l WHERE (@state::text IS NULL OR state = @state::text) ORDER BY COALESCE(hypothesis_id, id) DESC LIMIT @limit");
        select.Parameters.Add(new NpgsqlParameter("state", NpgsqlDbType.Text) { Value = (object?)state ?? DBNull.Value });
        select.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit);
        return await ReadAllAsync(select, cancellationToken);
    }

    public async ValueTask<int> CountOpenAsync(CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"SELECT COUNT(*)::int FROM ({Latest}) l WHERE state IN {Open}");
        return (int)(await select.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<long> InsertAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, NewHypothesisRow row, CancellationToken cancellationToken)
    {
        await using var insert = new NpgsqlCommand("""
            INSERT INTO research.hypotheses (hypothesis_id, state, from_state, source, cell_key, knob_name, knob_kind, baseline_value, candidate_value, rationale, baseline_run_key, candidate_run_key, experiment_id, reason, actor, created_at)
            VALUES (@hid, @state, @from, @source, @cell, @knob, @kind, @bvalue, @cvalue, @rationale, @brun, @crun, @experiment, @reason, @actor, @at)
            RETURNING id
            """, connection, transaction);
        var p = insert.Parameters;
        p.Add(new NpgsqlParameter("hid", NpgsqlDbType.Bigint) { Value = row.HypothesisId is { } h ? h : DBNull.Value });
        p.AddWithValue("state", NpgsqlDbType.Varchar, row.State);
        p.Add(new NpgsqlParameter("from", NpgsqlDbType.Varchar) { Value = (object?)row.FromState ?? DBNull.Value });
        p.AddWithValue("source", NpgsqlDbType.Varchar, row.Source);
        p.Add(new NpgsqlParameter("cell", NpgsqlDbType.Varchar) { Value = (object?)row.CellKey ?? DBNull.Value });
        p.AddWithValue("knob", NpgsqlDbType.Varchar, row.KnobName);
        p.AddWithValue("kind", NpgsqlDbType.Varchar, row.KnobKind);
        p.AddWithValue("bvalue", NpgsqlDbType.Varchar, row.BaselineValue);
        p.AddWithValue("cvalue", NpgsqlDbType.Varchar, row.CandidateValue);
        p.AddWithValue("rationale", NpgsqlDbType.Varchar, row.Rationale);
        p.AddWithValue("brun", NpgsqlDbType.Char, row.BaselineRunKey);
        p.Add(new NpgsqlParameter("crun", NpgsqlDbType.Char) { Value = (object?)row.CandidateRunKey ?? DBNull.Value });
        p.Add(new NpgsqlParameter("experiment", NpgsqlDbType.Bigint) { Value = row.ExperimentId is { } e ? e : DBNull.Value });
        p.AddWithValue("reason", NpgsqlDbType.Varchar, row.Reason);
        p.AddWithValue("actor", NpgsqlDbType.Varchar, row.Actor);
        p.AddWithValue("at", NpgsqlDbType.TimestampTz, row.CreatedAt);
        return (long)(await insert.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<IReadOnlyList<HypothesisRow>> ReadAllAsync(NpgsqlCommand select, CancellationToken cancellationToken)
    {
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        var rows = new List<HypothesisRow>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Read(reader));
        }

        return rows;
    }

    private static HypothesisRow Read(DbDataReader r) => new(
        r.GetInt64(0),
        new NewHypothesisRow(
            r.IsDBNull(1) ? null : r.GetInt64(1),
            r.GetString(2),
            r.IsDBNull(3) ? null : r.GetString(3),
            r.GetString(4),
            r.IsDBNull(5) ? null : r.GetString(5),
            r.GetString(6),
            r.GetString(7),
            r.GetString(8),
            r.GetString(9),
            r.GetString(10),
            r.GetString(11),
            r.IsDBNull(12) ? null : r.GetString(12),
            r.IsDBNull(13) ? null : r.GetInt64(13),
            r.GetString(14),
            r.GetString(15),
            r.GetFieldValue<DateTimeOffset>(16)));
}
