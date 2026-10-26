using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Domain.Paper;

namespace XDrishti.Infrastructure.Paper;

/// <summary>The raw read behind backtest versus paper (spec P10-04, ADR 0122): select only, one query each. The class inserts nothing.</summary>
internal sealed class BacktestPaperData(NpgsqlDataSource dataSource) : IBacktestPaperData
{
    public async ValueTask<PaperCompareData> ReadAsync(long epochId, CancellationToken cancellationToken)
    {
        var rows = new List<PaperCompareRow>();
        await using (var select = dataSource.CreateCommand("""
            SELECT e.id, e.plan_item_id, e.ticket_group, e.measure_status, e.cell_key, e.trade_key, e.entry_at, x.exit_at, x.r_sim, x.r_basis, x.charge_sheet_id, e.approval_kind
            FROM paper.entries e LEFT JOIN paper.exits x ON x.entry_id = e.id
            WHERE e.epoch_id = @epoch
            ORDER BY e.entry_at, e.id
            """))
        {
            select.Parameters.AddWithValue("epoch", NpgsqlDbType.Bigint, epochId);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new PaperCompareRow(
                    reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5),
                    reader.GetFieldValue<DateTimeOffset>(6),
                    reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
                    reader.IsDBNull(8) ? null : reader.GetDouble(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.IsDBNull(10) ? null : reader.GetString(10),
                    reader.GetString(11)));
            }
        }

        await using var other = dataSource.CreateCommand("""
            SELECT (SELECT count(*) FROM paper.entries WHERE epoch_id <> @epoch)::int,
                   (SELECT min(started_on) FROM paper.epochs WHERE id > @epoch)
            """);
        other.Parameters.AddWithValue("epoch", NpgsqlDbType.Bigint, epochId);
        await using var r = await other.ExecuteReaderAsync(cancellationToken);
        await r.ReadAsync(cancellationToken);
        return new PaperCompareData(rows, r.GetInt32(0), r.IsDBNull(1) ? null : DateOnly.FromDateTime(r.GetDateTime(1)));
    }
}
