using System.Globalization;

namespace XDrishti.Domain.LiveTrading;

/// <summary>
/// The evidence gates G1 to G7 for leaving a step (spec P11-04, ADR 0151), pure. Execution, discipline, containment and capture only: never profit. A gate is <c>pass</c>, <c>fail</c>, <c>insufficient</c>, <c>unavailable</c> or <c>not_evaluable</c>;
/// the step rises only when G1 to G6 all pass. G7 is information and never blocks.
/// </summary>
public static class RampGates
{
    public static IReadOnlyList<RampGate> Evaluate(RampEvidence e, int step, RampThresholds t)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(t);
        return [Sessions(e, t), Volume(e, step, t), Slippage(e, t), Discipline(e, t), Containment(e, t), Capture(e, t), CostDrag(e, t)];
    }

    /// <summary>All of G1 to G6 pass.</summary>
    public static bool Eligible(IReadOnlyList<RampGate> gates) => gates.Where(g => g.Code != RampGate.CostDrag).All(g => g.Passes) && gates.Count(g => g.Code != RampGate.CostDrag) == 6;

    private static RampGate Sessions(RampEvidence e, RampThresholds t) =>
        e.Sessions >= t.MinSessions
            ? new(RampGate.Sessions, GateStates.Pass, e.Sessions, t.MinSessions, e.Sessions, $"{e.Sessions} sessions at this step")
            : new(RampGate.Sessions, GateStates.Insufficient, e.Sessions, t.MinSessions, e.Sessions, $"{e.Sessions} of {t.MinSessions} sessions at this step");

    private static RampGate Volume(RampEvidence e, int step, RampThresholds t)
    {
        var need = step >= 2 ? t.MinRoundTripsS2 : t.MinRoundTripsS1;
        var graded = e.Trips.Count(x => !x.Uncertain);
        var uncertain = e.Trips.Count - graded;
        if (graded < need)
        {
            return new(RampGate.Volume, GateStates.Insufficient, graded, need, graded, $"{graded} of {need} graded round trips");
        }

        var share = (double)uncertain / e.Trips.Count;
        return share <= t.MaxUncertainShare + 1e-12
            ? new(RampGate.Volume, GateStates.Pass, graded, need, graded, $"{graded} graded round trips; {uncertain} uncertain ({Pct(share)})")
            : new(RampGate.Volume, GateStates.Fail, share, t.MaxUncertainShare, e.Trips.Count, $"{uncertain} of {e.Trips.Count} round trips are uncertain ({Pct(share)}), above {Pct(t.MaxUncertainShare)}");
    }

    private static RampGate Slippage(RampEvidence e, RampThresholds t)
    {
        var n = e.SlippageR.Count;
        if (n < t.MinSlippageN)
        {
            return new(RampGate.Slippage, GateStates.Insufficient, null, t.MaxSlippageGapR, n, $"{n} of {t.MinSlippageN} measured entries with one reference kind");
        }

        if (OneSidedT.Bounds(e.SlippageR) is not { } b)
        {
            return new(RampGate.Slippage, GateStates.Unavailable, null, t.MaxSlippageGapR, n, "a slippage figure is not a finite number");
        }

        if (b.Mean > t.MaxSlippageGapR + 1e-12)
        {
            return new(RampGate.Slippage, GateStates.Fail, b.Mean, t.MaxSlippageGapR, n, $"mean slippage {R(b.Mean)} R is above {R(t.MaxSlippageGapR)} R");
        }

        return b.Upper <= t.MaxSlippageUpperR + 1e-12
            ? new(RampGate.Slippage, GateStates.Pass, b.Mean, t.MaxSlippageGapR, n, $"mean {R(b.Mean)} R, upper bound {R(b.Upper)} R (limit {R(t.MaxSlippageUpperR)} R)")
            : new(RampGate.Slippage, GateStates.Insufficient, b.Mean, t.MaxSlippageGapR, n, $"mean {R(b.Mean)} R but the upper bound {R(b.Upper)} R is above {R(t.MaxSlippageUpperR)} R: not demonstrated");
    }

    private static RampGate Discipline(RampEvidence e, RampThresholds t)
    {
        var tickets = e.Trips.Count;
        var all = tickets + e.DiscretionaryTrades;
        if (all < t.MinDisciplineN)
        {
            return new(RampGate.Discipline, GateStates.Insufficient, null, t.MaxDiscretionaryShare, all, $"{all} of {t.MinDisciplineN} trades to judge discipline on");
        }

        var discretionary = (double)e.DiscretionaryTrades / all;
        var failures = new List<string>();
        if (discretionary > t.MaxDiscretionaryShare + 1e-12)
        {
            failures.Add($"discretionary share {Pct(discretionary)} above {Pct(t.MaxDiscretionaryShare)}");
        }

        if (e.OverAllocations > 0)
        {
            failures.Add($"{e.OverAllocations} over-allocated entries");
        }

        var chased = e.Entries == 0 ? 0 : (double)e.ChasedEntries / e.Entries;
        if (chased > t.MaxChasedShare + 1e-12)
        {
            failures.Add($"chased entries {Pct(chased)} above {Pct(t.MaxChasedShare)}");
        }

        if (failures.Count > 0)
        {
            return new(RampGate.Discipline, GateStates.Fail, discretionary, t.MaxDiscretionaryShare, all, string.Join("; ", failures));
        }

        if (e.IntradayEntries > 0)
        {
            if (e.ProtectionUnavailable > 0)
            {
                return new(RampGate.Discipline, GateStates.Unavailable, discretionary, t.MaxDiscretionaryShare, all, $"{e.ProtectionUnavailable} intraday entries have no order evidence, so their stop cannot be judged");
            }

            var protectedShare = (double)e.ProtectedIntraday / e.IntradayEntries;
            if (protectedShare < t.MinProtectedShare - 1e-12)
            {
                return new(RampGate.Discipline, GateStates.Fail, protectedShare, t.MinProtectedShare, e.IntradayEntries, $"protected share of intraday entries {Pct(protectedShare)} below {Pct(t.MinProtectedShare)}");
            }
        }

        return new(RampGate.Discipline, GateStates.Pass, discretionary, t.MaxDiscretionaryShare, all, $"discretionary {Pct(discretionary)}, chased {Pct(chased)}, no over-allocation; {e.ProtectionUnknown} delivery entries excluded (stop not readable)");
    }

    private static RampGate Containment(RampEvidence e, RampThresholds t)
    {
        if (e.AfterHaltFills > 0)
        {
            return new(RampGate.Containment, GateStates.Fail, e.AfterHaltFills, 0, null, $"{e.AfterHaltFills} entries were filled after a halt was open");
        }

        if (e.DailyHalts > t.MaxDailyHalts)
        {
            return new(RampGate.Containment, GateStates.Fail, e.DailyHalts, t.MaxDailyHalts, null, $"{e.DailyHalts} daily halts at this step, more than {t.MaxDailyHalts}");
        }

        if (e.DrawdownR is not { } d)
        {
            return e.Trips.Count == 0
                ? new(RampGate.Containment, GateStates.Insufficient, null, t.MaxDrawdownR, 0, "no closed round trip yet")
                : new(RampGate.Containment, GateStates.Unavailable, null, t.MaxDrawdownR, e.Trips.Count, "no closed round trip has a fixed R");
        }

        return d <= t.MaxDrawdownR + 1e-12
            ? new(RampGate.Containment, GateStates.Pass, d, t.MaxDrawdownR, e.Trips.Count, $"drawdown {R(d)} R, no fill after a halt, {e.DailyHalts} daily halts")
            : new(RampGate.Containment, GateStates.Fail, d, t.MaxDrawdownR, e.Trips.Count, $"drawdown {R(d)} R above {R(t.MaxDrawdownR)} R");
    }

    private static RampGate Capture(RampEvidence e, RampThresholds t)
    {
        if (e.CaptureSessions == 0)
        {
            return new(RampGate.Capture, GateStates.Unavailable, null, t.MinCaptureShare, 0, "no session of this step has a capture run");
        }

        var share = (double)e.HealthySessions / e.CaptureSessions;
        var failures = new List<string>();
        if (share < t.MinCaptureShare - 1e-12)
        {
            failures.Add($"capture healthy on {Pct(share)} of sessions, below {Pct(t.MinCaptureShare)}");
        }

        if (e.GapLast5 > 0)
        {
            failures.Add($"a gap in the last 5 sessions ({e.GapLast5})");
        }

        if (e.HistoryDisagreementsLast10 > 0)
        {
            failures.Add($"{e.HistoryDisagreementsLast10} history reads in the last 10 sessions did not agree with the trade book");
        }

        return failures.Count > 0
            ? new(RampGate.Capture, GateStates.Fail, share, t.MinCaptureShare, e.CaptureSessions, string.Join("; ", failures))
            : new(RampGate.Capture, GateStates.Pass, share, t.MinCaptureShare, e.CaptureSessions, $"capture healthy on {Pct(share)} of {e.CaptureSessions} sessions, no gap in the last 5");
    }

    private static RampGate CostDrag(RampEvidence e, RampThresholds t)
    {
        var shares = e.Trips.Where(x => x.ChargesShare is not null).Select(x => x.ChargesShare!.Value).Order().ToList();
        if (shares.Count == 0)
        {
            return new(RampGate.CostDrag, GateStates.Unavailable, null, t.CostDragWarnShare, 0, "no round trip has recorded charges (intraday fills have none)");
        }

        var median = shares.Count % 2 == 1 ? shares[shares.Count / 2] : (shares[(shares.Count / 2) - 1] + shares[shares.Count / 2]) / 2;
        var warn = median > t.CostDragWarnShare ? " - R is too small for the fixed costs: raise capital or risk, do not read net R" : string.Empty;
        return new(RampGate.CostDrag, GateStates.Info, median, t.CostDragWarnShare, shares.Count, $"median charges {Pct(median)} of 1 R{warn}");
    }

    private static string Pct(double v) => (v * 100).ToString("0.#", CultureInfo.InvariantCulture) + " %";

    private static string R(double v) => v.ToString("0.000", CultureInfo.InvariantCulture);
}
