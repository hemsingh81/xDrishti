using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.LiveTrading;

namespace XDrishti.Infrastructure.LiveTrading;

/// <summary>The paper side of the live-versus-paper report (spec P11-03): select only, from <c>paper.epoch_accounts</c>, <c>paper.entries</c>, <c>paper.exits</c> and <c>paper.skips</c> of the newest epoch. Nothing here writes or calls a broker.</summary>
internal sealed class PaperTradeSource(NpgsqlDataSource dataSource) : IPaperTradeSource
{
    public async ValueTask<PaperAccountsRead> AccountsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var epoch = dataSource.CreateCommand("SELECT id, started_on FROM paper.epochs ORDER BY id DESC LIMIT 1");
            long id;
            DateOnly started;
            await using (var reader = await epoch.ExecuteReaderAsync(cancellationToken))
            {
                if (!await reader.ReadAsync(cancellationToken))
                {
                    return new PaperAccountsRead(true, null, null, []);
                }

                id = reader.GetInt64(0);
                started = DateOnly.FromDateTime(reader.GetDateTime(1));
            }

            await using var select = dataSource.CreateCommand("SELECT DISTINCT account_id FROM paper.epoch_accounts WHERE epoch_id = @epoch AND mode = 'paper' ORDER BY account_id");
            select.Parameters.AddWithValue("epoch", NpgsqlDbType.Bigint, id);
            var accounts = new List<Guid>();
            await using var rows = await select.ExecuteReaderAsync(cancellationToken);
            while (await rows.ReadAsync(cancellationToken))
            {
                accounts.Add(rows.GetGuid(0));
            }

            return new PaperAccountsRead(true, id, started, accounts);
        }
        catch (NpgsqlException)
        {
            return new PaperAccountsRead(false, null, null, []);
        }
    }

    public async ValueTask<PaperTradesRead> TradesAsync(long epochId, Guid accountId, IReadOnlyList<long> itemIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(itemIds);
        var ids = itemIds.ToArray();
        if (ids.Length == 0)
        {
            return new PaperTradesRead(true, [], new Dictionary<long, string>());
        }

        try
        {
            var trades = new List<PaperTrade>();
            await using (var select = dataSource.CreateCommand("""
                -- uq_exits_entry_id and uq_entries_item_account: at most one exit per entry and one entry per item and account, so the join is one row per ticket
                SELECT e.plan_item_id, e.symbol, e.side, e.entry_price, e.qty, x.exit_price, x.charges
                FROM paper.entries e LEFT JOIN paper.exits x ON x.entry_id = e.id
                WHERE e.epoch_id = @epoch AND e.account_id = @account AND e.plan_item_id = ANY(@ids)
                ORDER BY e.plan_item_id, e.id
                """))
            {
                select.Parameters.AddWithValue("epoch", NpgsqlDbType.Bigint, epochId);
                select.Parameters.AddWithValue("account", NpgsqlDbType.Uuid, accountId);
                select.Parameters.AddWithValue("ids", NpgsqlDbType.Array | NpgsqlDbType.Bigint, ids);
                await using var reader = await select.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    trades.Add(new PaperTrade(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetDecimal(3), reader.GetInt32(4), reader.IsDBNull(5) ? null : reader.GetDecimal(5), reader.IsDBNull(6) ? null : reader.GetDecimal(6)));
                }
            }

            var skips = new Dictionary<long, string>();
            await using var skip = dataSource.CreateCommand("SELECT plan_item_id, code FROM paper.skips WHERE epoch_id = @epoch AND account_id = @account AND plan_item_id = ANY(@ids) ORDER BY id");
            skip.Parameters.AddWithValue("epoch", NpgsqlDbType.Bigint, epochId);
            skip.Parameters.AddWithValue("account", NpgsqlDbType.Uuid, accountId);
            skip.Parameters.AddWithValue("ids", NpgsqlDbType.Array | NpgsqlDbType.Bigint, ids);
            await using var rows = await skip.ExecuteReaderAsync(cancellationToken);
            while (await rows.ReadAsync(cancellationToken))
            {
                skips.TryAdd(rows.GetInt64(0), rows.GetString(1));
            }

            return new PaperTradesRead(true, trades, skips);
        }
        catch (NpgsqlException)
        {
            return new PaperTradesRead(false, [], new Dictionary<long, string>());
        }
    }
}
