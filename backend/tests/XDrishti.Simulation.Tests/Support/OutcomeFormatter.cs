using System.Globalization;
using System.Text;
using XDrishti.Domain.Common;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>
/// A plain text rendering of walker results, every field of the trade included (invariant culture, round-trip doubles), so that "the same input gives the same bytes" and "this field-by-field equals that one" are
/// comparisons of strings. Nothing here decides anything about a trade.
/// </summary>
internal static class OutcomeFormatter
{
    public static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string Number(double? value) => value is { } v ? Number(v) : "null";

    public static string Render(Result<WalkOutcome> result)
    {
        if (result.IsFailure)
        {
            return "error " + result.Error.Code;
        }

        var outcome = result.Value;
        var text = new StringBuilder();
        if (outcome.Trade is { } t)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"trade schema={t.SchemaVersion} strategy={t.StrategyId} instrument={t.InstrumentId} symbol={t.Symbol} side={t.Side} date={t.TradeDate:yyyy-MM-dd} decision={t.DecisionEpochSeconds} ")
                .Append(CultureInfo.InvariantCulture,
                $"trigger={t.TriggerTimeframe}/{t.TriggerBar.EpochSeconds}/{Number(t.TriggerBar.Open)}/{Number(t.TriggerBar.High)}/{Number(t.TriggerBar.Low)}/{Number(t.TriggerBar.Close)}/{t.TriggerBar.Volume}/{t.TriggerBarMinutes}/{t.TriggerBarComplete} ")
                .Append(CultureInfo.InvariantCulture,
                $"entry={t.EntryEpochSeconds}@{Number(t.EntryPrice)} stop={Number(t.StopPrice)} target={Number(t.TargetPrice)} exit={t.ExitEpochSeconds}@{Number(t.ExitPrice)} reason={t.ExitReason} ")
                .Append(CultureInfo.InvariantCulture,
                $"R={Number(t.RExact)} mae={Number(t.MaeR)} mfe={Number(t.MfeR)} fills={t.Fills} costs={t.Costs} snapshot={t.EntrySnapshotJson ?? "null"} key={t.Key} ")
                .Append(CultureInfo.InvariantCulture,
                $"rFilled={Number(t.RFilled)} slippageR={Number(t.SlippageR)} entryFilled={Number(t.EntryFilledPrice)} exitFilled={Number(t.ExitFilledPrice)} entryFill={t.EntryFill} tickFallback={t.TickFallback}");
        }
        else
        {
            text.Append(CultureInfo.InvariantCulture, $"no-trade {outcome.NoTrade}");
        }

        return text.Append(CultureInfo.InvariantCulture, $" | examined={outcome.BarsExamined} delayed={outcome.EntryAfterMissingMinutes} stopGap={outcome.StopGapExit} targetGap={outcome.TargetGapExit} ambiguous={outcome.AmbiguousMinute} late={outcome.LateSquareOff} under4={outcome.RiskUnderFourTicks} zero={outcome.ZeroVolumeMinutes}").ToString();
    }

    public static string Render(WalkCounters c) =>
        string.Create(CultureInfo.InvariantCulture,
            $"candidates={c.Candidates} trades={c.Trades} noFillBar={c.NoFillBar} afterSquareOff={c.AfterSquareOff} entryThroughStop={c.EntryThroughStop} exitsStop={c.ExitsStop} exitsTarget={c.ExitsTarget} exitsSquareOff={c.ExitsSquareOff} exitsEndOfData={c.ExitsEndOfData} stopGapExits={c.StopGapExits} targetGapExits={c.TargetGapExits} entriesAfterMissingMinutes={c.EntriesAfterMissingMinutes} barsExamined={c.BarsExamined} limitNotReached={c.LimitNotReached} entriesAtOpen={c.EntriesAtOpen} entriesAtLimit={c.EntriesAtLimit} ambiguousMinutes={c.AmbiguousMinutes} lateSquareOffs={c.LateSquareOffs} zeroVolumeMinutes={c.ZeroVolumeMinutes} riskUnderFourTicks={c.RiskUnderFourTicks}");
}
