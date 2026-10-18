using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using XDrishti.Domain.Common;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>
/// The schema-2 view of P3-03d D9: the walker's results printed with exactly the 30 fields of <c>SimTrade</c> schema 2 (and <c>schema=2</c> written here, not read from the trade) and the 20 counters of record 2, and nothing
/// else. The frozen sim-2 files (<c>Golden/*.sim-2.*</c>) were made with this text under the sim-2 code before the first change of the slice and are never regenerated; the same text over the sim-3 code proves that an intraday
/// trade did not change by one byte (the two appended fields are 1 and 0 and are checked apart).
/// </summary>
internal static class Schema2View
{
    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

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
                $"trade schema=2 strategy={t.StrategyId} instrument={t.InstrumentId} symbol={t.Symbol} side={t.Side} date={t.TradeDate:yyyy-MM-dd} decision={t.DecisionEpochSeconds} ")
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

    /// <summary>The F-days and the W-rows, as <c>walker.golden.txt</c> holds them (without its first line).</summary>
    public static string Walker()
    {
        var text = new StringBuilder();
        var total = WalkCounters.Empty;
        foreach (var walkCase in Cases.All)
        {
            for (var i = 0; i < walkCase.Items.Count; i++)
            {
                var result = WalkAssert.Walk(walkCase, walkCase.Items[i]);
                text.Append(walkCase.Name).Append(" #").Append(i).Append(' ').Append(Render(result)).Append('\n');
                total = total.Add(result.Value.ToCounters());
            }
        }

        return text.Append("total ").Append(Render(total)).Append('\n').ToString();
    }

    /// <summary>H1 to H25 with the doc 16 slippage and again with none: one line per walk.</summary>
    public static string HRows()
    {
        var text = new StringBuilder();
        foreach (var hCase in HandCases.All)
        {
            foreach (var (label, fills) in new[] { ("modelled", FillRules.Default), ("exact", Cand.NoSlippage) })
            {
                text.Append(hCase.Hand.Name).Append(' ').Append(label).Append(' ').Append(Render(Result.Success(hCase.Hand.Walk(fills)))).Append('\n');
            }
        }

        return text.ToString();
    }

    /// <summary>The text of one seeded intraday day: its walks and the sum of their counters.</summary>
    public static string SeededDay(SeededDay day)
    {
        var text = new StringBuilder().Append("seed ").Append(day.Seed).Append('\n');
        var counters = WalkCounters.Empty;
        foreach (var walk in day.Walks)
        {
            var result = TradeWalker.Walk(walk.Candidate, day.Bars, walk.Rules);
            text.Append(Render(result)).Append('\n');
            counters = counters.Add(result.Value.ToCounters());
        }

        return text.Append(Render(counters)).Append('\n').ToString();
    }

    /// <summary>One line per batch of 50 seeded intraday days: <c>seeds 1-50 &lt;sha256&gt;</c> over the text of the days of the batch.</summary>
    public static string SeededBatches()
    {
        var lines = new StringBuilder();
        for (var first = 1; first <= SeededDays.Count; first += 50)
        {
            var text = new StringBuilder();
            foreach (var day in SeededDays.All.Where(d => d.Seed >= first && d.Seed < first + 50))
            {
                text.Append(SeededDay(day));
            }

            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant();
            lines.Append(CultureInfo.InvariantCulture, $"seeds {first}-{first + 49} {hash}\n");
        }

        return lines.ToString();
    }
}
