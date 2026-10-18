using System.Globalization;
using System.Text;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>
/// A canonical text of the run record, every field of it, doubles in the round-trip form with negative zero written 0 (<see cref="CanonicalText"/>), lists in their defined order (universe order, date order, path order): two records are
/// equal when their texts are, and the golden record of criterion 13 is this text. Nothing here decides anything.
/// </summary>
internal static class RunText
{
    private static readonly System.Text.RegularExpressions.Regex NegativeZero = new(@"(?<![\w])-0(?![\d.])", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>Whether the text holds a negative zero as a number (a date such as 2026-03-02 does not).</summary>
    public static bool HasNegativeZero(string text) => NegativeZero.IsMatch(text);

    private static string N(double value) => CanonicalText.Number(value);

    private static string N(double? value) => value is { } v ? N(v) : "null";

    private static string D(DateOnly? date) => date is { } d ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "null";

    private static string I(long value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Trade(SimTrade t) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"trade schema={t.SchemaVersion} strategy={t.StrategyId} instrument={t.InstrumentId:D} symbol={t.Symbol} side={t.Side} date={D(t.TradeDate)} decision={t.DecisionEpochSeconds} tf={t.TriggerTimeframe.ToCode()} "
            + $"bar={t.TriggerBar.EpochSeconds}/{N(t.TriggerBar.Open)}/{N(t.TriggerBar.High)}/{N(t.TriggerBar.Low)}/{N(t.TriggerBar.Close)}/{t.TriggerBar.Volume}/{t.TriggerBarMinutes}/{t.TriggerBarComplete} "
            + $"entry={t.EntryEpochSeconds}@{N(t.EntryPrice)} stop={N(t.StopPrice)} target={N(t.TargetPrice)} exit={t.ExitEpochSeconds}@{N(t.ExitPrice)} reason={t.ExitReason} "
            + $"rExact={N(t.RExact)} mae={N(t.MaeR)} mfe={N(t.MfeR)} fills={t.Fills} costs={t.Costs} snapshot={t.EntrySnapshotJson ?? "null"} rFilled={N(t.RFilled)} slippageR={N(t.SlippageR)} "
            + $"entryFilled={N(t.EntryFilledPrice)} exitFilled={N(t.ExitFilledPrice)} entryFill={t.EntryFill} tickFallback={t.TickFallback} sessionsHeld={t.SessionsHeld} sessionsUnseen={t.SessionsUnseen}")
        + (t.Costing is { } costing ? " costing=" + Costing(costing) : string.Empty);

    /// <summary>The costing of a trade, every figure and every line of both legs (money by <see cref="CanonicalText.Number(decimal)"/>).</summary>
    public static string Costing(TradeCosting c) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"segment={c.Segment} sheet={c.SheetId} qty={c.Quantity} riskTaken={M(c.RiskTaken)} entryTurnover={M(c.EntryTurnover)} exitTurnover={M(c.ExitTurnover)} entryLeg[{Leg(c.EntryLeg)}] exitLeg[{Leg(c.ExitLeg)}] total={M(c.TotalCost)} costR={N(c.CostR)} rNet={N(c.RNet)}");

    /// <summary>The eight lines of a leg.</summary>
    public static string Leg(LegCharges l) =>
        string.Create(CultureInfo.InvariantCulture, $"brokerage={M(l.Brokerage)} stt={M(l.Stt)} exchange={M(l.ExchangeTxn)} sebi={M(l.SebiFee)} stamp={M(l.StampDuty)} other={M(l.OtherCharges)} dp={M(l.DpCharge)} gst={M(l.Gst)}");

    private static string M(decimal value) => CanonicalText.Number(value);

    public static string Instrument(InstrumentRun r)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"instrument {r.InstrumentId:D} {r.Symbol} skipped={(r.Skipped is { } s ? s.ToCode() : "-")} detail={r.SkipDetail ?? "-"}\n");
        text.Append(CultureInfo.InvariantCulture, $"  data first={D(r.FirstBar)} last={D(r.LastBar)} replayFrom={D(r.ReplayFrom)} cover={(r.CountersCover is { } c ? D(c.First) + ".." + D(c.Last) : "-")}\n");
        text.Append(CultureInfo.InvariantCulture, $"  bars read={r.BarsRead} fed={r.BarsFed} excluded={r.BarsExcluded} outside={r.BarsOutsideSession} sha256={r.BarsSha256 ?? "-"}\n");
        text.Append(CultureInfo.InvariantCulture, $"  tick={(r.TickSize is { } t ? CanonicalText.Number(t) : "-")} fallback={r.TickFromFallback}\n");
        foreach (var a in r.Adjustments)
        {
            text.Append(CultureInfo.InvariantCulture, $"  adjustment {D(a.ExDate)} {a.Kind} {a.Numerator}:{a.Denominator} factor={CanonicalText.Number(a.Factor)} onCsv={a.OnCsvBars}\n");
        }

        var k = r.Sessions;
        text.Append(CultureInfo.InvariantCulture, $"  sessions ran={k.Ran} closed={k.ClosedDay} special={k.SpecialSession} short={k.ShortSession} missing={k.MissingSession}\n");
        foreach (var n in r.Notes)
        {
            text.Append(CultureInfo.InvariantCulture, $"  note {D(n.Date)} {n.Reason.ToCode()} bars={n.BarsInWindow} expected={n.ExpectedMinutes}\n");
        }

        text.Append(CultureInfo.InvariantCulture, $"  notesTruncated={r.NotesTruncated}\n");
        foreach (var d in r.Discontinuities)
        {
            text.Append(CultureInfo.InvariantCulture, $"  flagged {D(d.Date)} ratio={N(d.Ratio)} excludedPositions={d.ExcludedPositions}\n");
        }

        foreach (var scan in r.Scans)
        {
            text.Append("  ").Append(Render.Counters(scan).Replace("\n", "\n  ", StringComparison.Ordinal)).Append('\n');
        }

        text.Append("  walk ").Append(OutcomeFormatter.Render(r.Walk)).Append('\n');
        text.Append("  ").Append(Fates(r.Fates)).Append('\n');
        text.Append(CultureInfo.InvariantCulture, $"  inWindow candidates={r.CandidatesInWindow} trades={r.TradesInWindow}\n");
        return text.ToString();
    }

    public static string Fates(RunFates f) =>
        string.Create(CultureInfo.InvariantCulture, $"fates suppressed={f.EntriesSuppressed} beforeWindow={f.TradesBeforeWindow} openAtEnd={f.OpenAtEnd} trades={f.Trades} noFillBar={f.NoFillBar} afterSquareOff={f.AfterSquareOff} entryThroughStop={f.EntryThroughStop} limitNotReached={f.LimitNotReached} excludedAcrossDiscontinuity={f.ExcludedAcrossDiscontinuity} positionOpen={f.PositionOpen} skippedQuantity={f.SkippedQuantity} skippedCost={f.SkippedCost} orderPendingAtEnd={f.OrderPendingAtEnd}");

    public static string Replay(InstrumentReplay replay)
    {
        var text = new StringBuilder(Instrument(replay.Record));
        foreach (var t in replay.Trades)
        {
            text.Append("trades ").Append(Trade(t)).Append('\n');
        }

        foreach (var t in replay.OpenAtEnd)
        {
            text.Append("openAtEnd ").Append(Trade(t)).Append('\n');
        }

        return text.ToString();
    }

    public static string Totals(RunCounters t)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"totals instruments={t.Instruments} run={t.InstrumentsRun} skipped=[{string.Join(",", t.Skipped.Select(s => s.Reason.ToCode() + ":" + s.Instruments))}]\n");
        text.Append(CultureInfo.InvariantCulture, $"  bars read={t.BarsRead} fed={t.BarsFed} excluded={t.BarsExcluded} outside={t.BarsOutsideSession} flagged={t.DiscontinuitiesFlagged}\n");
        text.Append(CultureInfo.InvariantCulture, $"  sessions ran={t.Sessions.Ran} closed={t.Sessions.ClosedDay} special={t.Sessions.SpecialSession} short={t.Sessions.ShortSession} missing={t.Sessions.MissingSession}\n");
        foreach (var scan in t.Scans)
        {
            text.Append("  ").Append(Render.Counters(scan).Replace("\n", "\n  ", StringComparison.Ordinal)).Append('\n');
        }

        text.Append("  walk ").Append(OutcomeFormatter.Render(t.Walk)).Append('\n');
        text.Append("  ").Append(Fates(t.Fates)).Append('\n');
        text.Append(CultureInfo.InvariantCulture, $"  inWindow candidates={t.CandidatesInWindow} trades={t.TradesInWindow}\n");
        return text.ToString();
    }

    public static string Run(SimulationRun run)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"run schema={run.SchemaVersion} key={run.RunKey} engine={run.EngineVersion}\n");
        var s = run.Strategy;
        text.Append(CultureInfo.InvariantCulture, $"strategy {s.Id} timeframe={s.EvaluationTimeframe.ToCode()} sides=[{string.Join(",", s.Sides)}] params=[{string.Join(",", s.Parameters.Select(p => p.Key + "=" + CanonicalText.Number(p.Value)))}] sha256={s.DefinitionSha256}\n");
        text.Append(CultureInfo.InvariantCulture, $"json {s.DefinitionJson}\n");
        text.Append(CultureInfo.InvariantCulture, $"window {D(run.From)}..{D(run.To)} options={CanonicalText.Number(run.Options.MinSessionCompleteness)},{CanonicalText.Number(run.Options.DiscontinuityThreshold)} ack={run.AcknowledgeCsvAdjusted}\n");
        text.Append(CultureInfo.InvariantCulture, $"fills {run.Fills}\n");
        if (run.Costs is { } costs)
        {
            text.Append(CultureInfo.InvariantCulture, $"costs sheet={costs.SheetId} ratesSha256={costs.RatesSha256} checked={costs.Checked ?? "-"} capital={M(costs.Capital)} profile={costs.Profile.Name}/{M(costs.Profile.RiskPerTradePct)}/{M(costs.Profile.MaxCapitalPerTradePct)} maxCostR={M(costs.MaxCostR)}\n");
            text.Append(CultureInfo.InvariantCulture, $"sheetJson {costs.SheetJson}\n");
        }

        var u = run.Universe;
        text.Append(CultureInfo.InvariantCulture, $"universe kind={u.Kind} basket={(u.BasketId is { } b ? b.ToString("D") : "-")} name={u.BasketName ?? "-"} label={u.Label} members=[{string.Join(",", u.Members.Select(m => m.Id.ToString("D") + ":" + m.Symbol))}]\n");
        foreach (var e in run.CalendarExceptions)
        {
            text.Append(CultureInfo.InvariantCulture, $"calendar {D(e.Date)} {(e.IsHoliday ? "holiday" : e.FromMinute + "-" + e.ToMinute)}\n");
        }

        foreach (var i in run.Instruments)
        {
            text.Append(Instrument(i));
        }

        foreach (var t in run.Trades)
        {
            text.Append(CultureInfo.InvariantCulture, $"trade baskets=[{string.Join(",", t.BasketIds.Select(x => x.ToString("D")))}] {Trade(t.Trade)}\n");
        }

        foreach (var t in run.OpenAtEnd)
        {
            text.Append(CultureInfo.InvariantCulture, $"openAtEnd baskets=[{string.Join(",", t.BasketIds.Select(x => x.ToString("D")))}] {Trade(t.Trade)}\n");
        }

        return text.Append(Totals(run.Totals)).ToString();
    }
}
