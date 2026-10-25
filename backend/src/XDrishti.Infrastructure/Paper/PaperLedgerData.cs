using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Domain.Market;
using XDrishti.Domain.Paper;

namespace XDrishti.Infrastructure.Paper;

/// <summary>
/// The raw reads behind the paper ledger (spec P10-02, ADR 0118): select only, raw SQL, one query each. The runtime role could not write these tables beyond inserts, and this class inserts nothing.
/// </summary>
internal sealed class PaperLedgerData(NpgsqlDataSource dataSource) : IPaperLedgerData
{
    public async ValueTask<EpochRow?> EpochAsync(long? epochId, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("SELECT id, started_on, started_at, kind, reason, actor FROM paper.epochs WHERE (@id IS NULL OR id = @id) ORDER BY id DESC LIMIT 1");
        select.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, (object?)epochId ?? DBNull.Value);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new EpochRow(reader.GetInt64(0), DateOnly.FromDateTime(reader.GetDateTime(1)), reader.GetFieldValue<DateTimeOffset>(2), reader.GetString(3), reader.GetString(4), reader.GetString(5))
            : null;
    }

    public async ValueTask<IReadOnlyList<LedgerTrade>> TradesAsync(long epochId, Guid? accountId, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("""
            SELECT e.id, e.account_id, e.symbol, e.side, e.qty, e.entry_at, e.entry_price, e.stop_price, e.swing, e.ticket_group, e.trade_key,
                   x.id, x.exit_at, x.gross_pnl, x.charges, x.net_pnl, x.charge_sheet_id, x.r_sim
            FROM paper.entries e LEFT JOIN paper.exits x ON x.entry_id = e.id
            WHERE e.epoch_id = @epoch AND (@account IS NULL OR e.account_id = @account)
            ORDER BY e.entry_at, e.id
            """);
        select.Parameters.AddWithValue("epoch", NpgsqlDbType.Bigint, epochId);
        select.Parameters.AddWithValue("account", NpgsqlDbType.Uuid, (object?)accountId ?? DBNull.Value);
        var trades = new List<LedgerTrade>();
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            decimal? M(int i) => reader.IsDBNull(i) ? null : reader.GetDecimal(i);
            var exit = reader.IsDBNull(11)
                ? null
                : new LedgerExit(reader.GetFieldValue<DateTimeOffset>(12), reader.GetDecimal(13), M(14), M(15), reader.IsDBNull(16) ? null : reader.GetString(16), reader.GetDouble(17));
            trades.Add(new LedgerTrade(
                reader.GetInt64(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4), reader.GetFieldValue<DateTimeOffset>(5),
                reader.GetDecimal(6), reader.GetDecimal(7), reader.GetBoolean(8), reader.GetString(9), reader.GetString(10), exit));
        }

        return trades;
    }

    public async ValueTask<IReadOnlySet<DateOnly>> FinishedSessionsAsync(DateOnly from, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("SELECT DISTINCT session_date FROM paper.capture_runs WHERE session_date >= @from AND outcome = 'done' AND pending = 0");
        select.Parameters.AddWithValue("from", NpgsqlDbType.Date, from);
        var result = new HashSet<DateOnly>();
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(DateOnly.FromDateTime(reader.GetDateTime(0)));
        }

        return result;
    }

    public async ValueTask<IReadOnlyDictionary<string, InstrumentClose>> ClosesAsync(IReadOnlyCollection<string> symbols, DateOnly upTo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        var result = new Dictionary<string, InstrumentClose>(StringComparer.OrdinalIgnoreCase);
        if (symbols.Count == 0)
        {
            return result;
        }

        await using var select = dataSource.CreateCommand("""
            SELECT i.canonical_symbol, i.id, (b.ts AT TIME ZONE 'Asia/Kolkata')::date, b.close, b.complete
            FROM market.instruments i
            JOIN LATERAL (SELECT ts, close, complete FROM market.bars_agg WHERE instrument_id = i.id AND timeframe = '1D' AND ts < @to ORDER BY ts DESC LIMIT 1) b ON true
            WHERE i.canonical_symbol = ANY(@symbols)
            """);
        select.Parameters.AddWithValue("to", NpgsqlDbType.TimestampTz, new DateTimeOffset(upTo.AddDays(1).ToDateTime(TimeOnly.MinValue), IndiaTime.Offset).ToUniversalTime());
        select.Parameters.AddWithValue("symbols", NpgsqlDbType.Array | NpgsqlDbType.Varchar, symbols.ToArray());
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result[reader.GetString(0)] = new InstrumentClose(reader.GetGuid(1), new DailyClose(DateOnly.FromDateTime(reader.GetDateTime(2)), (decimal)reader.GetDouble(3), reader.GetBoolean(4)));
        }

        return result;
    }

    public async ValueTask<IReadOnlyList<Guid>> EntryAccountsAsync(long epochId, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("SELECT DISTINCT account_id FROM paper.entries WHERE epoch_id = @epoch ORDER BY account_id");
        select.Parameters.AddWithValue("epoch", NpgsqlDbType.Bigint, epochId);
        var result = new List<Guid>();
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(reader.GetGuid(0));
        }

        return result;
    }

    public async ValueTask<StoredTotals> TotalsAsync(long epochId, Guid accountId, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("""
            SELECT COALESCE(SUM(COALESCE(x.net_pnl, x.gross_pnl)), 0), COUNT(*) FILTER (WHERE x.id IS NULL), COUNT(*) FILTER (WHERE x.exit_at < e.entry_at)
            FROM paper.entries e LEFT JOIN paper.exits x ON x.entry_id = e.id
            WHERE e.epoch_id = @epoch AND e.account_id = @account
            """);
        select.Parameters.AddWithValue("epoch", NpgsqlDbType.Bigint, epochId);
        select.Parameters.AddWithValue("account", NpgsqlDbType.Uuid, accountId);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new StoredTotals(reader.GetDecimal(0), (int)reader.GetInt64(1), (int)reader.GetInt64(2));
    }
}
