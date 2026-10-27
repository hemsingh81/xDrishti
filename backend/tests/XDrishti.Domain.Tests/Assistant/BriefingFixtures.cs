using XDrishti.Domain.Assistant.Text;

namespace XDrishti.Domain.Tests.Assistant;

/// <summary>The fixture plan of P7-09 as briefing facts: primary AAA and BBB, reserve CCC and DDD (spec P9-06 AC 1).</summary>
internal static class BriefingFixtures
{
    public static BriefingFacts Daily(bool provisional = true)
    {
        var lines = new List<FactLine>
        {
            new(string.Empty, "plan", "plan", "Plan for 2026-10-28 is published with 2 primary and 2 reserve tickets; 2 candidates were rejected."),
        };
        if (provisional)
        {
            lines.Add(new FactLine(string.Empty, "plan", "provisional", "The plan is provisional: its cells are not paper-proven."));
        }

        lines.AddRange(Ticket("AAA", "BUY", "primary", "100.00", "98.50", "104.00", "2.67", 61, "58.3", "0.34"));
        lines.AddRange(Ticket("BBB", "SELL", "primary", "202.00", "205.00", "196.00", "2.00", 70, "55.1", "0.21"));
        lines.AddRange(Ticket("CCC", "BUY", "reserve", "50.50", "49.00", "53.00", "1.67", 33, "52.0", "0.10"));
        lines.AddRange(Ticket("DDD", "SELL", "reserve", "75.00", "77.00", "72.00", "1.50", 40, "51.2", "0.05"));
        lines.Add(new FactLine(string.Empty, "risk", "risk", "Basis: costed_unconfirmed, gross costs were assumed."));
        lines.Add(new FactLine(string.Empty, "risk", "risk", "Gate reduced the risk to 0.50 of normal."));
        lines.Add(new FactLine(string.Empty, "note", "regime", "Regime is unavailable: market context is not stored.", FactLine.Unavailable));
        lines.Add(new FactLine(string.Empty, "note", "reference", "Reference value 45.83."));
        lines.Add(new FactLine("AAA", "note", "loss", "AAA lost -0.30R last week."));
        return new BriefingFacts(BriefingFacts.Daily, "2026-10-28", provisional, ["AAA", "BBB", "CCC", "DDD"], lines, 42, null, []);
    }

    public static BriefingFacts Weekly()
    {
        var lines = new List<FactLine>
        {
            new(string.Empty, "plan", "review", "Weekly review for 2026-W43, built from the stored digest and the report insights."),
            new(string.Empty, "plan", "plan outcomes", "5 plans were graded; 12 tickets hit the target and 9 the stop."),
            new(string.Empty, "insight", "best strategy", "Best strategy: orb_15 0.31R per trade over 143 trades (win rate 52.0%)."),
            new(string.Empty, "insight", "regime", "Regime: unavailable, no market context is stored.", FactLine.Unavailable),
            new(string.Empty, "digest", "cells", "3 cells changed this week."),
            new(string.Empty, "digest", "limits", "Data is simulated on plan prices."),
        };
        return new BriefingFacts(BriefingFacts.Weekly, "2026-W43", false, [], lines, null, 7, ["RUN1"]);
    }

    private static IEnumerable<FactLine> Ticket(string symbol, string side, string group, string price, string stop, string target, string rr, int trades, string pWin, string expectancy)
    {
        yield return new FactLine(symbol, "ticket", "ticket", $"{symbol} {side} {group} ticket, strategy orb_15.");
        yield return new FactLine(symbol, "ticketdetail", "levels", $"{symbol} reference price {price}, stop {stop}, target {target}, reward-to-risk {rr}, indicative from the close of the session; not tomorrow's price.");
        yield return new FactLine(symbol, "ticketdetail", "evidence", $"{symbol} cell NIFTY50:orb_15 has {trades} trades, pWin {pWin}% (shrunk estimate), conservative expectancy {expectancy}R.");
    }
}
