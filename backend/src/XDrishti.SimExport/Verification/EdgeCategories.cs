using System.Globalization;
using System.Text.Json;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.SimExport.Verification;

/// <summary>
/// The edge categories of the verification pack (spec P3-09, V2): a table of codes, each a pure function of a trade, its instrument's run record and the recorded definition. A category <i>occurs</i> when a trade of the pooled runs belongs
/// to it. The table is in a fixed order, which is the order of the cover rule. A category the record cannot tell apart or that has no trade (a special session, a position the data cut short) is a spot check, not a row of this table.
/// </summary>
public static class EdgeCategories
{
    /// <summary>The codes, in the order of the table.</summary>
    public static IReadOnlyList<string> Codes { get; } = Array.AsReadOnly(
    [
        "sell", "stop_gap", "stop_in_entry_minute", "entry_at_limit", "entry_after_hole", "first_evaluation", "overnight_entry",
        "exit_target", "exit_square_off", "exit_max_hold", "incomplete_trigger_bar", "adjusted_instrument", "tick_fallback",
    ]);

    /// <summary>What the table does with each exit reason: the code it is a category under, or the reason it has none. The test iterates the enum and fails when a new value is not here.</summary>
    public static IReadOnlyDictionary<ExitReason, string> ExitReasonHandling { get; } = new Dictionary<ExitReason, string>
    {
        [ExitReason.Stop] = "stop_gap, stop_in_entry_minute",
        [ExitReason.Target] = "exit_target",
        [ExitReason.SquareOff] = "exit_square_off",
        [ExitReason.MaxHold] = "exit_max_hold",
        [ExitReason.EmaExit] = "none: a trail policy's own exit, not a category of the pack's table",
        [ExitReason.TimeStop] = "none: not a category of the pack yet; the verification of the P4 exits is the pack of P4-06, and the trade is an ordinary card in the sample",
        [ExitReason.EndOfData] = "none: the position is in OpenAtEnd, gets no card, and is a spot check",
    };

    /// <summary>What the table does with each entry fill; the same rule as <see cref="ExitReasonHandling"/>.</summary>
    public static IReadOnlyDictionary<EntryFill, string> EntryFillHandling { get; } = new Dictionary<EntryFill, string>
    {
        [EntryFill.AtOpen] = "none: the ordinary fill",
        [EntryFill.AtLimit] = "entry_at_limit",
    };

    /// <summary>The codes of the categories the trade belongs to, in table order.</summary>
    public static IReadOnlyList<string> Of(SimulationRun run, RunTrade trade)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(trade);
        return For(run)(trade);
    }

    /// <summary>A classifier for the trades of one run: the instruments are indexed and the definition is read once.</summary>
    public static Func<RunTrade, IReadOnlyList<string>> For(SimulationRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var instruments = new Dictionary<Guid, InstrumentRun>();
        foreach (var instrument in run.Instruments)
        {
            instruments.TryAdd(instrument.InstrumentId, instrument);
        }

        var firstEntry = FirstEntryMinute(run.Strategy.DefinitionJson);
        return runTrade => Classify(runTrade.Trade, instruments.GetValueOrDefault(runTrade.Trade.InstrumentId), firstEntry);
    }

    /// <summary>The first entry time of the recorded definition as a minute of the day, or null when the text does not parse or has none.</summary>
    public static int? FirstEntryMinute(string? definitionJson)
    {
        if (string.IsNullOrEmpty(definitionJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(definitionJson);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("time_rules", out var rules) && rules.ValueKind == JsonValueKind.Object
                && rules.TryGetProperty("first_entry", out var first) && first.ValueKind == JsonValueKind.String
                && TimeSpan.TryParseExact(first.GetString(), @"hh\:mm", CultureInfo.InvariantCulture, out var time))
            {
                return (int)time.TotalMinutes;
            }
        }
        catch (Exception bad) when (bad is JsonException or InvalidOperationException)
        {
            // not JSON, or a string with a lone surrogate: no first entry time
        }

        return null;
    }

    private static List<string> Classify(SimTrade t, InstrumentRun? instrument, int? firstEntryMinute)
    {
        var decisionDate = SessionClock.DateOf(t.DecisionEpochSeconds - 60);
        var fillDate = SessionClock.DateOf(t.EntryEpochSeconds);
        var stopped = t.ExitReason == ExitReason.Stop;
        var beyondStop = t.Side == Side.Buy ? t.ExitPrice < t.StopPrice : t.ExitPrice > t.StopPrice;

        var found = new List<string>();
        Add(found, "sell", t.Side == Side.Sell);
        Add(found, "stop_gap", stopped && beyondStop);
        Add(found, "stop_in_entry_minute", stopped && t.ExitEpochSeconds == t.EntryEpochSeconds);
        Add(found, "entry_at_limit", t.EntryFill == EntryFill.AtLimit);
        Add(found, "entry_after_hole", fillDate == decisionDate && t.EntryEpochSeconds > t.DecisionEpochSeconds);
        Add(found, "first_evaluation", firstEntryMinute is { } minute && SessionClock.MinuteOfDay(t.DecisionEpochSeconds) == minute);
        Add(found, "overnight_entry", fillDate > decisionDate);
        Add(found, "exit_target", t.ExitReason == ExitReason.Target);
        Add(found, "exit_square_off", t.ExitReason == ExitReason.SquareOff);
        Add(found, "exit_max_hold", t.ExitReason == ExitReason.MaxHold);
        Add(found, "incomplete_trigger_bar", !t.TriggerBarComplete);
        Add(found, "adjusted_instrument", instrument is not null && instrument.Adjustments.Any(a => a.ExDate > t.TradeDate));
        Add(found, "tick_fallback", t.TickFallback);
        return found;
    }

    private static void Add(List<string> found, string code, bool belongs)
    {
        if (belongs)
        {
            found.Add(code);
        }
    }
}
