using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Options;
using XDrishti.Domain.Options;

namespace XDrishti.Infrastructure.OptionContracts;

/// <summary>
/// Writes <c>options.contracts</c> and <c>options.lot_sizes</c> (spec P14-01, ADR 0175) in one transaction: the contracts are copied (binary COPY) into a temporary
/// table; a (security id, expiry) not stored yet is inserted; a stored one whose underlying, strike and side are the same has its <c>last_seen_on</c> advanced (the
/// one column the runtime role may update); a stored one that differs is a reused id, counted and left alone. Nothing is ever deleted, so an expired contract keeps
/// its row. Seeds are written when missing; a <c>scrip_master</c> lot row only when the observed lot differs from the newest such row.
/// </summary>
internal sealed class OptionContractStore(NpgsqlDataSource dataSource) : IOptionContractStore
{
    private const string CreateStage =
        """
        CREATE TEMP TABLE stage_option_contracts (
            security_id bigint NOT NULL,
            underlying_id uuid NOT NULL,
            instrument text NOT NULL,
            expiry date NOT NULL,
            strike_px integer NOT NULL,
            side text NOT NULL,
            expiry_kind text NULL,
            lot_size_seen integer NOT NULL,
            tick_px integer NULL
        ) ON COMMIT DROP
        """;

    // One row per (security id, expiry) of the file. Dhan's file has none twice (2026-11-01); if it ever does, the row kept is chosen deterministically (lowest
    // underlying, strike, side, lot) and every dropped duplicate is counted conflicting (CountDuplicates).
    private const string Dedupe =
        """
        CREATE TEMP TABLE stage_option_keys ON COMMIT DROP AS
        SELECT DISTINCT ON (security_id, expiry) * FROM stage_option_contracts ORDER BY security_id, expiry, underlying_id, strike_px, side, lot_size_seen
        """;

    private const string CountDuplicates =
        """
        SELECT (SELECT count(*) FROM stage_option_contracts) - (SELECT count(*) FROM stage_option_keys)
        """;

    // A reuse is a file row whose very contract is not stored yet while its security id already names another contract (another underlying, expiry, strike or
    // side). A row that is already stored as itself (for example the new-expiry row a reuse once added) is not counted again: counted before anything is written.
    private const string CountReused =
        """
        SELECT count(DISTINCT s.security_id) FROM stage_option_keys s
        WHERE EXISTS (
                SELECT 1 FROM options.contracts c
                WHERE c.security_id = s.security_id AND (c.underlying_id, c.expiry, c.strike_px, c.side) IS DISTINCT FROM (s.underlying_id, s.expiry, s.strike_px, s.side))
          AND NOT EXISTS (
                SELECT 1 FROM options.contracts c2
                WHERE c2.security_id = s.security_id AND c2.expiry = s.expiry AND (c2.underlying_id, c2.strike_px, c2.side) = (s.underlying_id, s.strike_px, s.side))
        """;

    // A new (security id, expiry) is inserted; a contract another security id already holds is a counted conflict, never an error.
    private const string InsertNew =
        """
        INSERT INTO options.contracts (security_id, underlying_id, instrument, expiry, strike_px, side, expiry_kind, lot_size_seen, tick_px, first_seen_on, last_seen_on)
        SELECT s.security_id, s.underlying_id, s.instrument, s.expiry, s.strike_px, s.side, s.expiry_kind, s.lot_size_seen, s.tick_px, @today, @today
        FROM stage_option_keys s
        WHERE NOT EXISTS (SELECT 1 FROM options.contracts c WHERE c.security_id = s.security_id AND c.expiry = s.expiry)
        ON CONFLICT DO NOTHING
        """;

    // Only the very same contract is advanced: a stored row whose underlying, strike or side differ is a reused id and keeps its dates.
    private const string Advance =
        """
        UPDATE options.contracts c SET last_seen_on = @today
        FROM stage_option_keys s
        WHERE c.security_id = s.security_id AND c.expiry = s.expiry AND c.underlying_id = s.underlying_id AND c.strike_px = s.strike_px AND c.side = s.side
          AND c.last_seen_on < @today
        """;

    // Rows of the file that are now stored neither as themselves nor under a reused id: another security id holds the same contract.
    private const string CountConflicts =
        """
        SELECT count(*) FROM stage_option_keys s
        WHERE NOT EXISTS (SELECT 1 FROM options.contracts c WHERE c.security_id = s.security_id AND c.expiry = s.expiry)
        """;

    private const string InsertSeeds =
        """
        INSERT INTO options.lot_sizes (underlying_id, effective_from, lot_size, source, source_url, confirmed)
        SELECT i.id, x.effective_from, x.lot_size, x.source, x.source_url, x.confirmed
        FROM unnest(@symbols, @dates, @lots, @sources, @urls, @confirmed) AS x(symbol, effective_from, lot_size, source, source_url, confirmed)
        JOIN market.instruments i ON i.canonical_symbol = x.symbol AND i.type IN ('index', 'share') AND i.retired_at IS NULL
        WHERE NOT EXISTS (
            SELECT 1 FROM options.lot_sizes l
            WHERE l.underlying_id = i.id AND l.effective_from = x.effective_from AND l.source = x.source AND l.lot_size = x.lot_size)
        """;

    private const string InsertLotChanges =
        """
        INSERT INTO options.lot_sizes (underlying_id, effective_from, lot_size, source, source_url, confirmed)
        SELECT o.underlying_id, @today, o.lot_size, 'scrip_master', NULL, false
        FROM unnest(@ids, @lots) AS o(underlying_id, lot_size)
        WHERE o.lot_size IS DISTINCT FROM (
            SELECT l.lot_size FROM options.lot_sizes l
            WHERE l.underlying_id = o.underlying_id AND l.source = 'scrip_master'
            ORDER BY l.effective_from DESC, l.id DESC
            LIMIT 1)
        """;

    public async ValueTask<IReadOnlyDictionary<string, Guid>> ResolveUnderlyingsAsync(IReadOnlyCollection<string> canonicalSymbols, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(canonicalSymbols);
        var found = new Dictionary<string, Guid>(StringComparer.Ordinal);
        if (canonicalSymbols.Count == 0)
        {
            return found;
        }

        await using var command = dataSource.CreateCommand(
            "SELECT canonical_symbol, id FROM market.instruments WHERE canonical_symbol = ANY(@symbols) AND type IN ('index', 'share') AND retired_at IS NULL");
        command.Parameters.AddWithValue("symbols", canonicalSymbols.ToArray());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            found[reader.GetString(0)] = reader.GetGuid(1);
        }

        return found;
    }

    public async ValueTask<OptionContractsApplied> ApplyAsync(
        IReadOnlyList<OptionContractCandidate> contracts,
        IReadOnlyList<LotObservation> lots,
        IReadOnlyList<LotSizeSeed> seeds,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contracts);
        ArgumentNullException.ThrowIfNull(lots);
        ArgumentNullException.ThrowIfNull(seeds);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await RunAsync(connection, transaction, CreateStage, null, cancellationToken);
        await CopyAsync(connection, contracts, cancellationToken);
        await RunAsync(connection, transaction, Dedupe, null, cancellationToken);
        var reused = await CountAsync(connection, transaction, CountReused, cancellationToken);
        var inserted = await RunAsync(connection, transaction, InsertNew, c => c.Parameters.AddWithValue("today", today), cancellationToken);
        var advanced = await RunAsync(connection, transaction, Advance, c => c.Parameters.AddWithValue("today", today), cancellationToken);
        var conflicts = await CountAsync(connection, transaction, CountConflicts, cancellationToken) + await CountAsync(connection, transaction, CountDuplicates, cancellationToken);

        var seedsWritten = await RunAsync(connection, transaction, InsertSeeds, c =>
        {
            c.Parameters.AddWithValue("symbols", seeds.Select(s => s.UnderlyingSymbol).ToArray());
            c.Parameters.AddWithValue("dates", NpgsqlDbType.Array | NpgsqlDbType.Date, seeds.Select(s => s.EffectiveFrom).ToArray());
            c.Parameters.AddWithValue("lots", seeds.Select(s => s.LotSize).ToArray());
            c.Parameters.AddWithValue("sources", seeds.Select(s => s.Source).ToArray());
            c.Parameters.AddWithValue("urls", seeds.Select(s => s.SourceUrl).ToArray());
            c.Parameters.AddWithValue("confirmed", seeds.Select(s => s.Confirmed).ToArray());
        }, cancellationToken);
        var lotChanges = await RunAsync(connection, transaction, InsertLotChanges, c =>
        {
            c.Parameters.AddWithValue("today", today);
            c.Parameters.AddWithValue("ids", lots.Select(l => l.UnderlyingId).ToArray());
            c.Parameters.AddWithValue("lots", lots.Select(l => l.LotSize).ToArray());
        }, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new OptionContractsApplied(inserted, advanced, conflicts, reused, seedsWritten, lotChanges);
    }

    private static async Task CopyAsync(NpgsqlConnection connection, IReadOnlyList<OptionContractCandidate> contracts, CancellationToken cancellationToken)
    {
        await using var importer = await connection.BeginBinaryImportAsync(
            "COPY stage_option_contracts (security_id, underlying_id, instrument, expiry, strike_px, side, expiry_kind, lot_size_seen, tick_px) FROM STDIN (FORMAT BINARY)",
            cancellationToken);
        foreach (var c in contracts)
        {
            await importer.StartRowAsync(cancellationToken);
            await importer.WriteAsync(c.SecurityId, NpgsqlDbType.Bigint, cancellationToken);
            await importer.WriteAsync(c.UnderlyingId, NpgsqlDbType.Uuid, cancellationToken);
            await importer.WriteAsync(c.Instrument, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(c.Expiry, NpgsqlDbType.Date, cancellationToken);
            await importer.WriteAsync(c.StrikePx, NpgsqlDbType.Integer, cancellationToken);
            await importer.WriteAsync(c.Side, NpgsqlDbType.Text, cancellationToken);
            if (c.ExpiryKind is null)
            {
                await importer.WriteNullAsync(cancellationToken);
            }
            else
            {
                await importer.WriteAsync(c.ExpiryKind, NpgsqlDbType.Text, cancellationToken);
            }

            await importer.WriteAsync(c.LotSizeSeen, NpgsqlDbType.Integer, cancellationToken);
            if (c.TickPx is null)
            {
                await importer.WriteNullAsync(cancellationToken);
            }
            else
            {
                await importer.WriteAsync(c.TickPx.Value, NpgsqlDbType.Integer, cancellationToken);
            }
        }

        await importer.CompleteAsync(cancellationToken);
    }

    private static async Task<int> CountAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        return (int)(long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<int> RunAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, Action<NpgsqlCommand>? parameters, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        parameters?.Invoke(command);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
