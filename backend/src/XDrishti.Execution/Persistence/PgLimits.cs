using System.Text.Json;
using Npgsql;
using XDrishti.Domain.Execution.Limits;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain.Limits;

namespace XDrishti.Execution.Persistence;

/// <summary>
/// The limits sheet in force, the newest settings row and the feed's newest price time, as the role xd_exec reads them (spec P12-09; SELECT only). The sheet in force is the newest row (by id) whose <c>effective_from</c> has passed by the
/// database clock, the same rule as the trigger <c>execution.check_ledger_entry</c>.
/// </summary>
internal sealed class PgLimits(NpgsqlDataSource data) : ILimitsStore, IAutoLiveSettingsSource, IFeedHealth
{
    private const string InForce = """
        SELECT clock_timestamp(), l.id, l.account_id, l.max_orders_per_day, l.max_notional_per_order, l.max_notional_per_day, l.max_loss_per_day, l.max_open_positions, l.allow_list::text, l.window_start, l.window_end, l.products,
               l.exit_all_on_loss_halt, l.effective_from
          FROM (SELECT 1) one
          LEFT JOIN LATERAL (SELECT * FROM execution.limits x WHERE x.account_id = @a AND x.effective_from <= clock_timestamp() ORDER BY x.id DESC LIMIT 1) l ON true
        """;

    public async Task<LimitsReading> ReadAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using var command = data.CreateCommand(InForce);
        command.Parameters.AddWithValue("a", accountId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        var now = reader.GetFieldValue<DateTimeOffset>(0);
        if (reader.IsDBNull(1))
        {
            return new LimitsReading(now, null);
        }

        var sheet = new ExecutionLimits(
            reader.IsDBNull(3) ? null : reader.GetInt32(3),
            reader.IsDBNull(4) ? null : reader.GetDecimal(4),
            reader.IsDBNull(5) ? null : reader.GetDecimal(5),
            reader.IsDBNull(6) ? null : reader.GetDecimal(6),
            reader.IsDBNull(7) ? null : reader.GetInt32(7),
            JsonSerializer.Deserialize<string[]>(reader.GetString(8)) ?? [],
            reader.IsDBNull(9) ? null : reader.GetFieldValue<TimeOnly>(9),
            reader.IsDBNull(10) ? null : reader.GetFieldValue<TimeOnly>(10),
            reader.GetFieldValue<string[]>(11),
            reader.GetBoolean(12));
        return new LimitsReading(now, new LimitsRow(reader.GetInt64(1), reader.GetGuid(2), sheet, reader.GetFieldValue<DateTimeOffset>(13)));
    }

    async Task<AutoLiveSettings?> IAutoLiveSettingsSource.ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var command = data.CreateCommand("SELECT id, execution_enabled, auto_live_enabled, \"values\"::text FROM execution.settings ORDER BY id DESC LIMIT 1");
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return AutoLiveSettings.None;
            }

            using var document = JsonDocument.Parse(reader.GetString(3));
            var values = document.RootElement.ValueKind == JsonValueKind.Object
                ? document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString()! : p.Value.GetRawText(), StringComparer.Ordinal)
                : null;
            return AutoLiveSettings.From(reader.GetInt64(0), reader.GetBoolean(1), reader.GetBoolean(2), values);
        }
        catch (Exception failure) when (failure is NpgsqlException or JsonException)
        {
            return null;
        }
    }

    public async Task<DateTimeOffset?> LastPriceAtAsync(string symbol, CancellationToken cancellationToken)
    {
        try
        {
            await using var command = data.CreateCommand("SELECT q.tick_at FROM market.live_quotes q JOIN market.instruments m ON m.id = q.instrument_id WHERE m.canonical_symbol = @s AND m.retired_at IS NULL LIMIT 2");
            command.Parameters.AddWithValue("s", symbol);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var times = new List<DateTimeOffset>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                times.Add(reader.GetFieldValue<DateTimeOffset>(0));
            }

            return times.Count == 1 ? times[0] : null; // none or ambiguous: stale
        }
        catch (NpgsqlException)
        {
            return null;
        }
    }
}
