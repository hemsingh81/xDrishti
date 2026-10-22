using Npgsql;
using NpgsqlTypes;
using XDrishti.Domain.Common;
using XDrishti.Domain.Research;

namespace XDrishti.Infrastructure.Research;

/// <summary>
/// The legs of a stored trade (spec P4-05, D9): <c>research.sim_trade_legs</c> and <c>research.sim_trade_leg_charges</c>, written by a binary copy after the trades, in the same transaction, and read in chunks beside the trades. A run with no trade
/// that has legs writes and reads nothing here. The columns are the properties of <see cref="Leg"/>, <see cref="LegCharge"/> and <see cref="LegCharges"/>; a test compares the lists below with them.
/// </summary>
internal sealed partial class RunStore
{
    /// <summary>The columns of <c>sim_trade_legs</c> beyond its four key columns, in the order of the copy and the reads.</summary>
    internal static readonly IReadOnlyList<string> LegColumnNames = ["epoch_seconds", "price", "filled_price", "share", "kind", "reason"];

    /// <summary>The columns of <c>sim_trade_leg_charges</c> beyond its four key columns, in the order of the copy and the reads.</summary>
    internal static readonly IReadOnlyList<string> ChargeColumnNames = ["epoch_seconds", "quantity", "turnover", "brokerage", "stt", "exchange_txn", "sebi_fee", "stamp_duty", "other_charges", "dp_charge", "gst"];

    private const int LegChunk = 2_000;

    private static readonly string LegCopySql = "COPY research.sim_trade_legs (run_id, open_at_end, ordinal, leg_ordinal, " + string.Join(", ", LegColumnNames) + ") FROM STDIN (FORMAT BINARY)";

    private static readonly string ChargeCopySql = "COPY research.sim_trade_leg_charges (run_id, open_at_end, ordinal, charge_ordinal, " + string.Join(", ", ChargeColumnNames) + ") FROM STDIN (FORMAT BINARY)";

    private static readonly string LegSelect = "SELECT open_at_end, ordinal, " + string.Join(", ", LegColumnNames) + " FROM research.sim_trade_legs WHERE run_id = @run_id AND (open_at_end, ordinal) >= (@from_open, @from_ordinal) AND (open_at_end, ordinal) <= (@to_open, @to_ordinal) ORDER BY open_at_end, ordinal, leg_ordinal";

    private static readonly string ChargeSelect = "SELECT open_at_end, ordinal, " + string.Join(", ", ChargeColumnNames) + " FROM research.sim_trade_leg_charges WHERE run_id = @run_id AND (open_at_end, ordinal) >= (@from_open, @from_ordinal) AND (open_at_end, ordinal) <= (@to_open, @to_ordinal) ORDER BY open_at_end, ordinal, charge_ordinal";

    /// <summary>How many legs and how many fill charges the trades of the run hold: what the count check expects of the two tables.</summary>
    private static (long Legs, long Charges) LegCounts(SimulationRun run)
    {
        long legs = 0, charges = 0;
        foreach (var trade in run.Trades.Concat(run.OpenAtEnd))
        {
            legs += trade.Trade.Legs?.Count ?? 0;
            charges += trade.Trade.Costing?.LegCharges?.Count ?? 0;
        }

        return (legs, charges);
    }

    /// <summary>Copies the legs and the fill charges of the trades that have them, in the order of the trades; nothing is opened when no trade has any.</summary>
    private static async Task CopyLegsAsync(NpgsqlConnection connection, long runId, SimulationRun run, CancellationToken cancellationToken)
    {
        var (legCount, chargeCount) = LegCounts(run);
        if (legCount > 0)
        {
            await using var writer = await connection.BeginBinaryImportAsync(LegCopySql, cancellationToken);
            foreach (var (trades, openAtEnd) in new[] { (run.Trades, false), (run.OpenAtEnd, true) })
            {
                for (var ordinal = 0; ordinal < trades.Count; ordinal++)
                {
                    if (trades[ordinal].Trade.Legs is not { } legs)
                    {
                        continue;
                    }

                    for (var leg = 0; leg < legs.Count; leg++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var value = legs[leg];
                        await writer.StartRowAsync(cancellationToken);
                        await writer.WriteAsync(runId, NpgsqlDbType.Bigint, cancellationToken);
                        await writer.WriteAsync(openAtEnd, NpgsqlDbType.Boolean, cancellationToken);
                        await writer.WriteAsync(ordinal, NpgsqlDbType.Integer, cancellationToken);
                        await writer.WriteAsync(leg, NpgsqlDbType.Integer, cancellationToken);
                        await writer.WriteAsync(value.EpochSeconds, NpgsqlDbType.Bigint, cancellationToken);
                        await writer.WriteAsync(value.Price, NpgsqlDbType.Double, cancellationToken);
                        await writer.WriteAsync(value.FilledPrice, NpgsqlDbType.Double, cancellationToken);
                        await writer.WriteAsync(value.Share, NpgsqlDbType.Numeric, cancellationToken);
                        await writer.WriteAsync(EnumText<LegKind>.Name(value.Kind), NpgsqlDbType.Varchar, cancellationToken);
                        await writer.WriteAsync(value.Reason, NpgsqlDbType.Varchar, cancellationToken);
                    }
                }
            }

            await writer.CompleteAsync(cancellationToken);
        }

        if (chargeCount > 0)
        {
            await using var writer = await connection.BeginBinaryImportAsync(ChargeCopySql, cancellationToken);
            foreach (var (trades, openAtEnd) in new[] { (run.Trades, false), (run.OpenAtEnd, true) })
            {
                for (var ordinal = 0; ordinal < trades.Count; ordinal++)
                {
                    if (trades[ordinal].Trade.Costing?.LegCharges is not { } charges)
                    {
                        continue;
                    }

                    for (var index = 0; index < charges.Count; index++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var charge = charges[index];
                        await writer.StartRowAsync(cancellationToken);
                        await writer.WriteAsync(runId, NpgsqlDbType.Bigint, cancellationToken);
                        await writer.WriteAsync(openAtEnd, NpgsqlDbType.Boolean, cancellationToken);
                        await writer.WriteAsync(ordinal, NpgsqlDbType.Integer, cancellationToken);
                        await writer.WriteAsync(index, NpgsqlDbType.Integer, cancellationToken);
                        await writer.WriteAsync(charge.EpochSeconds, NpgsqlDbType.Bigint, cancellationToken);
                        await writer.WriteAsync(charge.Quantity, NpgsqlDbType.Bigint, cancellationToken);
                        await writer.WriteAsync(charge.Turnover, NpgsqlDbType.Numeric, cancellationToken);
                        foreach (var amount in new[] { charge.Charges.Brokerage, charge.Charges.Stt, charge.Charges.ExchangeTxn, charge.Charges.SebiFee, charge.Charges.StampDuty, charge.Charges.OtherCharges, charge.Charges.DpCharge, charge.Charges.Gst })
                        {
                            await writer.WriteAsync(amount, NpgsqlDbType.Numeric, cancellationToken);
                        }
                    }
                }
            }

            await writer.CompleteAsync(cancellationToken);
        }
    }

    /// <summary>The count check of the two tables inside the transaction: the rows written are the legs and the fill charges of the run.</summary>
    private static async Task<string?> CheckLegCountsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long runId, SimulationRun run, CancellationToken cancellationToken)
    {
        var (legs, charges) = LegCounts(run);
        foreach (var (table, expected) in new[] { ("sim_trade_legs", legs), ("sim_trade_leg_charges", charges) })
        {
            await using var count = new NpgsqlCommand("SELECT count(*) FROM research." + table + " WHERE run_id = @id", connection, transaction);
            count.Parameters.AddWithValue("id", NpgsqlDbType.Bigint, runId);
            var stored = (long)(await count.ExecuteScalarAsync(cancellationToken))!;
            if (stored != expected)
            {
                return $"The rows written to {table} ({stored}) differ from those of the run ({expected}); nothing was stored.";
            }
        }

        return null;
    }

    /// <summary>Whether the run has a stored leg.</summary>
    private async Task<bool> HasLegsAsync(long runId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM research.sim_trade_legs WHERE run_id = @run_id)", connection);
        command.Parameters.AddWithValue("run_id", NpgsqlDbType.Bigint, runId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>
    /// The legs and the fill charges of the trades of a chunk, found by the range of their keys on a connection of their own (the reader of the trades is still open on another), and put on the trades. A trade whose row has legs gets them in
    /// order; one with a costing gets the charges of its fills. A trade of the chunk that has none is returned as it is.
    /// </summary>
    private async Task<List<RunTrade>> WithLegsAsync(long runId, List<(bool OpenAtEnd, int Ordinal, RunTrade Trade)> chunk, CancellationToken cancellationToken)
    {
        var first = chunk[0];
        var last = chunk[^1];
        var legs = new Dictionary<(bool, int), List<Leg>>();
        var charges = new Dictionary<(bool, int), List<LegCharge>>();
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        {
            await using (var command = RangeCommand(connection, LegSelect, runId, first, last))
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    var key = (reader.GetBoolean(0), reader.GetInt32(1));
                    if (!legs.TryGetValue(key, out var list))
                    {
                        legs[key] = list = [];
                    }

                    list.Add(new Leg(reader.GetInt64(2), reader.GetDouble(3), reader.GetDouble(4), reader.GetDecimal(5), EnumText<LegKind>.Parse(reader.GetString(6)), reader.GetString(7)));
                }
            }

            await using (var command = RangeCommand(connection, ChargeSelect, runId, first, last))
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    var key = (reader.GetBoolean(0), reader.GetInt32(1));
                    if (!charges.TryGetValue(key, out var list))
                    {
                        charges[key] = list = [];
                    }

                    list.Add(new LegCharge(
                        reader.GetInt64(2), reader.GetInt64(3), reader.GetDecimal(4),
                        new LegCharges(reader.GetDecimal(5), reader.GetDecimal(6), reader.GetDecimal(7), reader.GetDecimal(8), reader.GetDecimal(9), reader.GetDecimal(10), reader.GetDecimal(11), reader.GetDecimal(12))));
                }
            }
        }

        var done = new List<RunTrade>(chunk.Count);
        foreach (var (openAtEnd, ordinal, runTrade) in chunk)
        {
            var trade = runTrade.Trade;
            if (legs.TryGetValue((openAtEnd, ordinal), out var tradeLegs))
            {
                trade = trade with { Legs = new ValueList<Leg>(tradeLegs) };
            }

            if (charges.TryGetValue((openAtEnd, ordinal), out var tradeCharges) && trade.Costing is { } costing)
            {
                trade = trade with { Costing = costing with { LegCharges = new ValueList<LegCharge>(tradeCharges) } };
            }

            done.Add(runTrade with { Trade = trade });
        }

        return done;
    }

    private static NpgsqlCommand RangeCommand(NpgsqlConnection connection, string sql, long runId, (bool OpenAtEnd, int Ordinal, RunTrade Trade) first, (bool OpenAtEnd, int Ordinal, RunTrade Trade) last)
    {
        var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("run_id", NpgsqlDbType.Bigint, runId);
        command.Parameters.AddWithValue("from_open", NpgsqlDbType.Boolean, first.OpenAtEnd);
        command.Parameters.AddWithValue("from_ordinal", NpgsqlDbType.Integer, first.Ordinal);
        command.Parameters.AddWithValue("to_open", NpgsqlDbType.Boolean, last.OpenAtEnd);
        command.Parameters.AddWithValue("to_ordinal", NpgsqlDbType.Integer, last.Ordinal);
        return command;
    }
}
