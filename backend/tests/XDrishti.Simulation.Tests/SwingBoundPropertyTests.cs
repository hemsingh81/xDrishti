using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;

namespace XDrishti.Simulation.Tests;

/// <summary>
/// Criteria 9 and 22 of P3-03d over the 500 seeded multi-day series (the calendar given in 70 %, none in 30 %): the hold is bounded by the due session, found by the test's own scan of the calendar, the sessions held and unseen are
/// the test's own count, and slippage in a hold never changes a decision, never improves a fill and is monotone in each parameter.
/// </summary>
public sealed class SwingBoundPropertyTests
{
    private const double Tolerance = 1e-12;

    private static readonly FillRules None = FillRules.Create(0.1, SlippageConfig.None).Value;

    [Fact]
    public void No_hold_is_longer_than_its_due_session_and_a_max_hold_leaves_at_the_due_minute_or_at_the_last_known_close_before_it()
    {
        var maxHolds = 0;
        var atLastClose = 0;
        foreach (var walk in SwingWalks.WalkAll(SeededSwing.All, FillRules.Default).Where(w => w.Trade is not null))
        {
            var (series, trade, outcome) = (walk.Series, walk.Trade!, walk.Outcome);
            var bars = series.Bars;
            var label = $"seed {series.Seed} {trade.ExitReason}";
            var entryDate = trade.TradeDate;
            var due = series.Due(entryDate);
            var dueMinute = series.DueMinute(due);
            var dueEpoch = Day.Epoch(due, dueMinute);
            var exitDate = SessionClock.DateOf(trade.ExitEpochSeconds);

            trade.ExitEpochSeconds.ShouldBeLessThanOrEqualTo(dueEpoch, label + ": no trade exits after its due minute");
            exitDate.ShouldBeLessThanOrEqualTo(due, label);
            if (trade.ExitReason is ExitReason.MaxHold or ExitReason.Stop or ExitReason.Target)
            {
                trade.SessionsHeld.ShouldBeLessThanOrEqualTo(series.Sessions, label);
            }

            if (trade.ExitReason == ExitReason.MaxHold)
            {
                maxHolds++;
                var dueBar = bars.Where(b => b.EpochSeconds == dueEpoch).Select(b => (Bar?)b).FirstOrDefault();
                if (!outcome.MaxHoldAtLastClose)
                {
                    trade.ExitEpochSeconds.ShouldBe(dueEpoch, label + ": a max hold is at the due minute of the due session");
                    dueBar.ShouldNotBeNull(label);
                    dueBar!.Value.Volume.ShouldBeGreaterThan(0, label);
                    trade.ExitPrice.ShouldBe(dueBar.Value.Close, label + ": the close of the due minute");
                }
                else
                {
                    atLastClose++;
                    (dueBar is null || dueBar.Value.Volume == 0).ShouldBeTrue(label + ": the due minute held no trade");
                    var after = bars.Where(b => b.EpochSeconds > trade.ExitEpochSeconds && b.EpochSeconds <= dueEpoch).ToList();
                    after.ShouldAllBe(b => b.Volume == 0, label + ": nothing between the exit and the due minute holds a trade");
                    var last = bars.Single(b => b.EpochSeconds == trade.ExitEpochSeconds);
                    trade.ExitPrice.ShouldBe(last.Close, label + ": the close of the last minute that holds a trade");
                    outcome.HoldEndEpochSeconds.ShouldBe(Day.Epoch(due, dueMinute + 1), label + ": the position counts as open until its due session ends");
                }
            }
        }

        maxHolds.ShouldBeGreaterThanOrEqualTo(100);
        atLastClose.ShouldBeGreaterThanOrEqualTo(10);
    }

    [Fact]
    public void The_sessions_held_and_unseen_of_every_trade_are_the_tests_own_count_from_the_calendar_and_the_bars()
    {
        var unseenTrades = 0;
        foreach (var walk in SwingWalks.WalkAll(SeededSwing.All, FillRules.Default).Where(w => w.Trade is not null))
        {
            var (series, trade) = (walk.Series, walk.Trade!);
            var exitDate = SessionClock.DateOf(trade.ExitEpochSeconds);
            var held = SwingOracle.SessionsHeld(trade.TradeDate, exitDate, series.Given);
            var seen = series.Bars
                .Where(b => b.EpochSeconds >= trade.EntryEpochSeconds && b.EpochSeconds <= trade.ExitEpochSeconds && b.Volume > 0)
                .Select(b => SessionClock.DateOf(b.EpochSeconds))
                .Distinct()
                .Count();

            trade.SessionsHeld.ShouldBe(held, "seed " + series.Seed);
            trade.SessionsUnseen.ShouldBe(Math.Max(0, held - seen), "seed " + series.Seed);
            trade.SessionsHeld.ShouldBeGreaterThanOrEqualTo(1);
            unseenTrades += trade.SessionsUnseen > 0 ? 1 : 0;
        }

        unseenTrades.ShouldBeGreaterThanOrEqualTo(10);
    }

    [Fact]
    public void The_excursions_bracket_the_result_and_the_exit_is_never_before_the_entry_on_every_trade()
    {
        foreach (var walk in SwingWalks.WalkAll(SeededSwing.All, FillRules.Default).Where(w => w.Trade is not null))
        {
            var trade = walk.Trade!;
            var label = $"seed {walk.Series.Seed} {trade.ExitReason}";

            (-trade.MaeR).ShouldBeLessThanOrEqualTo(trade.RExact + 1e-9, label);
            trade.RExact.ShouldBeLessThanOrEqualTo(trade.MfeR + 1e-9, label);
            trade.MaeR.ShouldBeGreaterThanOrEqualTo(0, label);
            trade.MfeR.ShouldBeGreaterThanOrEqualTo(0, label);
            trade.ExitEpochSeconds.ShouldBeGreaterThanOrEqualTo(trade.EntryEpochSeconds, label);
            trade.EntryEpochSeconds.ShouldBeGreaterThanOrEqualTo(trade.DecisionEpochSeconds, label);
            trade.SchemaVersion.ShouldBe(4, label);
            if (trade.ExitReason == ExitReason.Target)
            {
                trade.RExact.ShouldBe(walk.Series.RewardRisk, 1e-9, label + ": a target exit is exactly k");
            }
        }
    }

    [Fact]
    public void A_stop_exit_is_never_better_than_the_stop_and_a_target_exit_never_better_than_the_target_overnight_included()
    {
        var gaps = 0;
        foreach (var walk in SwingWalks.WalkAll(SeededSwing.All, FillRules.Default).Where(w => w.Trade is not null))
        {
            var trade = walk.Trade!;
            if (trade.ExitReason == ExitReason.Stop)
            {
                trade.ExitPrice.ShouldBeLessThanOrEqualTo(trade.StopPrice + Tolerance, "seed " + walk.Series.Seed);
                gaps += trade.ExitPrice < trade.StopPrice - Tolerance ? 1 : 0;
            }
            else if (trade.ExitReason == ExitReason.Target)
            {
                trade.ExitPrice.ShouldBe(trade.TargetPrice!.Value, 1e-9, "seed " + walk.Series.Seed);
            }
        }

        gaps.ShouldBeGreaterThanOrEqualTo(10);
    }

    // criterion 9: slippage in a hold

    private static readonly (string Parameter, FillRules[] Rules)[] Ladders =
    [
        ("Ticks", [.. new double[] { 0, 1, 2, 3 }.Select(v => WalkProperties.Rules(v, 0.05, 2))]),
        ("BarRangeFrac", [.. new[] { 0, 0.05, 0.1, 0.2 }.Select(v => WalkProperties.Rules(1, v, 2))]),
        ("StopExitExtraTicks", [.. new double[] { 0, 2, 4, 5 }.Select(v => WalkProperties.Rules(1, 0.05, v))]),
    ];

    private static string Decision(SwingWalkResult walk)
    {
        var outcome = walk.Outcome;
        var trade = outcome.Trade is { } t ? t with { Fills = FillBasis.Exact, RFilled = 0, SlippageR = 0, EntryFilledPrice = 0, ExitFilledPrice = 0 } : null;
        return OutcomeFormatter.Render(XDrishti.Domain.Common.Result.Success(outcome with { Trade = trade }));
    }

    [Fact]
    public void Slippage_never_changes_a_decision_in_a_hold_whatever_its_parameters()
    {
        var exact = SwingWalks.WalkAll(SeededSwing.All, None);
        var configurations = new List<FillRules> { FillRules.Default };
        configurations.AddRange(Ladders.SelectMany(l => l.Rules));

        foreach (var rules in configurations)
        {
            var modelled = SwingWalks.WalkAll(SeededSwing.All, rules);

            modelled.Count.ShouldBe(exact.Count);
            for (var i = 0; i < exact.Count; i++)
            {
                Decision(modelled[i]).ShouldBe(Decision(exact[i]), $"seed {exact[i].Series.Seed} candidate {i} under {rules.ToCanonicalJson()}");
            }
        }
    }

    [Fact]
    public void No_fill_of_a_hold_is_ever_better_than_the_exact_price_and_max_hold_and_end_of_data_pay_the_base_slippage_without_the_extra()
    {
        var compared = 0;
        foreach (var walk in SwingWalks.WalkAll(SeededSwing.All, FillRules.Default).Where(w => w.Trade is not null))
        {
            var trade = walk.Trade!;
            var label = $"seed {walk.Series.Seed} {trade.ExitReason}";

            trade.EntryFilledPrice.ShouldBeGreaterThanOrEqualTo(trade.EntryPrice, label + " entry");
            trade.ExitFilledPrice.ShouldBeLessThanOrEqualTo(trade.ExitPrice, label + " exit");
            trade.RFilled.ShouldBeLessThanOrEqualTo(trade.RExact + Tolerance, label);
            trade.SlippageR.ShouldBeLessThanOrEqualTo(Tolerance, label);
            if (trade.ExitReason == ExitReason.Target)
            {
                trade.ExitFilledPrice.ShouldBe(trade.ExitPrice, 0.1 + 1e-9, label + ": a target only goes on the grid");
            }
            else
            {
                // The amount is (1 tick) + 5 % of the range of the exit minute (a stop adds 2 ticks), rounded down on the grid: the move is at most that plus a tick.
                var minute = walk.Series.Bars.Single(b => b.EpochSeconds == trade.ExitEpochSeconds);
                var extra = trade.ExitReason == ExitReason.Stop ? 2 : 0;
                var amount = (double)((1 + extra) * walk.Series.Tick) + (0.05 * (minute.High - minute.Low));
                (trade.ExitPrice - trade.ExitFilledPrice).ShouldBeLessThanOrEqualTo(amount + (double)walk.Series.Tick + 1e-9, label);
                (trade.ExitPrice - trade.ExitFilledPrice).ShouldBeGreaterThanOrEqualTo(amount - 1e-9, label + ": it pays at least the amount");
            }

            compared++;
        }

        compared.ShouldBeGreaterThan(900);
    }

    [Fact]
    public void Raising_one_parameter_never_raises_filled_r_in_a_hold()
    {
        var compared = 0;
        foreach (var (parameter, rules) in Ladders)
        {
            var runs = rules.Select(r => SwingWalks.WalkAll(SeededSwing.All, r)).ToList();
            for (var step = 1; step < runs.Count; step++)
            {
                for (var i = 0; i < runs[0].Count; i++)
                {
                    if (runs[step][i].Trade is not { } higher || runs[step - 1][i].Trade is not { } lower)
                    {
                        continue;
                    }

                    higher.RFilled.ShouldBeLessThanOrEqualTo(lower.RFilled + Tolerance, $"{parameter} step {step}, seed {runs[0][i].Series.Seed} candidate {i}");
                    compared++;
                }
            }
        }

        compared.ShouldBeGreaterThan(3 * 3 * 900);
    }
}
