using Npgsql;
using NpgsqlTypes;
using XDrishti.Domain.Planning;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain.Intents;

namespace XDrishti.Execution.Persistence;

/// <summary>
/// The plan side of the intent creator as the role xd_exec, read only (P12-01b granted SELECT on these tables; P12-00 D11: no <c>trading.*</c> row is written): the allocations of tickets that passed through an owner approval, with the
/// ticket's newest state, its P7-19 levels, the approval row, and the Dhan NSE equity security id of the symbol (null when unknown or ambiguous); and the newest live price of a symbol.
/// </summary>
internal sealed class PgIntentPlanSource(NpgsqlDataSource data) : IIntentPlanSource, IIntentQuotes
{
    private const string Candidates = """
        SELECT p.id, i.id, ap.approval_id, p.for_date, a.account_id, a.quantity, i.side, i.symbol,
               (SELECT CASE WHEN count(DISTINCT b.broker_id) = 1 THEN min(b.broker_id) END
                  FROM market.instruments m JOIN market.instrument_broker_ids b ON b.instrument_id = m.id
                 WHERE m.canonical_symbol = i.symbol AND m.retired_at IS NULL AND b.broker = 'dhan' AND b.segment = 'NSE_EQ'),
               i.entry_ref_price, i.stop_level, i.target_level, st.to_state
          FROM trading.plans p
          JOIN trading.plan_items i ON i.plan_id = p.id
          JOIN trading.plan_item_allocations a ON a.item_id = i.id
          JOIN LATERAL (SELECT e.to_state FROM trading.plan_events e WHERE e.item_id = i.id ORDER BY e.id DESC LIMIT 1) st ON true
          JOIN LATERAL (SELECT e.approval_id FROM trading.plan_events e WHERE e.item_id = i.id AND e.to_state = 'approved' ORDER BY e.id DESC LIMIT 1) ap ON true
         WHERE p.for_date BETWEEN @s AND @s + 7
         ORDER BY p.for_date, i.id, a.account_id
        """;

    public async Task<IReadOnlyList<PlanEntryCandidate>?> CandidatesAsync(DateOnly session, CancellationToken cancellationToken)
    {
        try
        {
            await using var command = data.CreateCommand(Candidates);
            command.Parameters.Add(new NpgsqlParameter("s", NpgsqlDbType.Date) { Value = session });
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var list = new List<PlanEntryCandidate>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (TicketStateText.TryParse(reader.GetString(12)) is not { } state)
                {
                    continue;
                }

                list.Add(new(
                    reader.GetInt64(0), reader.GetInt64(1), reader.IsDBNull(2) ? null : reader.GetInt64(2), reader.GetFieldValue<DateOnly>(3), reader.GetGuid(4), reader.GetInt32(5), reader.GetString(6), reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8), Dec(reader, 9), Dec(reader, 10), Dec(reader, 11), state));
            }

            return list;
        }
        catch (NpgsqlException)
        {
            return null;
        }
    }

    public async Task<LivePrice?> LastAsync(string symbol, CancellationToken cancellationToken)
    {
        try
        {
            await using var command = data.CreateCommand(
                "SELECT q.last_price, q.tick_at FROM market.live_quotes q JOIN market.instruments m ON m.id = q.instrument_id WHERE m.canonical_symbol = @s AND m.retired_at IS NULL LIMIT 2");
            command.Parameters.AddWithValue("s", symbol);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            var price = new LivePrice(reader.GetDecimal(0), reader.GetFieldValue<DateTimeOffset>(1));
            return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? null : price; // two instruments with one symbol: no price is better than the wrong one
        }
        catch (NpgsqlException)
        {
            return null;
        }
    }

    private static decimal? Dec(NpgsqlDataReader reader, int i) => reader.IsDBNull(i) ? null : reader.GetDecimal(i);
}
