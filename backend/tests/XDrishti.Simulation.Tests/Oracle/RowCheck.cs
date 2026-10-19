using System.Globalization;
using System.Reflection;

namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>Compares the oracle's result for a hand row with the copied numbers.</summary>
internal static class RowCheck
{
    internal static decimal R4(double x) => Math.Round((decimal)x, 4, MidpointRounding.AwayFromZero);

    private static long At(string s) => Fx.E(s[..10], s[11..]);

    /// <summary>The rules of a row in a fill mode: the exact mode runs the row with no slippage.</summary>
    internal static OracleRules RulesOf(HandRow row, bool exactMode) =>
        exactMode ? row.Rules with { Slippage = OracleSlippage.None } : row.Rules;

    /// <summary>The oracle's walk of a row against the copied numbers (never with a fault: the fault check walks the rows itself, in <c>FaultTests</c>).</summary>
    internal static List<string> Verify(HandRow row, bool exactMode)
    {
        OracleResult res = ReferenceWalker.Walk(row.Bars, row.Candidate, RulesOf(row, exactMode), OracleFaults.None);
        return Compare(row.Expect, res, exactMode);
    }

    internal static List<string> Compare(Expect x, OracleResult res, bool exactMode)
    {
        var bad = new List<string>();
        if (!x.Trade)
        {
            if (res.Trade is not null)
            {
                bad.Add("expected no trade " + x.NoTrade + " but got a trade");
            }
            else if (res.NoTrade != x.NoTrade)
            {
                bad.Add("no-trade reason expected " + x.NoTrade + " actual " + res.NoTrade);
            }

            CompareCounters(x, res, bad);
            return bad;
        }

        OracleTrade? t = res.Trade;
        if (t is null)
        {
            bad.Add("expected a trade but got " + res.NoTrade);
            return bad;
        }

        void Eq(string name, decimal? expected, double actual)
        {
            if (expected is { } e && R4(actual) != e)
            {
                bad.Add(string.Create(CultureInfo.InvariantCulture, $"{name}: expected {e} actual {R4(actual)}"));
            }
        }

        Eq("Entry", x.Entry, t.EntryPrice);
        Eq("Exit", x.Exit, t.ExitPrice);
        Eq("RExact", x.R, t.RExact);
        Eq("Mae", x.Mae, t.MaeR);
        Eq("Mfe", x.Mfe, t.MfeR);
        if (x.Target is { } tg)
        {
            if (t.TargetPrice is null)
            {
                bad.Add("target expected but null");
            }
            else
            {
                Eq("Target", tg, t.TargetPrice.Value);
            }
        }

        if (x.TargetIsNull && t.TargetPrice is not null)
        {
            bad.Add("target expected null");
        }

        if (x.Reason is { } rs && t.ExitReason != rs)
        {
            bad.Add("reason expected " + rs + " actual " + t.ExitReason);
        }

        if (t.EntryFill != x.Fill)
        {
            bad.Add("entry fill expected " + x.Fill + " actual " + t.EntryFill);
        }

        if (exactMode)
        {
            Eq("EntryFilled", x.Entry, t.EntryFilledPrice);
            Eq("ExitFilled", x.Exit, t.ExitFilledPrice);
            Eq("RFilled", x.R, t.RFilled);
            if (t.SlippageR != 0.0)
            {
                bad.Add("SlippageR expected 0 in exact mode");
            }

            if (t.Fills != OracleFillBasis.Exact)
            {
                bad.Add("Fills expected Exact");
            }
        }
        else
        {
            Eq("EntryFilled", x.EntryF, t.EntryFilledPrice);
            Eq("ExitFilled", x.ExitF, t.ExitFilledPrice);
            Eq("RFilled", x.RF, t.RFilled);
            Eq("SlippageR", x.SlipR, t.SlippageR);
        }

        if (x.EntryAt is { } ea && t.EntryEpochSeconds != At(ea))
        {
            bad.Add("entry time expected " + ea);
        }

        if (x.ExitAt is { } xa && t.ExitEpochSeconds != At(xa))
        {
            bad.Add("exit time expected " + xa);
        }

        if (x.TradeDate is { } td && t.TradeDate != Fx.D(td))
        {
            bad.Add("trade date expected " + td + " actual " + t.TradeDate);
        }

        if (x.CheckCounters && (t.SessionsHeld != x.Held || t.SessionsUnseen != x.Unseen))
        {
            bad.Add($"held/unseen expected {x.Held}/{x.Unseen} actual {t.SessionsHeld}/{t.SessionsUnseen}");
        }

        CompareCounters(x, res, bad);
        return bad;
    }

    private static Dictionary<string, int> ExpectedCounters(Expect x)
    {
        var d = new Dictionary<string, int> { ["Candidates"] = 1 };
        if (x.Trade)
        {
            d["Trades"] = 1;
            d["Exits" + x.Reason] = 1;
            d[x.Fill == OracleEntryFill.AtOpen ? "EntriesAtOpen" : "EntriesAtLimit"] = 1;
            d["SessionsHeld"] = x.Held;
            d["SessionsUnseen"] = x.Unseen;
            if (x.Bars is { } b)
            {
                d["BarsExamined"] = b;
            }
        }
        else
        {
            d[x.NoTrade!.Value.ToString()] = 1;
            d["BarsExamined"] = x.NoTrade is OracleNoTradeReason.LimitNotReached or OracleNoTradeReason.EntryThroughStop ? 1 : 0;
        }

        foreach ((string k, int v) in x.Extra)
        {
            d[k] = v;
        }

        return d;
    }

    private static void CompareCounters(Expect x, OracleResult res, List<string> bad)
    {
        if (!x.CheckCounters)
        {
            return;
        }

        Dictionary<string, int> want = ExpectedCounters(x);
        foreach (PropertyInfo p in typeof(OracleCounters).GetProperties())
        {
            int actual = (int)p.GetValue(res.Counters)!;
            if (p.Name == "BarsExamined" && !want.ContainsKey(p.Name))
            {
                continue;
            }

            int expected = want.GetValueOrDefault(p.Name);
            if (actual != expected)
            {
                bad.Add($"counter {p.Name}: expected {expected} actual {actual}");
            }
        }
    }
}
