using System.Buffers;
using XDrishti.Domain.Research;

namespace XDrishti.SimExport.Csv;

/// <summary>
/// <c>trades.csv</c> and <c>open_at_end.csv</c> (spec P3-04b1, E3): the same columns in both files, one row per trade in the order of the record, UTF-8 without a byte order mark, <c>\n</c> line ends and a final newline, RFC 4180 quoting.
/// Numbers are written by the number format of the export (the round-trip form, <c>-0</c> as <c>0</c>, a NaN or an infinity throws), times as India time derived from the epoch, enums through <see cref="ExportSpellings"/>.
/// Text cells are cleaned of control and format characters and defused against spreadsheet formulas. The file prints no statistic: per-trade R is data. It writes to a sink the host owns and keeps no copy of the file.
/// </summary>
public static class TradesCsv
{
    /// <summary>The columns, in order. A reflection test requires every positional property of <see cref="SimTrade"/> but the reserved snapshot to be one of them or to feed some. <c>Costing</c> feeds the 17 columns <c>costing_*</c>, <c>cost_r</c> and <c>r_net</c>, empty for a gross trade; a line column is the sum of the two legs, the 16 lines of each leg are in <c>run.json</c>.</summary>
    public static IReadOnlyList<string> Columns { get; } = Array.AsReadOnly(
    [
        "run_key", "schema_version", "strategy_id", "instrument_id", "symbol", "side", "trade_date", "decision_epoch", "decision_ist",
        "trigger_timeframe", "trigger_bar_epoch", "trigger_bar_open", "trigger_bar_high", "trigger_bar_low", "trigger_bar_close", "trigger_bar_volume", "trigger_bar_minutes", "trigger_bar_complete",
        "entry_epoch", "entry_ist", "entry_price", "stop_price", "target_price", "exit_epoch", "exit_ist", "exit_price", "exit_reason",
        "r_exact", "mae_r", "mfe_r", "fills", "costs", "r_filled", "slippage_r", "entry_filled_price", "exit_filled_price", "entry_fill", "tick_fallback", "sessions_held", "sessions_unseen",
        "costing_segment", "costing_sheet", "costing_quantity", "costing_risk_taken", "costing_entry_turnover", "costing_exit_turnover", "costing_brokerage", "costing_stt", "costing_exchange_txn", "costing_sebi_fee", "costing_stamp_duty",
        "costing_other", "costing_dp_charge", "costing_gst", "costing_total_cost", "cost_r", "r_net", "basket_ids", "key",
    ]);

    private const int CostingColumns = 17;

    /// <summary>Writes the header and one row per trade.</summary>
    /// <param name="sink">Where the UTF-8 bytes go; the host owns it.</param>
    /// <param name="runKey">The run key (64 lower-case hex characters), repeated in every row: the check log of P3-09 needs it.</param>
    /// <param name="trades">The trades of one file: <c>run.Trades</c> or <c>run.OpenAtEnd</c>.</param>
    /// <exception cref="ArgumentException">The run key is not 64 lower-case hex characters, or a price or R of a trade is a NaN or an infinity.</exception>
    public static void Write(IBufferWriter<byte> sink, string runKey, IReadOnlyList<RunTrade> trades)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(trades);
        ExportText.RequireRunKey(runKey);

        var row = new CsvRow();
        row.Begin();
        foreach (var column in Columns)
        {
            row.Raw(column);
        }

        row.End(sink);

        foreach (var runTrade in trades)
        {
            WriteRow(row, sink, runKey, runTrade);
        }
    }

    private static void WriteRow(CsvRow row, IBufferWriter<byte> sink, string runKey, RunTrade runTrade)
    {
        var trade = runTrade.Trade;
        var bar = trade.TriggerBar;
        row.Begin();
        row.Raw(runKey);
        row.Number(trade.SchemaVersion);
        row.Text(trade.StrategyId);
        row.Raw(FormatGuid(row, trade.InstrumentId));
        row.Text(trade.Symbol);
        row.Raw(ExportSpellings.Of(trade.Side));
        row.Date(trade.TradeDate);
        row.Number(trade.DecisionEpochSeconds);
        row.IndiaTime(trade.DecisionEpochSeconds);
        row.Raw(ExportSpellings.Of(trade.TriggerTimeframe));
        row.Number(bar.EpochSeconds);
        row.Number(bar.Open);
        row.Number(bar.High);
        row.Number(bar.Low);
        row.Number(bar.Close);
        row.Number(bar.Volume);
        row.Number(trade.TriggerBarMinutes);
        row.Bool(trade.TriggerBarComplete);
        row.Number(trade.EntryEpochSeconds);
        row.IndiaTime(trade.EntryEpochSeconds);
        row.Number(trade.EntryPrice);
        row.Number(trade.StopPrice);
        if (trade.TargetPrice is { } target)
        {
            row.Number(target);
        }
        else
        {
            row.Empty();
        }

        row.Number(trade.ExitEpochSeconds);
        row.IndiaTime(trade.ExitEpochSeconds);
        row.Number(trade.ExitPrice);
        row.Raw(ExportSpellings.Of(trade.ExitReason));
        row.Number(trade.RExact);
        row.Number(trade.MaeR);
        row.Number(trade.MfeR);
        row.Raw(ExportSpellings.Of(trade.Fills));
        row.Raw(ExportSpellings.Of(trade.Costs));
        row.Number(trade.RFilled);
        row.Number(trade.SlippageR);
        row.Number(trade.EntryFilledPrice);
        row.Number(trade.ExitFilledPrice);
        row.Raw(ExportSpellings.Of(trade.EntryFill));
        row.Bool(trade.TickFallback);
        row.Number(trade.SessionsHeld);
        row.Number(trade.SessionsUnseen);
        Costing(row, trade.Costing);
        BasketIds(row, runTrade.BasketIds);
        Key(row, trade);
        row.End(sink);
    }

    /// <summary>The 17 cells of the costing: all empty for a gross trade; for a costed one the segment, the sheet, the quantity, the risk taken, the two turnovers, the seven line groups plus GST summed over the two legs, the cost and net R.</summary>
    private static void Costing(CsvRow row, TradeCosting? costing)
    {
        if (costing is null)
        {
            for (var i = 0; i < CostingColumns; i++)
            {
                row.Empty();
            }

            return;
        }

        var entry = costing.EntryLeg;
        var exit = costing.ExitLeg;
        row.Raw(ExportSpellings.Of(costing.Segment));
        row.Text(costing.SheetId);
        row.Number(costing.Quantity);
        row.Number(costing.RiskTaken);
        row.Number(costing.EntryTurnover);
        row.Number(costing.ExitTurnover);
        row.Number(entry.Brokerage + exit.Brokerage);
        row.Number(entry.Stt + exit.Stt);
        row.Number(entry.ExchangeTxn + exit.ExchangeTxn);
        row.Number(entry.SebiFee + exit.SebiFee);
        row.Number(entry.StampDuty + exit.StampDuty);
        row.Number(entry.OtherCharges + exit.OtherCharges);
        row.Number(entry.DpCharge + exit.DpCharge);
        row.Number(entry.Gst + exit.Gst);
        row.Number(costing.TotalCost);
        row.Number(costing.CostR);
        row.Number(costing.RNet);
    }

    /// <summary>The instrument id cell: composed in the scratch buffer (a GUID in the D form) so that no string is made for it.</summary>
    private static ReadOnlySpan<char> FormatGuid(CsvRow row, Guid id)
    {
        row.Scratch.Clear();
        row.Scratch.Append(id);
        return row.Scratch.Span;
    }

    private static void BasketIds(CsvRow row, IReadOnlyList<Guid> ids)
    {
        row.Scratch.Clear();
        for (var i = 0; i < ids.Count; i++)
        {
            if (i > 0)
            {
                row.Scratch.Append(';');
            }

            row.Scratch.Append(ids[i]);
        }

        row.Text(row.Scratch.Span);
    }

    /// <summary>The name of the side as <see cref="SimTrade.Key"/> spells it (the member name), without the boxing of <c>Enum.ToString</c>.</summary>
    private static string SideName(Side side) => side switch
    {
        Side.Buy => nameof(Side.Buy),
        Side.Sell => nameof(Side.Sell),
        _ => side.ToString(),
    };

    /// <summary>The key of the trade, as <see cref="SimTrade.Key"/> spells it, composed without making the string.</summary>
    private static void Key(CsvRow row, SimTrade trade)
    {
        row.Scratch.Clear();
        row.Scratch.Append(trade.StrategyId);
        row.Scratch.Append('|');
        row.Scratch.Append(trade.Symbol);
        row.Scratch.Append('|');
        row.Scratch.Append(SideName(trade.Side));
        row.Scratch.Append('|');
        row.Scratch.Append(trade.TradeDate);
        row.Text(row.Scratch.Span);
    }
}
