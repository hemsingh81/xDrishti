using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Domain.Market;

namespace XDrishti.Infrastructure.Market;

/// <summary>
/// Writes the master list: the candidates are copied (binary COPY) into a temporary table and merged into <c>market.instruments</c> in one
/// transaction, so a failure leaves the previous list untouched.
/// </summary>
internal sealed class InstrumentCatalog(NpgsqlDataSource dataSource) : IInstrumentCatalog
{
    private const string BrokerName = "dhan";

    private const string CreateStage =
        """
        CREATE TEMP TABLE stage_instruments (
            new_id uuid NOT NULL,
            canonical_symbol text NOT NULL,
            exchange_segment text NOT NULL,
            type text NOT NULL,
            name text NOT NULL,
            display_name text NOT NULL,
            isin text NULL,
            series text NULL,
            lot_size integer NOT NULL,
            tick_size numeric NULL,
            expiry_date date NULL,
            underlying_symbol text NOT NULL,
            sector text NULL,
            sector_source text NULL,
            fno_eligible boolean NOT NULL,
            asm_gsm_flag text NULL,
            asm_gsm_category text NULL,
            mtf_leverage numeric NULL,
            freeze_qty bigint NULL,
            broker_id text NOT NULL
        ) ON COMMIT DROP
        """;

    private const string CopyStage =
        """
        COPY stage_instruments (new_id, canonical_symbol, exchange_segment, type, name, display_name, isin, series, lot_size, tick_size, expiry_date,
            underlying_symbol, sector, sector_source, fno_eligible, asm_gsm_flag, asm_gsm_category, mtf_leverage, freeze_qty, broker_id)
        FROM STDIN (FORMAT BINARY)
        """;

    // What differs between the stored row (i) and the file (s). Sectors are compared only when the sector list was read.
    private const string Differs =
        """
        (i.exchange_segment, i.type, i.name, i.display_name, i.isin, i.series, i.lot_size, i.tick_size, i.expiry_date, i.underlying_symbol,
         i.sector, i.sector_source, i.fno_eligible, i.asm_gsm_flag, i.asm_gsm_category, i.mtf_leverage, i.freeze_qty)
        IS DISTINCT FROM
        (s.exchange_segment, s.type, s.name, s.display_name, s.isin, s.series, s.lot_size, s.tick_size, s.expiry_date, s.underlying_symbol,
         CASE WHEN @sectors THEN s.sector ELSE i.sector END, CASE WHEN @sectors THEN s.sector_source ELSE i.sector_source END,
         s.fno_eligible, s.asm_gsm_flag, s.asm_gsm_category, s.mtf_leverage, s.freeze_qty)
        """;

    private const string CountChanges =
        $"""
        SELECT
            count(*) FILTER (WHERE i.id IS NULL),
            count(*) FILTER (WHERE i.id IS NOT NULL AND i.retired_at IS NOT NULL),
            count(*) FILTER (WHERE i.id IS NOT NULL AND i.retired_at IS NULL AND ({Differs})),
            count(*) FILTER (WHERE i.id IS NOT NULL AND i.retired_at IS NULL AND NOT ({Differs}))
        FROM stage_instruments s
        LEFT JOIN market.instruments i ON i.canonical_symbol = s.canonical_symbol
        """;

    private const string InsertNew =
        """
        INSERT INTO market.instruments
            (id, canonical_symbol, exchange, exchange_segment, type, name, display_name, isin, series, lot_size, tick_size, expiry_date,
             underlying_symbol, sector, sector_source, fno_eligible, asm_gsm_flag, asm_gsm_category, mtf_leverage, freeze_qty,
             first_seen_at, last_seen_at, retired_at)
        SELECT s.new_id, s.canonical_symbol, 'NSE', s.exchange_segment, s.type, s.name, s.display_name, s.isin, s.series, s.lot_size, s.tick_size,
               s.expiry_date, s.underlying_symbol, s.sector, s.sector_source, s.fno_eligible, s.asm_gsm_flag, s.asm_gsm_category, s.mtf_leverage,
               s.freeze_qty, @now, @now, NULL
        FROM stage_instruments s
        WHERE NOT EXISTS (SELECT 1 FROM market.instruments i WHERE i.canonical_symbol = s.canonical_symbol)
        """;

    // Every instrument in the file is rewritten (4,400 rows, once a day) so that last_seen_at and a revival are one simple statement; the counts above
    // (not this statement) say what really changed.
    private const string UpdateExisting =
        """
        UPDATE market.instruments i SET
            exchange_segment = s.exchange_segment, type = s.type, name = s.name, display_name = s.display_name, isin = s.isin, series = s.series,
            lot_size = s.lot_size, tick_size = s.tick_size, expiry_date = s.expiry_date, underlying_symbol = s.underlying_symbol,
            sector = CASE WHEN @sectors THEN s.sector ELSE i.sector END,
            sector_source = CASE WHEN @sectors THEN s.sector_source ELSE i.sector_source END,
            fno_eligible = s.fno_eligible, asm_gsm_flag = s.asm_gsm_flag, asm_gsm_category = s.asm_gsm_category,
            mtf_leverage = s.mtf_leverage, freeze_qty = s.freeze_qty, last_seen_at = @now, retired_at = NULL
        FROM stage_instruments s
        WHERE i.canonical_symbol = s.canonical_symbol
        """;

    private const string CreateProtected =
        """
        CREATE TEMP TABLE stage_protected (segment text NOT NULL, broker_id text NOT NULL) ON COMMIT DROP
        """;

    // An instrument whose row in the file was skipped (unreadable) is not retired: its absence from the candidates proves nothing.
    private const string Retire =
        """
        UPDATE market.instruments i SET retired_at = @now
        WHERE i.retired_at IS NULL
          AND NOT EXISTS (SELECT 1 FROM stage_instruments s WHERE s.canonical_symbol = i.canonical_symbol)
          AND NOT EXISTS (
              SELECT 1 FROM market.instrument_broker_ids b
              JOIN stage_protected p ON p.segment = b.segment AND p.broker_id = b.broker_id
              WHERE b.instrument_id = i.id AND b.broker = @broker)
        """;

    private const string DropChangedBrokerIds =
        """
        DELETE FROM market.instrument_broker_ids b
        USING market.instruments i, stage_instruments s
        WHERE b.instrument_id = i.id AND i.canonical_symbol = s.canonical_symbol AND b.broker = @broker
          AND (b.broker_id <> s.broker_id OR b.segment <> s.exchange_segment)
        """;

    private const string UpsertBrokerIds =
        """
        INSERT INTO market.instrument_broker_ids (instrument_id, broker, broker_id, segment)
        SELECT i.id, @broker, s.broker_id, s.exchange_segment
        FROM stage_instruments s
        JOIN market.instruments i ON i.canonical_symbol = s.canonical_symbol
        ON CONFLICT (broker, broker_id, segment) DO UPDATE SET instrument_id = EXCLUDED.instrument_id
        """;

    public async ValueTask<CatalogApplied> ApplyAsync(
        IReadOnlyList<InstrumentCandidate> candidates,
        IReadOnlyList<BrokerIdentity> protectedIds,
        bool sectorsAvailable,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(protectedIds);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await Run(connection, transaction, CreateStage, cancellationToken: cancellationToken);
        await CopyAsync(connection, candidates, now, cancellationToken);
        await Run(connection, transaction, CreateProtected, cancellationToken: cancellationToken);
        await CopyProtectedAsync(connection, protectedIds, cancellationToken);

        int added, revived, updated, unchanged;
        await using (var count = Command(connection, transaction, CountChanges, sectorsAvailable, now))
        await using (var reader = await count.ExecuteReaderAsync(cancellationToken))
        {
            await reader.ReadAsync(cancellationToken);
            added = (int)reader.GetInt64(0);
            revived = (int)reader.GetInt64(1);
            updated = (int)reader.GetInt64(2);
            unchanged = (int)reader.GetInt64(3);
        }

        await Run(connection, transaction, InsertNew, sectorsAvailable, now, cancellationToken);
        await Run(connection, transaction, UpdateExisting, sectorsAvailable, now, cancellationToken);
        var retired = await Run(connection, transaction, Retire, sectorsAvailable, now, cancellationToken);
        await Run(connection, transaction, DropChangedBrokerIds, sectorsAvailable, now, cancellationToken);
        await Run(connection, transaction, UpsertBrokerIds, sectorsAvailable, now, cancellationToken);

        int total;
        await using (var active = Command(connection, transaction, "SELECT count(*) FROM market.instruments WHERE retired_at IS NULL", sectorsAvailable, now))
        {
            total = (int)(long)(await active.ExecuteScalarAsync(cancellationToken))!;
        }

        await transaction.CommitAsync(cancellationToken);
        return new CatalogApplied(added, updated, retired, revived, unchanged, total);
    }

    public async ValueTask<int> CountActiveAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT count(*) FROM market.instruments WHERE retired_at IS NULL");
        return (int)(long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task CopyAsync(NpgsqlConnection connection, IReadOnlyList<InstrumentCandidate> candidates, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var importer = await connection.BeginBinaryImportAsync(CopyStage, cancellationToken);
        foreach (var c in candidates)
        {
            await importer.StartRowAsync(cancellationToken);
            await importer.WriteAsync(Guid.CreateVersion7(now), NpgsqlDbType.Uuid, cancellationToken);
            await importer.WriteAsync(c.CanonicalSymbol, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(c.ExchangeSegment, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(c.Type.ToCode(), NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(c.Name, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(c.DisplayName, NpgsqlDbType.Text, cancellationToken);
            await WriteOptionalAsync(importer, c.Isin, NpgsqlDbType.Text, cancellationToken);
            await WriteOptionalAsync(importer, c.Series, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(c.LotSize, NpgsqlDbType.Integer, cancellationToken);
            await WriteOptionalAsync(importer, c.TickSize, NpgsqlDbType.Numeric, cancellationToken);
            await WriteOptionalAsync(importer, c.ExpiryDate, NpgsqlDbType.Date, cancellationToken);
            await importer.WriteAsync(c.UnderlyingSymbol, NpgsqlDbType.Text, cancellationToken);
            await WriteOptionalAsync(importer, c.Sector, NpgsqlDbType.Text, cancellationToken);
            await WriteOptionalAsync(importer, c.SectorSource, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(c.FnoEligible, NpgsqlDbType.Boolean, cancellationToken);
            await WriteOptionalAsync(importer, c.AsmGsmFlag, NpgsqlDbType.Text, cancellationToken);
            await WriteOptionalAsync(importer, c.AsmGsmCategory, NpgsqlDbType.Text, cancellationToken);
            await WriteOptionalAsync(importer, c.MtfLeverage, NpgsqlDbType.Numeric, cancellationToken);
            await WriteOptionalAsync(importer, c.FreezeQuantity, NpgsqlDbType.Bigint, cancellationToken);
            await importer.WriteAsync(c.BrokerId, NpgsqlDbType.Text, cancellationToken);
        }

        await importer.CompleteAsync(cancellationToken);
    }

    private static async Task CopyProtectedAsync(NpgsqlConnection connection, IReadOnlyList<BrokerIdentity> protectedIds, CancellationToken cancellationToken)
    {
        await using var importer = await connection.BeginBinaryImportAsync("COPY stage_protected (segment, broker_id) FROM STDIN (FORMAT BINARY)", cancellationToken);
        foreach (var id in protectedIds)
        {
            await importer.StartRowAsync(cancellationToken);
            await importer.WriteAsync(id.Segment, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(id.BrokerId, NpgsqlDbType.Text, cancellationToken);
        }

        await importer.CompleteAsync(cancellationToken);
    }

    private static async ValueTask WriteOptionalAsync(NpgsqlBinaryImporter importer, string? value, NpgsqlDbType type, CancellationToken cancellationToken)
    {
        if (value is null)
        {
            await importer.WriteNullAsync(cancellationToken);
        }
        else
        {
            await importer.WriteAsync(value, type, cancellationToken);
        }
    }

    private static async ValueTask WriteOptionalAsync<T>(NpgsqlBinaryImporter importer, T? value, NpgsqlDbType type, CancellationToken cancellationToken)
        where T : struct
    {
        if (value is null)
        {
            await importer.WriteNullAsync(cancellationToken);
        }
        else
        {
            await importer.WriteAsync(value.Value, type, cancellationToken);
        }
    }

    private static async Task<int> Run(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, bool sectors = false, DateTimeOffset now = default, CancellationToken cancellationToken = default)
    {
        await using var command = Command(connection, transaction, sql, sectors, now);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static NpgsqlCommand Command(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, bool sectors, DateTimeOffset now)
    {
        var command = new NpgsqlCommand(sql, connection, transaction);
        if (sql.Contains("@sectors", StringComparison.Ordinal))
        {
            command.Parameters.AddWithValue("sectors", sectors);
        }

        if (sql.Contains("@now", StringComparison.Ordinal))
        {
            command.Parameters.AddWithValue("now", now);
        }

        if (sql.Contains("@broker", StringComparison.Ordinal))
        {
            command.Parameters.AddWithValue("broker", BrokerName);
        }

        return command;
    }
}
