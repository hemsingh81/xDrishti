using System.Reflection;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>
/// Acceptance criterion 4 of P3-08 (D3): the adapter is the one place where the engine owner's reading of the engine can hollow the comparison, so it is tested on its own. A case goes through the adapter and comes back equal, field by
/// field, over every generator family; the engine's results mapped into the oracle's records lose and change nothing; and a deliberately dropped field makes the check fail (the sentinels).
/// </summary>
public sealed class AdapterRoundTripTests
{
    private static WalkRules Rules(WalkRules r) => r with { Fills = FillRules.Default };

    [Fact]
    public void Engine_cases_come_back_equal_for_the_seeded_days()
    {
        foreach (var day in SeededDays.All)
        {
            foreach (var walk in day.Walks)
            {
                RoundTrip.Engine(day.Bars, [walk.Candidate], Rules(walk.Rules)).ShouldBeEmpty($"SeededDays {day.Seed}");
            }
        }
    }

    [Fact]
    public void Engine_cases_come_back_equal_for_the_seeded_daily_series()
    {
        foreach (var series in SeededDaily.All)
        {
            RoundTrip.Engine(series.Bars, series.Candidates, series.Rules(FillRules.Default)).ShouldBeEmpty($"SeededDaily {series.Seed}");
        }
    }

    [Fact]
    public void Engine_cases_come_back_equal_for_the_swing_series_with_their_calendars()
    {
        foreach (var series in SeededSwing.All)
        {
            RoundTrip.Engine(series.Bars, series.Candidates, series.Rules(FillRules.Default)).ShouldBeEmpty($"SwingSeries {series.Seed}");
        }
    }

    [Fact]
    public void Engine_cases_come_back_equal_for_the_replay_strategies()
    {
        foreach (var (name, _, rules) in ComparisonCases.ReplayStrategies)
        {
            foreach (var series in SeededReplay.All.Take(20))
            {
                RoundTrip.Engine(series.Bars, [], rules(false)).ShouldBeEmpty($"{name} {series.Seed}");
            }
        }
    }

    [Fact]
    public void Oracle_cases_come_back_equal_for_the_authors_series()
    {
        for (var category = 1; category <= OracleCategories.Count; category++)
        {
            for (long seed = 0; seed < 100; seed++)
            {
                RoundTrip.Oracle(OracleSeries.Of(category, seed)).ShouldBeEmpty($"OracleSeries {category}/{seed}");
            }
        }
    }

    [Fact]
    public void Oracle_cases_come_back_equal_for_the_authors_hand_rows()
    {
        foreach (var row in AllRows.Rows)
        {
            var source = new OracleCase(0, 0, row.Bars, [row.Candidate], row.Rules);

            RoundTrip.Oracle(source).ShouldBeEmpty(row.Id);
        }
    }

    // ---- the sentinels: a deliberately dropped field makes the round trip fail

    public static TheoryData<string> Drops() =>
    [
        "slippage ticks", "slippage range", "slippage stop ticks", "reward-risk", "square-off", "entry limit", "tick", "hold sessions", "hold", "calendar holiday", "calendar special session",
    ];

    [Theory]
    [MemberData(nameof(Drops))]
    public void A_dropped_rule_field_makes_the_engine_round_trip_fail(string drop)
    {
        var series = SeededSwing.All.First(s => s.Given is not null && s.Candidates.Count > 0 && s.Truth.SessionOf(s.Dates[0].AddDays(5)) is { IsRegular: false });
        var rules = series.Rules(FillRules.Default);

        Func<OracleRules, OracleRules> lossy = drop switch
        {
            "slippage ticks" => r => r with { Slippage = r.Slippage with { Ticks = 0 } },
            "slippage range" => r => r with { Slippage = r.Slippage with { BarRangeFrac = 0 } },
            "slippage stop ticks" => r => r with { Slippage = r.Slippage with { StopExitExtraTicks = 0 } },
            "reward-risk" => r => r with { RewardRisk = null },
            "square-off" => r => r with { SquareOffMinute = 915 },
            "entry limit" => r => r with { EntryLimitPercent = 0.2 },
            "tick" => r => r with { Tick = 0.01m },
            "hold sessions" => r => r with { Hold = r.Hold! with { MaxHoldSessions = r.Hold.MaxHoldSessions == 1 ? 2 : 1 } },
            "hold" => r => r with { Hold = null },
            "calendar holiday" => r => r with { Hold = r.Hold! with { Calendar = new OracleCalendar([], r.Hold.Calendar!.SpecialSessions) } },
            _ => r => r with { Hold = r.Hold! with { Calendar = new OracleCalendar(r.Hold.Calendar!.Holidays, new Dictionary<DateOnly, OracleSession>()) } },
        };

        // the unspoilt trip is clean for the same series (a failure below is the drop and nothing else)
        RoundTrip.Engine(series.Bars, series.Candidates, rules).ShouldBeEmpty();
        RoundTrip.Engine(series.Bars, series.Candidates, rules, lossy).ShouldNotBeEmpty($"dropping '{drop}' was not noticed");
    }

    [Fact]
    public void A_dropped_bar_field_or_candidate_field_makes_the_oracle_round_trip_fail()
    {
        var source = OracleSeries.Of(3, 5);

        RoundTrip.Oracle(source).ShouldBeEmpty();
        RoundTrip.Oracle(source, lossyBar: b => b with { Volume = 0 }).ShouldNotBeEmpty("a dropped volume");
        RoundTrip.Oracle(source, lossyBar: b => b with { Low = b.Low - 0.01 }).ShouldNotBeEmpty("a changed low");
        RoundTrip.Oracle(source, lossyCandidate: c => c with { StopLevel = c.StopLevel + 0.05 }).ShouldNotBeEmpty("a changed stop");
        RoundTrip.Oracle(source, lossyCandidate: c => c with { TriggerClose = c.TriggerClose + 0.05 }).ShouldNotBeEmpty("a changed trigger close");
        RoundTrip.Oracle(source, lossyCandidate: c => c with { Side = c.Side == OracleSide.Buy ? OracleSide.Sell : OracleSide.Buy }).ShouldNotBeEmpty("a changed side");
    }

    // ---- the results: the engine's trade and counters, mapped into the oracle's records, lose nothing

    private static object? Plain(object? value) => value is Enum e ? e.ToString() : value;

    private static void ShouldMapField(PropertyInfo oracleProperty, object oracleObject, object engineObject, string label)
    {
        var engineProperty = engineObject.GetType().GetProperty(oracleProperty.Name);
        engineProperty.ShouldNotBeNull($"{label}: no property '{oracleProperty.Name}' on the engine's {engineObject.GetType().Name}");
        var a = Plain(oracleProperty.GetValue(oracleObject));
        var b = Plain(engineProperty.GetValue(engineObject));
        if (a is int && b is long) { b = checked((int)(long)b); }
        if (a is double da && b is double db)
        {
            BitConverter.DoubleToInt64Bits(da).ShouldBe(BitConverter.DoubleToInt64Bits(db), $"{label}.{oracleProperty.Name}");
        }
        else
        {
            a.ShouldBe(b, $"{label}.{oracleProperty.Name}");
        }
    }

    [Fact]
    public void Every_field_of_the_engines_trade_and_counters_survives_the_mapping_into_the_oracles_records()
    {
        var trades = 0;
        foreach (var day in SeededDays.All.Take(200))
        {
            foreach (var walk in day.Walks)
            {
                var outcome = TradeWalker.Walk(walk.Candidate, day.Bars, Rules(walk.Rules)).Value;
                var mapped = OracleAdapter.Result(outcome);
                foreach (var property in typeof(OracleCounters).GetProperties().Where(p => p.Name != "EqualityContract"))
                {
                    ShouldMapField(property, mapped.Counters, outcome.ToCounters(), $"SeededDays {day.Seed} counters");
                }

                if (outcome.Trade is { } trade)
                {
                    trades++;
                    mapped.Trade.ShouldNotBeNull();
                    foreach (var property in typeof(OracleTrade).GetProperties().Where(p => p.Name != "EqualityContract"))
                    {
                        ShouldMapField(property, mapped.Trade, trade, $"SeededDays {day.Seed} trade");
                    }

                    mapped.HoldEndEpochSeconds.ShouldBe(outcome.HoldEndEpochSeconds);
                }
                else
                {
                    mapped.NoTrade.ToString().ShouldBe(outcome.NoTrade.ToString());
                    mapped.HoldEndEpochSeconds.ShouldBeNull();
                }
            }
        }

        trades.ShouldBeGreaterThan(100);
    }

    [Fact]
    public void The_sentinel_a_broken_mapping_of_one_trade_field_is_seen_by_the_field_check()
    {
        var day = SeededDays.All.First(d => d.Walks.Any(w => TradeWalker.Walk(w.Candidate, d.Bars, Rules(w.Rules)).Value.Trade is not null));
        var walk = day.Walks.First(w => TradeWalker.Walk(w.Candidate, day.Bars, Rules(w.Rules)).Value.Trade is not null);
        var trade = TradeWalker.Walk(walk.Candidate, day.Bars, Rules(walk.Rules)).Value.Trade!;
        var broken = OracleAdapter.Trade(trade) with { ExitPrice = trade.ExitPrice + 0.05 };
        var property = typeof(OracleTrade).GetProperty(nameof(OracleTrade.ExitPrice))!;

        Should.Throw<ShouldAssertException>(() => ShouldMapField(property, broken, trade, "sentinel"));
    }

    [Fact]
    public void Every_property_of_the_oracles_trade_and_counters_has_an_engine_property_of_the_same_name_and_the_reverse_holds_for_counters()
    {
        var simTrade = typeof(SimTrade).GetProperties().Select(p => p.Name).ToHashSet();
        typeof(OracleTrade).GetProperties().Where(p => p.Name != "EqualityContract").Select(p => p.Name).ShouldAllBe(n => simTrade.Contains(n));

        var oracleCounters = typeof(OracleCounters).GetProperties().Where(p => p.Name != "EqualityContract").Select(p => p.Name).Order().ToList();
        var engineCounters = typeof(WalkCounters).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.Name != "EqualityContract").Select(p => p.Name).Order().ToList();
        oracleCounters.ShouldBe(engineCounters);
    }
}
