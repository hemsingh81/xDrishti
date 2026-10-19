namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>Compares the reference with one worked example, line by line; returns the differences.</summary>
internal static class CostCheck
{
    internal static List<string> Mismatches(CostExample e)
    {
        OracleCostResult r;
        try
        {
            r = ReferenceCosting.Cost(e.Input, TestSheet.Sheet, e.Sizing);
        }
        catch (InvalidOperationException ex)
        {
            return [ex.Message];
        }

        return Compare(e, r);
    }

    /// <summary>The differences between a result (the reference's, faulted or not) and the printed numbers of the example. The faults are switched on by <c>CostFaultTests</c> only, which calls the reference itself.</summary>
    internal static List<string> Compare(CostExample e, OracleCostResult r)
    {
        var d = new List<string>();
        Eq(d, "skip", r.Skip, e.ExpectedSkip);
        Eq(d, "quantity", r.Quantity, e.ExpectedQuantity);
        Opt(d, "riskTaken", r.RiskTaken, e.RiskTaken);
        Opt(d, "entryTurnover", r.EntryTurnover, e.EntryTurnover);
        Opt(d, "exitTurnover", r.ExitTurnover, e.ExitTurnover);
        Leg(d, "entry", r.EntryLeg, e.Entry);
        Leg(d, "exit", r.ExitLeg, e.Exit);
        if (e.TotalCost is { } total)
        {
            Eq(d, "total", r.TotalCost, total);
        }

        if (e.CostR is { } costR && Math.Abs(r.CostR - costR) > e.CostRTolerance)
        {
            d.Add($"costR {r.CostR} != {costR}");
        }

        if (e.RNet is { } net)
        {
            if (r.RNet is not { } got || Math.Abs(got - net) > e.RNetTolerance)
            {
                d.Add($"rNet {r.RNet} != {net}");
            }
        }
        else if (e.ExpectedSkip != OracleCostSkip.None && r.RNet is not null)
        {
            d.Add("rNet present after a skip");
        }

        return d;
    }

    private static void Eq<T>(List<string> d, string name, T got, T want)
    {
        if (!EqualityComparer<T>.Default.Equals(got, want))
        {
            d.Add($"{name} {got} != {want}");
        }
    }

    private static void Opt(List<string> d, string name, decimal got, decimal? want)
    {
        if (want is { } w)
        {
            Eq(d, name, got, w);
        }
    }

    private static void Leg(List<string> d, string name, OracleLegLines? got, ExpectedLeg? want)
    {
        if (want is null)
        {
            return;
        }

        if (got is null)
        {
            d.Add(name + " leg missing");
            return;
        }

        Eq(d, name + ".brokerage", got.Brokerage, want.Brokerage);
        Eq(d, name + ".stt", got.Stt, want.Stt);
        Eq(d, name + ".exchange", got.Exchange, want.Exchange);
        Eq(d, name + ".sebi", got.Sebi, want.Sebi);
        Eq(d, name + ".stamp", got.Stamp, want.Stamp);
        Eq(d, name + ".other", got.Other, want.Other);
        Eq(d, name + ".dp", got.Dp, want.Dp);
        Eq(d, name + ".gst", got.Gst, want.Gst);
        Eq(d, name + ".total", got.Total, want.Total);
    }
}
