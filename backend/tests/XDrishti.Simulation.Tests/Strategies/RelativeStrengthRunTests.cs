using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Strength;
using XDrishti.Simulation.Running;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.File;
using XDrishti.Strategies.Model;

namespace XDrishti.Simulation.Tests.Strategies;

/// <summary>
/// BK-09 criteria 6 and 11 through the engine: a swing strategy that adds <c>mom_rank("Nifty 100", 126, 21) >= 0.67</c> to the hand-worked 52-week-high series trades when the stored row of the session before the decision says so, and does not when the rank is low, unknown, or
/// only stored for the decision session itself or later (no look-ahead, and no second shift); a run given no rows is refused.
/// </summary>
public sealed class RelativeStrengthRunTests
{
    private const int Trigger = 260;

    private static StrategyDefinition Definition()
    {
        var read = StrategyFile.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Seed", "HIGH_52W_MOMENTUM.yaml")));
        var buy = read.Definition.ShouldNotBeNull().Buy!;
        return read.Definition with { Buy = buy with { Trigger = new ConditionList(ListMode.All, [.. buy.Trigger!.Items, "mom_rank(\"Nifty 100\", 126, 21) >= 0.67"]) } };
    }

    private static RelativeStrengthInputs Rows(params (int Session, double Value)[] rows) =>
        new([.. rows.Select(r => new StrengthPoint("mom_rank", 2, 126, 21, BaseDays.Date(r.Session), r.Value))]);

    private static SimulationResult Run(RelativeStrengthInputs? rows)
    {
        var result = Sim.Create(Definition(), Timeframe.D1, Cand.NoSlippage).Run(Scan.Instrument, SwingBatchDays.Bars(SwingBatchDays.High52Rows()), Sim.Tick, Rep.DefaultCalendar, relativeStrength: rows);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code + ": " + result.Error.Message : string.Empty);
        return result.Value;
    }

    [Fact]
    public void A_high_rank_stored_for_the_session_before_the_decision_gives_the_trade_of_the_series()
    {
        var result = Run(Rows((Trigger - 1, 0.9)));

        var trade = result.Trades.Single();
        trade.TradeDate.ShouldBe(BaseDays.Date(Trigger + 1));
        trade.EntryPrice.ShouldBe(98.40, 1e-9);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(double.NaN)]
    public void A_low_or_unknown_rank_gives_no_trade_and_the_unknown_is_counted_never_a_pass(double value)
    {
        var result = Run(Rows([.. Enumerable.Range(0, Trigger).Select(i => (i, value))]));

        result.Trades.ShouldBeEmpty();
        result.OpenAtEnd.ShouldBeEmpty();
        // an unknown rank is a feature_unknown step (its own counter, once per session); a known low rank is simply false and is not (only the first session, which has no earlier row, is unknown)
        var unknown = result.Scans.Sum(s => s.FeatureUnknown);
        if (double.IsNaN(value))
        {
            unknown.ShouldBeGreaterThan(200);
        }
        else
        {
            unknown.ShouldBeLessThan(3);
        }
    }

    [Fact]
    public void A_row_as_of_the_decision_session_or_later_is_never_seen()
    {
        var result = Run(Rows((Trigger, 0.99), (Trigger + 1, 0.99), (Trigger + 5, 0.99)));

        result.Trades.ShouldBeEmpty("the row of the decision day is not known at the decision; the frame reads the session before it");
    }

    [Fact]
    public void Replacing_every_row_from_the_decision_session_on_changes_nothing()
    {
        var clean = Run(Rows((Trigger - 1, 0.9)));
        var poisoned = Run(Rows((Trigger - 1, 0.9), (Trigger, 0.0), (Trigger + 1, 0.0)));

        poisoned.Trades.Select(t => (t.TradeDate, t.EntryPrice, t.ExitReason)).ShouldBe(clean.Trades.Select(t => (t.TradeDate, t.EntryPrice, t.ExitReason)));
    }

    [Fact]
    public void A_run_given_no_rows_is_refused_with_the_reason()
    {
        var result = Sim.Create(Definition(), Timeframe.D1, Cand.NoSlippage).Run(Scan.Instrument, SwingBatchDays.Bars(SwingBatchDays.High52Rows()), Sim.Tick, Rep.DefaultCalendar);

        result.IsFailure.ShouldBeTrue();
        result.Error.Message.ShouldContain("relative-strength");
    }
}
