using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Replay;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;

namespace XDrishti.Simulation.Tests.Replay;

/// <summary>Criterion 8 of P3-04: bars are raw in the database and the replay adjusts a copy, once.</summary>
public sealed class AdjustmentTests
{
    private static readonly PreparedStrategy Bare = Rep.Prepare(OrbV1.Create());

    private static Bar[] DayWithFirstBar(DateOnly date, string firstRow, double price = 100.20) => Day.Dense(date).Start(price).Set(firstRow).Bars();

    private static (IReadOnlyList<Bar> Fed, InstrumentRun Record) ReplayAndSeeTheList(Bar[] bars, DateOnly from, DateOnly to, AdjustmentFactors factors, DateOnly? csv = null)
    {
        IReadOnlyList<Bar>? fed = null;
        var replay = InstrumentReplayer.ReplayCore(Bare, Rep.Input(bars, from, to, factors: factors, csvAdjustedThrough: csv), (list, _) => fed ??= list);
        return (fed.ShouldNotBeNull(), replay.Record);
    }

    [Fact]
    public void A_split_halves_the_prices_before_the_ex_date_and_doubles_the_volume_and_leaves_the_ex_date_alone()
    {
        var bars = ReplayDays.Join(DayWithFirstBar(ReplayDays.D1, "09:15 100.10/100.30/99.90/100.20"), DayWithFirstBar(ReplayDays.D2, "09:15 50.10/50.30/49.90/50.20", 50.20));

        var (fed, _) = ReplayAndSeeTheList(bars, ReplayDays.D1, ReplayDays.D2, Rep.Split(ReplayDays.D2, 1, 2));

        fed[0].ShouldBe(new Bar(bars[0].EpochSeconds, 50.05, 50.15, 49.95, 50.10, 2000));
        fed[375].ShouldBe(bars[375], "a bar of the ex-date is unchanged");
        bars[0].Open.ShouldBe(100.10, "the raw list is never changed");
    }

    [Fact]
    public void The_hash_of_the_bars_covers_the_raw_bars_before_adjustment()
    {
        var bars = ReplayDays.Join(ReplayDays.Flat(ReplayDays.D1, 99.00), ReplayDays.Flat(ReplayDays.D2, 49.50));

        var adjusted = Rep.Replay(Bare, bars, ReplayDays.D1, ReplayDays.D2, factors: Rep.Split(ReplayDays.D2, 1, 2)).Record;
        var plain = Rep.Replay(Bare, bars, ReplayDays.D1, ReplayDays.D2).Record;

        adjusted.BarsSha256.ShouldBe(BarStreamHash.Compute(bars));
        plain.BarsSha256.ShouldBe(adjusted.BarsSha256);
    }

    [Fact]
    public void A_bonus_of_one_for_two_gives_the_factor_two_thirds_for_prices_and_volume()
    {
        var bonus = AdjustmentFactors.From([new AppliedAction(ReplayDays.D2, CorporateActionKind.Bonus, 1, 2, 0.666666666667m)]);
        var bars = ReplayDays.Join(Day.Dense(ReplayDays.D1).Start(100.00).Bars(), Day.Dense(ReplayDays.D2).Start(66.67).Bars());

        var (fed, _) = ReplayAndSeeTheList(bars, ReplayDays.D1, ReplayDays.D2, bonus);

        fed[0].Open.ShouldBe(66.6667);
        fed[0].Volume.ShouldBe(1500);
    }

    [Fact]
    public void An_instrument_whose_actions_are_all_on_or_before_its_first_bar_is_not_copied_and_they_are_not_in_the_record()
    {
        var bars = ReplayDays.Join(ReplayDays.Flat(ReplayDays.D1, 99.00), ReplayDays.Flat(ReplayDays.D2, 99.00));
        var factors = AdjustmentFactors.From(
        [
            new AppliedAction(new DateOnly(2026, 2, 20), CorporateActionKind.Split, 1, 2, 0.5m),
            new AppliedAction(ReplayDays.D1, CorporateActionKind.Split, 1, 5, 0.2m),
        ]);

        var (fed, record) = ReplayAndSeeTheList(bars, ReplayDays.D1, ReplayDays.D2, factors);

        fed.ShouldBeSameAs(bars, "the same list is used");
        record.Adjustments.ShouldBeEmpty();
    }

    [Fact]
    public void An_instrument_with_an_action_after_its_first_bar_is_copied_and_the_action_is_recorded()
    {
        var bars = ReplayDays.Join(ReplayDays.Flat(ReplayDays.D1, 99.00), ReplayDays.Flat(ReplayDays.D2, 49.50));

        var (fed, record) = ReplayAndSeeTheList(bars, ReplayDays.D1, ReplayDays.D2, Rep.Split(ReplayDays.D2, 1, 2));

        fed.ShouldNotBeSameAs(bars);
        record.Adjustments.ShouldBe([new AppliedAdjustment(ReplayDays.D2, CorporateActionKind.Split, 1, 2, 0.5m, false)]);
    }

    [Fact]
    public void A_split_after_the_last_day_halves_every_price_of_the_trade_and_equals_a_direct_run_on_the_halved_bars()
    {
        var bars = ReplayDays.Join(ReplayDays.Flat(ReplayDays.D1, 99.00), ReplayDays.F1(ReplayDays.D2), ReplayDays.Flat(ReplayDays.D3, 105.00));
        var halved = ReplayDays.Scaled(bars, 0.5, volumeFactor: 2);

        var replay = Rep.Replay(Bare, bars, ReplayDays.D1, ReplayDays.D3, factors: Rep.Split(ReplayDays.D4, 1, 2));
        var direct = Sim.Run(OrbV1.Create(), halved, fills: Cand.NoSlippage, instrument: new InstrumentRef(Rep.Id, "TEST"));

        var trade = replay.Trades.ShouldHaveSingleItem();
        trade.EntryPrice.ShouldBe(50.25);
        trade.StopPrice.ShouldBe(49.00);
        trade.ExitPrice.ShouldBe(52.75);
        trade.RExact.ShouldBe(2.0);
        RunText.Trade(trade).ShouldBe(RunText.Trade(direct.Trades.ShouldHaveSingleItem()));
    }

    [Fact]
    public void Only_the_actions_on_or_before_the_date_the_csv_files_reach_are_marked_as_on_csv_bars()
    {
        var bars = ReplayDays.Join(ReplayDays.Flat(ReplayDays.D1, 99.00), ReplayDays.Flat(ReplayDays.D2, 99.00), ReplayDays.Flat(ReplayDays.D3, 99.00));
        var factors = AdjustmentFactors.From(
        [
            new AppliedAction(ReplayDays.D2, CorporateActionKind.Split, 1, 2, 0.5m),
            new AppliedAction(ReplayDays.D4, CorporateActionKind.Bonus, 1, 1, 0.5m),
        ]);

        var record = Rep.Replay(Bare, bars, ReplayDays.D1, ReplayDays.D3, factors: factors, csvAdjustedThrough: ReplayDays.D3).Record;

        record.Adjustments.Select(a => (a.ExDate, a.OnCsvBars)).ShouldBe([(ReplayDays.D2, true), (ReplayDays.D4, false)]);
    }

    [Fact]
    public void Without_a_csv_date_no_action_is_marked()
    {
        var bars = ReplayDays.Join(ReplayDays.Flat(ReplayDays.D1, 99.00), ReplayDays.Flat(ReplayDays.D2, 49.50));

        Rep.Replay(Bare, bars, ReplayDays.D1, ReplayDays.D2, factors: Rep.Split(ReplayDays.D2, 1, 2)).Record.Adjustments.ShouldAllBe(a => !a.OnCsvBars);
    }
}
