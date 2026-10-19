using static XDrishti.Simulation.Tests.Oracle.Fx;

namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>
/// The regression rows of the step-1 log entries D-01 to D-03 (class A: the rules of ADR 0047 R5, R1 and R9 that close the gaps of the dry read), compared on the oracle and on the engine. The expected numbers in the assertions are worked out by
/// hand from the ADR sentence; the oracle and the engine must each equal them and then each other.
/// </summary>
public sealed class GapRowsComparisonTests
{
    private static (OracleResult Oracle, OracleResult Engine) Both(Tape tape, OracleCandidate candidate, OracleRules rules)
    {
        OracleBar[] bars = tape.ToArray();
        var oracle = ReferenceWalker.Walk(bars, candidate, rules, OracleFaults.None);
        var engine = OracleAdapter.Walk(OracleAdapter.Bars(bars), [OracleAdapter.Candidate(candidate)], OracleAdapter.Rules(rules)).Results[0];
        OracleComparer.Diff(oracle, engine, EngineHandRowTests.Stats, "gap row").ShouldBeEmpty();
        return (oracle, engine);
    }

    [Fact]
    public void D_01_a_target_with_a_ninth_decimal_is_not_rounded_and_the_reward_is_exactly_k()
    {
        // k = 1.123456789 becomes 1.12345679 (R1); risk 0.05; target = 100.00 + 1.12345679 x 0.05 = 100.0561728395 (ten places, kept)
        Tape t = new Tape().Day(Mon, "09:15", "15:29", 100.00m, [M("10:30", 100.00m, 100.10m, 99.96m, 100.06m)]);
        OracleCandidate c = Cand(t, OracleSide.Buy, Mon, "10:00", 100.00m, 99.95m);

        var (oracle, engine) = Both(t, c, Intraday(OracleSlippage.None, 0.01m, 1.123456789));

        foreach (var result in new[] { oracle, engine })
        {
            result.Trade!.TargetPrice.ShouldBe(100.0561728395);
            result.Trade.ExitPrice.ShouldBe(100.0561728395);
            result.Trade.ExitReason.ShouldBe(OracleExitReason.Target);
            result.Trade.RExact.ShouldBe(1.12345679);
        }
    }

    [Fact]
    public void D_02_a_price_of_ten_million_or_more_keeps_fifteen_significant_digits_before_the_rounding_to_eight_places()
    {
        // the open 12,345,678.12345678 becomes 12,345,678.1234568 (R1: the platform conversion keeps 15 significant digits); the entry is that open
        Tape t = new Tape().Day(Mon, "09:15", "15:29", 12345600.00m, [M("10:00", 12345678.12345678m, 12345800.00m, 12345600.00m, 12345700.00m)]);
        OracleCandidate c = Cand(t, OracleSide.Buy, Mon, "10:00", 12345600.00m, 12345000.00m);

        var (oracle, engine) = Both(t, c, Intraday(OracleSlippage.None));

        foreach (var result in new[] { oracle, engine })
        {
            result.Trade.ShouldNotBeNull();
            result.Trade.EntryPrice.ShouldBe(12345678.1234568);
            result.Trade.EntryFill.ShouldBe(OracleEntryFill.AtOpen);
        }
    }

    [Fact]
    public void D_03_a_no_fill_bar_counts_the_zero_volume_minutes_the_search_passed_and_not_the_ones_beyond_it()
    {
        // decision 09:45, the minutes 09:45 to 09:50 hold no trade (six, all within five minutes of the decision instant) and 09:51 holds one: past the window, NoFillBar; the seventh empty minute (09:51 made empty too) is not counted
        var empty = Enumerable.Range(Mi("09:45"), 8).Select(m => V0($"{m / 60:00}:{m % 60:00}", 100.50m, 100.50m, 100.50m, 100.50m)).ToArray();
        Tape t = new Tape().Day(Mon, "09:15", "15:29", 100.50m, empty);
        OracleCandidate c = Cand(t, OracleSide.Buy, Mon, "09:45", 100.50m, 98.00m);

        var (oracle, engine) = Both(t, c, Intraday(OracleSlippage.None));

        foreach (var result in new[] { oracle, engine })
        {
            result.NoTrade.ShouldBe(OracleNoTradeReason.NoFillBar);
            result.Counters.ZeroVolumeMinutes.ShouldBe(6, "the minutes 09:45 to 09:50 are passed over; 09:51 is outside the five-minute window and ends the search without a count");
        }
    }
}
