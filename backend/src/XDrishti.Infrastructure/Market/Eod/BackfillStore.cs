using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Market.History;

namespace XDrishti.Infrastructure.Market.Eod;

/// <summary>
/// <c>market.backfill_chunks</c> (the resumable ledger of the 1-minute backfill) and <c>market.backfill_seams</c> (the seam each instrument was planned against, the overlap probe's outcome and the continuity flag) by plain SQL, plus the three small reads the rest of the
/// system has of them: the seam for the simulation (<see cref="IBackfillSeams"/>) and the unfinished work for the AU-12 gate (<see cref="IBackfillProgress"/>). The seam itself is always the oldest stored bar of source <c>csv</c>, never the file log (CF-04 review fix 2).
/// </summary>
internal sealed class BackfillStore(NpgsqlDataSource dataSource, IBackfillSettings settings) : IBackfillStore, IBackfillSeams, IBackfillProgress
{
    public async ValueTask<DateTimeOffset?> CurrentSeamAsync(Guid instrumentId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT ts FROM market.bars_1m WHERE instrument_id = @id AND source = 'csv' ORDER BY ts LIMIT 1");
        command.Parameters.AddWithValue("id", instrumentId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? reader.GetFieldValue<DateTimeOffset>(0) : null;
    }

    public async ValueTask<IReadOnlyDictionary<Guid, SeamRecord>> ListSeamsAsync(IReadOnlyCollection<Guid> instrumentIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instrumentIds);
        var map = new Dictionary<Guid, SeamRecord>();
        if (instrumentIds.Count == 0)
        {
            return map;
        }

        await using var command = dataSource.CreateCommand("""
            SELECT instrument_id, seam_at, probe_status, probed_at, median_bps, within_max_pct, detail, seam_jump, jump_pct, probe_basis, continuity_basis
            FROM market.backfill_seams WHERE instrument_id = ANY(@ids)
            """);
        command.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = instrumentIds.ToArray() });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var record = new SeamRecord(
                reader.GetGuid(0),
                reader.GetFieldValue<DateTimeOffset>(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3),
                reader.IsDBNull(4) ? null : reader.GetDouble(4),
                reader.IsDBNull(5) ? null : reader.GetDouble(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetBoolean(7),
                reader.IsDBNull(8) ? null : reader.GetDouble(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10));
            map[record.InstrumentId] = record;
        }

        return map;
    }

    public async ValueTask SaveSeamAsync(SeamRecord seam, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(seam);
        await using var command = dataSource.CreateCommand("""
            INSERT INTO market.backfill_seams (instrument_id, seam_at, high_water_at, probe_status, probed_at, median_bps, within_max_pct, detail, seam_jump, jump_pct, probe_basis, continuity_basis, updated_at)
            VALUES (@id, @seam, @seam, @probe, @probed, @median, @within, @detail, @jump, @jump_pct, @probe_basis, @continuity_basis, now())
            ON CONFLICT (instrument_id) DO UPDATE SET
                seam_at = EXCLUDED.seam_at, high_water_at = GREATEST(market.backfill_seams.high_water_at, EXCLUDED.high_water_at), probe_status = EXCLUDED.probe_status, probed_at = EXCLUDED.probed_at, median_bps = EXCLUDED.median_bps,
                within_max_pct = EXCLUDED.within_max_pct, detail = EXCLUDED.detail, seam_jump = EXCLUDED.seam_jump, jump_pct = EXCLUDED.jump_pct,
                probe_basis = EXCLUDED.probe_basis, continuity_basis = EXCLUDED.continuity_basis, updated_at = EXCLUDED.updated_at
            """);
        command.Parameters.AddWithValue("id", seam.InstrumentId);
        command.Parameters.AddWithValue("seam", seam.SeamAt);
        command.Parameters.AddWithValue("probe", seam.ProbeStatus);
        command.Parameters.Add(new NpgsqlParameter("probed", NpgsqlDbType.TimestampTz) { Value = (object?)seam.ProbedAt ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("median", NpgsqlDbType.Double) { Value = (object?)seam.MedianBps ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("within", NpgsqlDbType.Double) { Value = (object?)seam.WithinMaxPct ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("detail", NpgsqlDbType.Varchar) { Value = (object?)seam.Detail ?? DBNull.Value });
        command.Parameters.AddWithValue("jump", seam.SeamJump);
        command.Parameters.Add(new NpgsqlParameter("jump_pct", NpgsqlDbType.Double) { Value = (object?)seam.JumpPct ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("probe_basis", NpgsqlDbType.Char) { Value = (object?)seam.ProbeBasis ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("continuity_basis", NpgsqlDbType.Char) { Value = (object?)seam.ContinuityBasis ?? DBNull.Value });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask<IReadOnlyList<ChunkRecord>> ListChunksAsync(Guid instrumentId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT from_date, to_date, status, row_count, attempts, error FROM market.backfill_chunks WHERE instrument_id = @id ORDER BY from_date DESC");
        command.Parameters.AddWithValue("id", instrumentId);
        var list = new List<ChunkRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new ChunkRecord(instrumentId, reader.GetFieldValue<DateOnly>(0), reader.GetFieldValue<DateOnly>(1), reader.GetString(2), reader.GetInt32(3), reader.GetInt32(4), reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return list;
    }

    // Missing chunks are inserted as planned; a chunk whose window changed (the seam moved) is reset to planned; a planned chunk the plan no longer has is removed. Finished and failed rows of the same window are left alone.
    public async ValueTask PlanChunksAsync(Guid instrumentId, IReadOnlyList<(DateOnly From, DateOnly To)> chunks, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var upsert = new NpgsqlCommand(
            """
            INSERT INTO market.backfill_chunks (instrument_id, from_date, to_date, status, row_count, attempts, error, updated_at)
            SELECT @id, t.from_date, t.to_date, 'planned', 0, 0, NULL, now() FROM unnest(@froms, @tos) AS t(from_date, to_date)
            ON CONFLICT (instrument_id, from_date) DO UPDATE SET to_date = EXCLUDED.to_date, status = 'planned', row_count = 0, attempts = 0, error = NULL, updated_at = now()
            WHERE market.backfill_chunks.to_date <> EXCLUDED.to_date
            """,
            connection,
            transaction))
        {
            upsert.Parameters.AddWithValue("id", instrumentId);
            upsert.Parameters.Add(new NpgsqlParameter("froms", NpgsqlDbType.Array | NpgsqlDbType.Date) { Value = chunks.Select(c => c.From).ToArray() });
            upsert.Parameters.Add(new NpgsqlParameter("tos", NpgsqlDbType.Array | NpgsqlDbType.Date) { Value = chunks.Select(c => c.To).ToArray() });
            await upsert.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var remove = new NpgsqlCommand("DELETE FROM market.backfill_chunks WHERE instrument_id = @id AND status = 'planned' AND from_date <> ALL(@froms)", connection, transaction))
        {
            remove.Parameters.AddWithValue("id", instrumentId);
            remove.Parameters.Add(new NpgsqlParameter("froms", NpgsqlDbType.Array | NpgsqlDbType.Date) { Value = chunks.Select(c => c.From).ToArray() });
            await remove.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async ValueTask SaveChunkAsync(ChunkRecord chunk, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        await using var command = dataSource.CreateCommand("""
            UPDATE market.backfill_chunks SET status = @status, row_count = @rows, attempts = @attempts, error = @error, updated_at = now()
            WHERE instrument_id = @id AND from_date = @from
            """);
        command.Parameters.AddWithValue("id", chunk.InstrumentId);
        command.Parameters.AddWithValue("from", chunk.From);
        command.Parameters.AddWithValue("status", chunk.Status);
        command.Parameters.AddWithValue("rows", chunk.Rows);
        command.Parameters.AddWithValue("attempts", chunk.Attempts);
        command.Parameters.Add(new NpgsqlParameter("error", NpgsqlDbType.Varchar) { Value = (object?)chunk.Error ?? DBNull.Value });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask<IReadOnlyList<ChunkCounts>> CountChunksAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT instrument_id, count(*)::int, (count(*) FILTER (WHERE status IN ('done', 'empty')))::int, (count(*) FILTER (WHERE status = 'failed'))::int
            FROM market.backfill_chunks GROUP BY instrument_id
            """);
        var list = new List<ChunkCounts>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new ChunkCounts(reader.GetGuid(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3)));
        }

        return list;
    }

    public async ValueTask<DateTimeOffset?> SeamOfAsync(Guid instrumentId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT high_water_at FROM market.backfill_seams WHERE instrument_id = @id");
        command.Parameters.AddWithValue("id", instrumentId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? reader.GetFieldValue<DateTimeOffset>(0) : null;
    }

    public bool AdmitVerified => settings.AdmitVerified;

    // CF-04d: the stored facts of the admission rule, read whatever the switch says; PreSeamAdmission decides whether to ask (a run with the switch off asks for nothing).
    public async ValueTask<SeamAdmissionFacts?> AdmissionFactsAsync(Guid instrumentId, CancellationToken cancellationToken)
    {
        var record = (await ListSeamsAsync([instrumentId], cancellationToken)).GetValueOrDefault(instrumentId);
        if (record is null)
        {
            return new SeamAdmissionFacts(true, null, null, null, false);
        }

        var highWater = await SeamOfAsync(instrumentId, cancellationToken);
        var current = await CurrentSeamAsync(instrumentId, cancellationToken);
        await using var command = dataSource.CreateCommand("SELECT EXISTS (SELECT 1 FROM market.backfill_chunks WHERE instrument_id = @id AND status IN ('planned', 'failed'))");
        command.Parameters.AddWithValue("id", instrumentId);
        var unfinished = (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? true);
        return new SeamAdmissionFacts(true, record, current, highWater, unfinished);
    }

    public async ValueTask<bool> HasDhanBarsAsync(Guid instrumentId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT EXISTS (SELECT 1 FROM market.bars_1m WHERE instrument_id = @id AND source = 'dhan' AND ts >= @from AND ts < @to)");
        command.Parameters.AddWithValue("id", instrumentId);
        command.Parameters.AddWithValue("from", from.ToUniversalTime());   // Npgsql writes a DateTimeOffset to timestamptz only with offset 0
        command.Parameters.AddWithValue("to", to.ToUniversalTime());
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? true);
    }

    public async ValueTask<int> UnfinishedInstrumentsAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT count(DISTINCT c.instrument_id)::int FROM market.backfill_chunks c
            LEFT JOIN market.backfill_seams s ON s.instrument_id = c.instrument_id
            WHERE c.status IN ('planned', 'failed') AND COALESCE(s.probe_status, 'none') <> 'unresolved' AND NOT COALESCE(s.seam_jump, false)
            """);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }
}
