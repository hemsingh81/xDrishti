using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Domain.Paper;

namespace XDrishti.Infrastructure.Paper;

/// <summary>The raw reads behind the weekly paper review (spec P10-03, ADR 0121): one select each over the insert-only paper tables. This class inserts nothing.</summary>
internal sealed class PaperReviewData(NpgsqlDataSource dataSource) : IPaperReviewData
{
    public async ValueTask<IReadOnlyList<ReviewTrade>> TradesAsync(long epochId, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("""
            SELECT e.plan_id, e.plan_item_id, e.account_id, e.symbol, e.side, e.ticket_group, e.trade_key, e.measure_status, e.qty_allocated, e.qty, e.entry_at,
                   x.id, x.exit_at, x.exit_reason, x.gross_pnl, x.charges, x.net_pnl, x.r_exact, x.r_gross, x.r_net, x.r_sim
            FROM paper.entries e LEFT JOIN paper.exits x ON x.entry_id = e.id
            WHERE e.epoch_id = @epoch
            ORDER BY e.entry_at, e.id
            """);
        select.Parameters.AddWithValue("epoch", NpgsqlDbType.Bigint, epochId);
        var trades = new List<ReviewTrade>();
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            decimal? M(int i) => reader.IsDBNull(i) ? null : reader.GetDecimal(i);
            var exit = reader.IsDBNull(11)
                ? null
                : new ReviewExit(reader.GetFieldValue<DateTimeOffset>(12), reader.GetString(13), reader.GetDecimal(14), M(15), M(16), reader.GetDouble(17), reader.GetDouble(18), reader.IsDBNull(19) ? null : reader.GetDouble(19), reader.GetDouble(20));
            trades.Add(new ReviewTrade(
                reader.GetInt64(0), reader.GetInt64(1), reader.GetGuid(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6),
                string.Equals(reader.GetString(7), "measured", StringComparison.Ordinal), reader.GetInt32(8), reader.GetInt32(9), reader.GetFieldValue<DateTimeOffset>(10), exit));
        }

        return trades;
    }

    public async ValueTask<IReadOnlyList<ReviewSkip>> SkipsAsync(long epochId, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand("SELECT plan_item_id, account_id, code FROM paper.skips WHERE epoch_id = @epoch ORDER BY id");
        select.Parameters.AddWithValue("epoch", NpgsqlDbType.Bigint, epochId);
        var skips = new List<ReviewSkip>();
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            skips.Add(new ReviewSkip(reader.GetInt64(0), reader.GetGuid(1), reader.GetString(2)));
        }

        return skips;
    }
}
