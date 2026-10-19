using static XDrishti.Simulation.Tests.Oracle.Fx;

namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>Rows G1, G2, G2b, G3 of spec P3-03d and row O9 of spec P3-03e, through the reference gate.</summary>
public sealed class GateTests
{
    private static Mn A => M("10:00", 100.00m, 100.30m, 99.80m, 100.20m);

    private static OracleGateResult Gate(Tape t, OracleRules rules, params OracleCandidate[] cands)
    {
        OracleBar[] bars = t.ToArray();
        OracleGateResult oracle = ReferenceGate.Run(cands, c => ReferenceWalker.Walk(bars, c, rules, OracleFaults.None));

        // E: the engine's gate on the same candidates and minutes gives the same blocked set, hold ends and walks (the author's expectations below are on the oracle's result)
        EngineGate.ShouldAgree(oracle, bars, cands, _ => rules);
        return oracle;
    }

    private static Mn[] ZeroTail(string from, string to, decimal price)
    {
        var list = new List<Mn>();
        for (int m = Mi(from); m <= Mi(to); m++)
        {
            list.Add(V0($"{m / 60:00}:{m % 60:00}", price, price, price, price));
        }

        return [.. list];
    }

    [Fact]
    public void G1_two_candidates_are_blocked_one_on_each_side_of_the_hold_end()
    {
        Tape t = new Tape()
            .Day(Mon, "09:15", "15:29", 100.00m, [M("10:15", 100.00m, 100.30m, 99.80m, 100.20m)])
            .Day(Tue, "09:15", "15:29")
            .Day(Wed, "09:15", "15:29", listed: [M("10:15", 99.40m, 99.50m, 98.90m, 99.00m)]);
        OracleCandidate c1 = Cand(t, OracleSide.Buy, Mon, "10:15", 100.00m, 99.00m);
        OracleCandidate c2 = Cand(t, OracleSide.Buy, Tue, "10:15", 100.20m, 99.00m);
        OracleCandidate c3 = Cand(t, OracleSide.Buy, Wed, "10:15", 100.20m, 99.00m);
        OracleCandidate c4 = Cand(t, OracleSide.Buy, Wed, "10:45", 99.00m, 98.00m);
        OracleGateResult g = Gate(t, Swing(5), c1, c2, c3, c4);

        g.Entries[0].Result.Trade!.ExitReason.ShouldBe(OracleExitReason.Stop);
        g.Entries[0].HoldEndEpochSeconds.ShouldBe(E(Wed, "10:15"));
        g.Entries[1].Blocked.ShouldBeTrue();
        g.Entries[2].Blocked.ShouldBeTrue();
        g.Entries[3].Blocked.ShouldBeFalse();
        g.Entries[3].Result.Trade.ShouldNotBeNull();
        g.BlockedIndexes.ShouldBe([1, 2]);
        g.Counters.Candidates.ShouldBe(4);
        g.Counters.PositionOpen.ShouldBe(2);
        g.Counters.Trades.ShouldBe(2);
    }

    [Fact]
    public void G2_a_candidate_after_an_end_of_data_trade_is_blocked_even_after_its_exit_instant()
    {
        Tape t = new Tape().Day(Mon, "09:15", "15:29", 100.00m, [A]).Day(Tue, "09:15", "15:29");
        OracleCandidate c1 = Cand(t, OracleSide.Buy, Mon, "10:00", 100.00m, 99.00m);
        OracleCandidate c2 = Cand(t, OracleSide.Buy, Tue, "14:45", 100.20m, 99.00m);
        OracleCandidate c3 = Cand(t, OracleSide.Buy, Tue, "15:30", 100.20m, 99.00m);
        OracleGateResult g = Gate(t, Swing(5), c1, c2, c3);

        g.Entries[0].Result.Trade!.ExitReason.ShouldBe(OracleExitReason.EndOfData);
        g.Entries[1].Blocked.ShouldBeTrue();
        g.Entries[2].Blocked.ShouldBeTrue();
        g.Entries[2].Candidate.DecisionEpochSeconds.ShouldBeGreaterThan(g.Entries[0].Result.Trade!.ExitEpochSeconds);
        g.Rows.ShouldContain(OracleRows.D11EndOfData);
        g.Counters.PositionOpen.ShouldBe(2);
    }

    [Fact]
    public void G2b_the_hold_end_of_a_max_hold_at_the_last_close_is_the_end_of_the_due_session()
    {
        Mn[] zeros = ZeroTail("15:11", "15:29", 100.20m);
        Tape Build(bool withThursday)
        {
            Tape t = new Tape().Day(Mon, "09:15", "15:29", 100.00m, [A]).Day(Tue, "09:15", "15:29").Day(Wed, "09:15", "15:29", listed: zeros);
            return withThursday ? t.Day(Thu, "09:15", "15:29") : t;
        }

        Tape lng = Build(true);
        OracleCandidate[] LongCands(Tape t, bool withThursday)
        {
            var list = new List<OracleCandidate>
            {
                Cand(t, OracleSide.Buy, Mon, "10:00", 100.00m, 99.00m),
                Cand(t, OracleSide.Buy, Wed, "15:15", 100.20m, 99.00m),
                Cand(t, OracleSide.Buy, Wed, "15:30", 100.20m, 99.00m),
            };
            if (withThursday)
            {
                list.Add(Cand(t, OracleSide.Buy, Thu, "10:15", 100.20m, 99.00m));
            }

            return [.. list];
        }

        OracleGateResult g = Gate(lng, Swing(3), LongCands(lng, true));
        OracleTrade c1 = g.Entries[0].Result.Trade!;
        c1.ExitReason.ShouldBe(OracleExitReason.MaxHold);
        g.Entries[0].Result.Counters.MaxHoldAtLastClose.ShouldBe(1);
        g.Entries[0].HoldEndEpochSeconds.ShouldBe(E(Wed, "15:30"));
        c1.ExitEpochSeconds.ShouldBe(E(Wed, "15:10"));
        g.Entries[1].Blocked.ShouldBeTrue();
        g.Entries[2].Blocked.ShouldBeTrue();
        g.Entries[3].Blocked.ShouldBeFalse();

        Tape shrt = Build(false);
        OracleGateResult s = Gate(shrt, Swing(3), LongCands(shrt, false));
        s.Entries[0].Result.Trade!.ExitReason.ShouldBe(OracleExitReason.EndOfData);
        s.Entries[1].Blocked.ShouldBeTrue();
        s.Entries[2].Blocked.ShouldBeTrue();

        // The property of D14 2(d): everything blocked in the short run is blocked in the long run.
        for (int i = 0; i < 3; i++)
        {
            if (s.Entries[i].Blocked)
            {
                g.Entries[i].Blocked.ShouldBeTrue();
            }
        }
    }

    [Fact]
    public void G3_a_blocked_candidate_is_position_open_and_not_limit_not_reached()
    {
        Tape t = new Tape().Day(Mon, "09:15", "15:29", 100.00m, [A]).Day(Tue, "09:15", "15:29", listed: [M("10:15", 100.40m, 100.60m, 100.20m, 100.50m)]);
        OracleCandidate c1 = Cand(t, OracleSide.Buy, Mon, "10:00", 100.00m, 99.00m);
        OracleCandidate c2 = Cand(t, OracleSide.Buy, Tue, "10:15", 100.00m, 99.00m);
        OracleGateResult g = Gate(t, Swing(5), c1, c2);

        g.Entries[1].Result.NoTrade.ShouldBe(OracleNoTradeReason.PositionOpen);
        g.Counters.LimitNotReached.ShouldBe(0);
        g.Counters.PositionOpen.ShouldBe(1);
        g.Counters.Candidates.ShouldBe(2);
        ReferenceWalker.Walk(t.ToArray(), c2, Swing(5), OracleFaults.None).NoTrade.ShouldBe(OracleNoTradeReason.LimitNotReached);
    }

    [Fact]
    public void O9_a_candidate_at_15_30_on_the_day_a_position_leaves_at_the_close_is_not_blocked()
    {
        Tape t = new Tape()
            .Day(Mon, "09:15", "15:29", 100.00m, [A])
            .Day(Tue, "09:15", "15:29", listed: [M("09:15", 100.05m, 100.40m, 99.90m, 100.30m)])
            .Day(Wed, "09:15", "15:29", listed: [M("15:29", 100.55m, 100.70m, 100.50m, 100.60m)]);
        OracleBar[] bars = t.ToArray();
        OracleCandidate c1 = Cand(t, OracleSide.Buy, Mon, "10:00", 100.00m, 99.00m);
        OracleCandidate c2 = Cand(t, OracleSide.Buy, Mon, "15:30", 100.00m, 95.00m);
        OracleGateResult g = ReferenceGate.Run([c1, c2], c => ReferenceWalker.Walk(bars, c, c == c1 ? Swing(1) : Swing(2), OracleFaults.None));
        EngineGate.ShouldAgree(g, bars, [c1, c2], c => c == c1 ? Swing(1) : Swing(2));

        g.Entries[0].Result.Trade!.ExitReason.ShouldBe(OracleExitReason.MaxHold);
        g.Entries[0].HoldEndEpochSeconds.ShouldBe(E(Mon, "15:29"));
        g.Entries[1].Blocked.ShouldBeFalse();
        OracleTrade second = g.Entries[1].Result.Trade!;
        second.EntryPrice.ShouldBe(100.05);
        second.ExitReason.ShouldBe(OracleExitReason.MaxHold);
        RowCheck.R4(second.RExact).ShouldBe(0.1089m);
        g.Rows.ShouldContain(OracleRows.D11Close);
    }

    [Fact]
    public void A_candidate_at_the_hold_end_instant_is_blocked_and_one_minute_later_is_not()
    {
        // Setup A stops out at 10:20; the exit minute starts 10:20, so D = 10:20 is blocked and D = 10:21 is not.
        Tape t = new Tape().Day(Mon, "09:15", "15:29", 100.00m, [A, M("10:20", 99.60m, 99.70m, 98.90m, 99.00m)]);
        OracleCandidate c1 = Cand(t, OracleSide.Buy, Mon, "10:00", 100.00m, 99.00m);
        OracleCandidate atEnd = Cand(t, OracleSide.Buy, Mon, "10:20", 99.00m, 98.00m);
        OracleCandidate after = Cand(t, OracleSide.Buy, Mon, "10:21", 99.00m, 98.00m);
        OracleGateResult g = Gate(t, Swing(5), c1, atEnd, after);
        g.Entries[1].Blocked.ShouldBeTrue();
        g.Entries[2].Blocked.ShouldBeFalse();
    }

    [Fact]
    public void A_no_trade_is_not_accepted_and_does_not_set_the_gate()
    {
        Tape t = new Tape().Day(Mon, "09:15", "15:29", 100.00m, [M("10:00", 100.40m, 100.60m, 100.20m, 100.50m)]);
        OracleCandidate miss = Cand(t, OracleSide.Buy, Mon, "10:00", 100.00m, 99.00m);
        OracleCandidate next = Cand(t, OracleSide.Buy, Mon, "10:30", 100.50m, 99.00m);
        OracleGateResult g = Gate(t, Swing(5), miss, next);
        g.Entries[0].Result.NoTrade.ShouldBe(OracleNoTradeReason.LimitNotReached);
        g.Entries[1].Blocked.ShouldBeFalse();
    }
}
