using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.Research;

public sealed class TripleBarrierTests
{
    private static readonly long Entry = new DateTimeOffset(2025, 3, 3, 9, 45, 0, TimeSpan.FromMinutes(330)).ToUnixTimeSeconds();

    private static SimTrade Trade(ExitReason reason, int minutes = 30, double? target = 105.5, double rExact = 1.0, double rFilled = 0.9, double? rNet = null, string symbol = "X", ValueList<Leg>? legs = null)
    {
        var costing = rNet is null
            ? null
            : new TradeCosting(CostSegment.IntradayEquity, "s", 100, 1000m, 10000m, 10000m, LegCharges.Zero, LegCharges.Zero, 100m, 0.1, rNet.Value);
        return new SimTrade(
            SimTrade.CurrentSchemaVersion, "ORB", Guid.Empty, symbol, Side.Buy, new DateOnly(2025, 3, 3), Entry, Timeframe.M15, new Bar(Entry - 900, 99, 100.5, 98, 100, 1_000), 15, true,
            Entry, 100.5, 98, target, Entry + (minutes * 60L), 105.5, reason, rExact, 0.4, 1.2, FillBasis.Exact, costing is null ? CostBasis.None : CostBasis.Charged, null,
            rFilled, 0, 100.5, 105.5, EntryFill.AtLimit, false, 1, 0, costing, legs);
    }

    [Theory]
    [InlineData(ExitReason.Target, 1, BarrierOutcome.Target)]
    [InlineData(ExitReason.Stop, 0, BarrierOutcome.Stop)]
    [InlineData(ExitReason.SquareOff, 0, BarrierOutcome.SessionEnd)]
    [InlineData(ExitReason.MaxHold, 0, BarrierOutcome.MaxHold)]
    [InlineData(ExitReason.EmaExit, 0, BarrierOutcome.RuleExit)]
    [InlineData(ExitReason.TimeStop, 0, BarrierOutcome.RuleExit)]
    public void Every_exit_reason_has_its_label_from_the_hand_table(ExitReason reason, int label, BarrierOutcome outcome)
    {
        var result = TripleBarrier.Label(Trade(reason));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Label.ShouldBe(label);
        result.Value.Outcome.ShouldBe(outcome);
    }

    [Fact]
    public void The_table_covers_every_exit_reason()
    {
        foreach (var reason in Enum.GetValues<ExitReason>().Where(r => r != ExitReason.EndOfData))
        {
            TripleBarrier.Label(Trade(reason)).IsSuccess.ShouldBeTrue(reason.ToString());
        }
    }

    [Fact]
    public void A_position_still_open_when_the_bars_ended_is_unresolved()
    {
        var result = TripleBarrier.Label(Trade(ExitReason.EndOfData));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("label.unresolved");
    }

    [Fact]
    public void A_trade_without_a_target_has_no_upper_barrier()
    {
        var result = TripleBarrier.Label(Trade(ExitReason.Stop, target: null));

        result.Error.Code.ShouldBe("label.no_target");
    }

    [Fact]
    public void R_follows_the_basis_and_the_label_does_not()
    {
        var trade = Trade(ExitReason.Target, rExact: 2.0, rFilled: 1.9, rNet: 1.7);

        var exact = TripleBarrier.Label(trade, LabelBasis.Exact).Value;
        var filled = TripleBarrier.Label(trade, LabelBasis.Filled).Value;
        var net = TripleBarrier.Label(trade, LabelBasis.Net).Value;

        exact.R.ShouldBe(2.0);
        filled.R.ShouldBe(1.9);
        net.R.ShouldBe(1.7);
        new[] { exact.Label, filled.Label, net.Label }.ShouldAllBe(l => l == 1);
    }

    [Fact]
    public void A_trade_that_was_not_charged_has_no_net_result()
    {
        TripleBarrier.Label(Trade(ExitReason.Target), LabelBasis.Net).Error.Code.ShouldBe("label.no_net_r");
    }

    [Fact]
    public void The_default_basis_is_net_when_charged_filled_when_modelled_else_exact()
    {
        TripleBarrier.BasisOf(Trade(ExitReason.Stop)).ShouldBe(LabelBasis.Exact);
        TripleBarrier.BasisOf(Trade(ExitReason.Stop, rNet: -1.1)).ShouldBe(LabelBasis.Net);
        TripleBarrier.BasisOf(Trade(ExitReason.Stop) with { Fills = FillBasis.Modelled }).ShouldBe(LabelBasis.Filled);
    }

    [Fact]
    public void Minutes_to_resolution_count_from_the_fill_minute_and_the_horizon_ends_at_the_exit_minute()
    {
        var same = TripleBarrier.Label(Trade(ExitReason.Stop, minutes: 0)).Value;
        var later = TripleBarrier.Label(Trade(ExitReason.Target, minutes: 37)).Value;

        same.MinutesToResolution.ShouldBe(0);
        later.MinutesToResolution.ShouldBe(37);
        later.ResolvedEpochSeconds.ShouldBe(Entry + (37 * 60));
        later.DecisionEpochSeconds.ShouldBe(Entry);
    }

    [Fact]
    public void A_trade_with_legs_follows_the_last_exit_and_says_so()
    {
        ValueList<Leg> legs = new([
            new Leg(Entry, 100.5, 100.5, 1m, LegKind.Entry, string.Empty),
            new Leg(Entry + 600, 102, 102, 0.5m, LegKind.Exit, "partial"),
            new Leg(Entry + 1800, 98, 98, 0.5m, LegKind.Exit, "Stop"),
        ]);

        var label = TripleBarrier.Label(Trade(ExitReason.Stop, legs: legs)).Value;

        label.Label.ShouldBe(0);
        label.HasLegs.ShouldBeTrue();
    }

    [Fact]
    public void The_label_is_a_function_of_the_exit_reason_and_the_chosen_R_alone_so_nothing_past_the_barrier_leaks_in()
    {
        // vary every field that describes the path after or around the barrier: the label, the outcome and the horizon must not move
        foreach (var reason in Enum.GetValues<ExitReason>().Where(r => r != ExitReason.EndOfData))
        {
            var baseline = TripleBarrier.Label(Trade(reason), LabelBasis.Exact).Value;
            foreach (var mutated in new[]
            {
                Trade(reason) with { MaeR = 9, MfeR = 9 },
                Trade(reason) with { ExitPrice = 1, ExitFilledPrice = 1 },
                Trade(reason) with { RFilled = -5, SlippageR = -5 },
                Trade(reason) with { StopPrice = 50, TargetPrice = 500 },
                Trade(reason) with { TriggerBar = new Bar(1, 2, 3, 4, 5, 6) },
            })
            {
                TripleBarrier.Label(mutated, LabelBasis.Exact).Value.ShouldBe(baseline);
            }
        }
    }

    [Fact]
    public void Labelling_is_deterministic()
    {
        var a = TripleBarrier.Label(Trade(ExitReason.Target)).Value;
        var b = TripleBarrier.Label(Trade(ExitReason.Target)).Value;

        a.ShouldBe(b);
    }

    [Fact]
    public void A_training_set_counts_labels_and_what_was_left_out_and_its_id_is_stable_and_order_free()
    {
        var trades = new[]
        {
            Trade(ExitReason.Target, symbol: "A"),
            Trade(ExitReason.Stop, symbol: "B"),
            Trade(ExitReason.SquareOff, symbol: "C"),
            Trade(ExitReason.EndOfData, symbol: "D"),
            Trade(ExitReason.Stop, symbol: "E", target: null),
        };

        var set = TrainingSetBuilder.Build(["run-b", "run-a", "run-a"], trades).Value;
        var reversed = TrainingSetBuilder.Build(["run-a", "run-b"], trades.Reverse()).Value;

        set.Positives.ShouldBe(1);
        set.Negatives.ShouldBe(2);
        set.Unresolved.ShouldBe(1);
        set.NoTarget.ShouldBe(1);
        set.RunKeys.ShouldBe(["run-a", "run-b"]);
        set.FromDate.ShouldBe(new DateOnly(2025, 3, 3));
        set.ToDate.ShouldBe(new DateOnly(2025, 3, 3));
        set.RuleVersion.ShouldBe("tb-1");
        set.Labels.Select(l => l.Key).ShouldBe(set.Labels.Select(l => l.Key).Order(StringComparer.Ordinal));
        set.Id.Length.ShouldBe(64);
        reversed.Id.ShouldBe(set.Id);
    }

    [Fact]
    public void A_training_set_id_changes_when_a_label_changes()
    {
        var a = TrainingSetBuilder.Build(["r"], [Trade(ExitReason.Target)]).Value;
        var b = TrainingSetBuilder.Build(["r"], [Trade(ExitReason.Stop)]).Value;

        a.Id.ShouldNotBe(b.Id);
    }

    [Fact]
    public void A_training_set_refuses_a_key_twice_and_an_empty_result()
    {
        TrainingSetBuilder.Build(["r"], [Trade(ExitReason.Target), Trade(ExitReason.Stop)]).Error.Code.ShouldBe("training_set.duplicate_key");
        TrainingSetBuilder.Build(["r"], [Trade(ExitReason.EndOfData)]).Error.Code.ShouldBe("training_set.empty");
    }
}
