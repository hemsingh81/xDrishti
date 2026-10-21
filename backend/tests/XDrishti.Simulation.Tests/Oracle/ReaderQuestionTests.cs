using static XDrishti.Simulation.Tests.Oracle.Fx;

namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>
/// The places where the oracle's author wrote that the text has two readings (Q1, Q2, Q3, Q10, Q11 of its QUESTIONS file) and no hand row of the specs tells them apart, put to both sides on small series built to separate them: the oracle
/// and the engine must give the same answer, and the answer is written here so that it is pinned (the owner decides which reading stands; this test says which one the code reads today). Each is a log row of the family "agreed, ambiguous".
/// </summary>
public sealed class ReaderQuestionTests
{
    private static (OracleResult Oracle, OracleResult Engine) Both(Tape tape, OracleCandidate candidate, OracleRules rules)
    {
        OracleBar[] bars = tape.ToArray();
        var oracle = ReferenceWalker.Walk(bars, candidate, rules, OracleFaults.None);
        var engine = OracleAdapter.Walk(OracleAdapter.Bars(bars), [OracleAdapter.Candidate(candidate)], OracleAdapter.Rules(rules)).Results[0];
        var differences = OracleComparer.Diff(oracle, engine, EngineHandRowTests.Stats, "reader question");
        differences.ShouldBeEmpty(string.Join("; ", differences));
        return (oracle, engine);
    }

    [Fact]
    public void Q1_a_target_whose_minute_opens_beyond_it_counts_no_range_even_when_the_stop_is_not_touched()
    {
        // buy 100.00 at the open, stop 99.00, target 102.00; at 10:30 the minute opens at 102.50 (beyond the target), trades down to 99.50 (the stop is not touched) and closes at 102.00
        Tape t = new Tape().Day(Mon, "09:15", "15:29", 100.00m, [M("10:30", 102.50m, 102.60m, 99.50m, 102.00m)]);
        OracleCandidate c = Cand(t, OracleSide.Buy, Mon, "10:00", 100.00m, 99.00m);

        var (oracle, engine) = Both(t, c, Intraday(OracleSlippage.None));

        foreach (var result in new[] { oracle, engine })
        {
            result.Trade!.ExitReason.ShouldBe(OracleExitReason.Target);
            result.Trade.ExitPrice.ShouldBe(102.00);
            result.Trade.MaeR.ShouldBe(0.0, "reading B: the range of a minute that opened beyond the target counts nowhere (reading A would count its low: 0.5)");
        }
    }

    [Fact]
    public void Q2_a_gap_on_the_third_night_counts_as_an_overnight_gap()
    {
        // swing, hold 5: bought Monday, flat Tuesday, Wednesday opens below the stop (a gap exit at the first minute of Wednesday, two nights after the entry date's next day)
        Tape t = new Tape()
            .Day(Mon, "09:15", "15:29", 100.00m, [M("10:00", 100.00m, 100.30m, 99.80m, 100.20m)])
            .Day(Tue, "09:15", "15:29")
            .Day(Wed, "09:15", "15:29", listed: [M("09:15", 97.00m, 97.20m, 96.80m, 97.00m)]);
        OracleCandidate c = Cand(t, OracleSide.Buy, Mon, "10:00", 100.00m, 99.00m);

        var (oracle, engine) = Both(t, c, Swing(5, slip: OracleSlippage.None));

        foreach (var result in new[] { oracle, engine })
        {
            result.Trade!.ExitReason.ShouldBe(OracleExitReason.Stop);
            result.Counters.StopGapExits.ShouldBe(1);
            result.Counters.OvernightStopGaps.ShouldBe(1, "reading B: the first minute that holds a trade on its own date, for any date later than the entry date (reading A: only the first night)");
        }
    }

    [Fact]
    public void Q3_the_fill_minute_of_an_entry_at_the_limit_that_is_also_the_last_minute_of_the_data_counts_its_close_as_favourable()
    {
        // buy, limit 100.10; the minute 10:00 opens at 100.30 (beyond the limit), trades back to 100.05 (the order fills at the limit) and reached 100.60; the list ends there (EndOfData at the close)
        Tape t = new Tape().Day(Mon, "09:15", "10:00", 100.00m, [M("10:00", 100.30m, 100.60m, 100.05m, 100.20m)]);
        OracleCandidate c = Cand(t, OracleSide.Buy, Mon, "10:00", 100.00m, 99.00m);

        var (oracle, engine) = Both(t, c, Intraday(OracleSlippage.None));

        foreach (var result in new[] { oracle, engine })
        {
            result.Trade!.EntryFill.ShouldBe(OracleEntryFill.AtLimit);
            result.Trade.ExitReason.ShouldBe(OracleExitReason.EndOfData);
            result.Trade.MfeR.ShouldBe(0.1 / 1.1, 1e-12, "sim-6 (ISS-28, decided in P4-01): the favourable extreme reaches at least the close of the last minute, so MFE is not below R; the rest of the fill minute stays uncounted (X5: (100.20 - 100.10) / 1.10)");
            result.Trade.MfeR.ShouldBeGreaterThanOrEqualTo(result.Trade.RExact - 1e-12, "-MAE <= R <= MFE holds in the corner too");
        }
    }

    [Fact]
    public void Q10_a_swing_decision_inside_its_session_at_the_end_of_the_list_is_a_miss_or_a_pending_order()
    {
        // swing, decided at 10:00 on Monday; the list ends with the candidate's own minute (09:59). ADR 0043 D3 reserves OrderPendingAtEnd for an order that rests overnight; the author read an in-session decision as NoFillBar
        Tape t = new Tape().Day(Mon, "09:15", "09:59", 100.00m);
        OracleCandidate c = Cand(t, OracleSide.Buy, Mon, "10:00", 100.00m, 99.00m);
        OracleBar[] bars = t.ToArray();

        var oracle = ReferenceWalker.Walk(bars, c, Swing(5), OracleFaults.None);
        var engine = OracleAdapter.Walk(OracleAdapter.Bars(bars), [OracleAdapter.Candidate(c)], OracleAdapter.Rules(Swing(5))).Results[0];

        // recorded whatever the answer is: a difference here is the finding of the log, not a failure to hide
        engine.NoTrade.ShouldBe(oracle.NoTrade, $"oracle {oracle.NoTrade}, engine {engine.NoTrade}");
        engine.NoTrade.ShouldBe(OracleNoTradeReason.NoFillBar, "an in-session decision at the end of the list is a NoFillBar on both sides (ADR 0043 D3 keeps OrderPendingAtEnd for an order that rests overnight)");
    }
}
