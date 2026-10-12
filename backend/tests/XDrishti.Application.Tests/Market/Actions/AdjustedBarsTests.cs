using NSubstitute;
using XDrishti.Application.Abstractions.Market.Actions;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Market.Actions;
using XDrishti.Application.Market.Bars;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Tests.Market.Actions;

/// <summary>Adjusted reads: prices and volume are changed on read by the applied actions, for every timeframe, and raw stays the default (ADR 0017 §5).</summary>
public sealed class AdjustedBarsTests
{
    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    private static readonly DateTimeOffset Now = new(2026, 10, 12, 6, 30, 0, TimeSpan.Zero);

    private readonly InMemoryActionData _data = new();
    private readonly IBarQueries _queries = Substitute.For<IBarQueries>();
    private readonly ActionInstrument _abb;

    public AdjustedBarsTests()
    {
        _abb = _data.AddInstrument("ABB");
        _queries.FindInstrumentAsync(_abb.Id, Arg.Any<CancellationToken>()).Returns(new BarsInstrument(_abb.Id, _abb.Symbol, _abb.Name));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static BarRow Day(int month, int day, double close, long volume = 1000, int hour = 0, int minute = 0) =>
        new(new DateTimeOffset(2026, month, day, hour, minute, 0, Ist), close, close * 1.01, close * 0.99, close, volume, 375, true, null);

    private void Applied(DateOnly exDate, CorporateActionKind kind, int numerator, int denominator, bool ignored = false)
    {
        var action = CorporateAction.Apply(_abb.Id, exDate, kind, numerator, denominator, CorporateActionSource.Manual, null, "owner", Now);
        if (ignored)
        {
            action.Ignore("owner", Now);
        }

        _data.Actions.Add(action);
    }

    private async Task<InstrumentBars> ReadAsync(Timeframe timeframe, IReadOnlyList<BarRow> rows, bool adjusted)
    {
        _queries.ReadBarsAsync(_abb.Id, timeframe, 100, Arg.Any<CancellationToken>()).Returns(rows);
        var handler = new GetInstrumentBarsHandler(_queries, new AdjustmentFactorsProvider(_data));
        var result = await handler.HandleAsync(new GetInstrumentBarsQuery(_abb.Id, timeframe.ToCode(), null, adjusted), Ct);
        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }

    [Fact]
    public async Task Raw_is_the_default_and_has_no_adjustment_block_even_when_actions_exist()
    {
        Applied(new DateOnly(2026, 9, 1), CorporateActionKind.Split, 1, 5);
        var rows = new[] { Day(9, 2, 100), Day(8, 28, 500) };

        var bars = await ReadAsync(Timeframe.D1, rows, adjusted: false);

        bars.Adjustment.ShouldBeNull();
        bars.Items.ShouldBe(rows);
    }

    [Fact]
    public async Task Bars_before_the_ex_date_are_multiplied_and_the_volume_divided_while_the_ex_date_and_later_stay_raw()
    {
        Applied(new DateOnly(2026, 9, 1), CorporateActionKind.Split, 1, 5);
        var rows = new[] { Day(9, 2, 100, 5000), Day(9, 1, 101, 5000), Day(8, 31, 500, 1000), Day(8, 28, 505.5, 1000) };

        var bars = await ReadAsync(Timeframe.D1, rows, adjusted: true);

        bars.Items.Select(b => (b.Close, b.Volume)).ShouldBe([(100.0, 5000L), (101.0, 5000L), (100.0, 5000L), (101.1, 5000L)]);
        (bars.Items[2].Open, bars.Items[2].High, bars.Items[2].Low).ShouldBe((100.0, 101.0, 99.0));
        (bars.Items[2].Bars, bars.Items[2].Complete).ShouldBe((375L, true));
    }

    [Fact]
    public async Task The_adjustment_block_says_how_many_actions_changed_the_bars_read_the_factor_of_the_oldest_and_the_actions()
    {
        Applied(new DateOnly(2025, 3, 10), CorporateActionKind.Split, 1, 2);
        Applied(new DateOnly(2026, 9, 1), CorporateActionKind.Bonus, 1, 1);
        Applied(new DateOnly(2026, 8, 1), CorporateActionKind.Split, 1, 10, ignored: true);

        var recent = await ReadAsync(Timeframe.D1, [Day(9, 2, 100), Day(8, 20, 200)], adjusted: true);

        recent.Adjustment.ShouldNotBeNull();
        (recent.Adjustment.Applied, recent.Adjustment.Factor).ShouldBe((1, 0.5));
        recent.Adjustment.Actions.ShouldBe([new AdjustmentActionView(new DateOnly(2026, 9, 1), "bonus", "1:1", 0.5)]);

        var old = await ReadAsync(Timeframe.D1, [Day(9, 2, 100), new BarRow(new DateTimeOffset(2025, 1, 10, 0, 0, 0, Ist), 400, 400, 400, 400, 10, 375, true, null)], adjusted: true);

        (old.Adjustment!.Applied, old.Adjustment.Factor).ShouldBe((2, 0.25));
        old.Adjustment.Actions.Select(a => (a.ExDate, a.Kind, a.Ratio, a.Factor)).ShouldBe([(new DateOnly(2025, 3, 10), "split", "1:2", 0.5), (new DateOnly(2026, 9, 1), "bonus", "1:1", 0.5)]);
        old.Items[1].Close.ShouldBe(100.0);
    }

    [Fact]
    public async Task Bars_all_after_the_actions_are_unchanged_and_the_block_says_none_applied()
    {
        Applied(new DateOnly(2026, 9, 1), CorporateActionKind.Split, 1, 5);

        var bars = await ReadAsync(Timeframe.D1, [Day(9, 5, 100), Day(9, 2, 99)], adjusted: true);

        (bars.Adjustment!.Applied, bars.Adjustment.Factor, bars.Adjustment.Actions.Count).ShouldBe((0, 1.0, 0));
        bars.Items.Select(b => b.Close).ShouldBe([100.0, 99.0]);
    }

    [Fact]
    public async Task Suggested_and_ignored_actions_adjust_nothing()
    {
        _data.Actions.Add(CorporateAction.Suggest(_abb.Id, new DateOnly(2026, 9, 1), CorporateActionKind.Split, 1, 5, null, Now));
        Applied(new DateOnly(2026, 8, 1), CorporateActionKind.Split, 1, 10, ignored: true);

        var bars = await ReadAsync(Timeframe.D1, [Day(7, 1, 500)], adjusted: true);

        bars.Items[0].Close.ShouldBe(500.0);
        bars.Adjustment!.Applied.ShouldBe(0);
    }

    [Fact]
    public async Task Minute_bars_use_the_india_date_of_their_start_so_the_last_minutes_before_the_ex_date_are_adjusted()
    {
        Applied(new DateOnly(2026, 9, 1), CorporateActionKind.Split, 1, 5);
        var rows = new[] { Day(9, 1, 100, 10, 9, 15), Day(8, 31, 500, 10, 15, 29) };

        var bars = await ReadAsync(Timeframe.M1, rows, adjusted: true);

        bars.Items.Select(b => b.Close).ShouldBe([100.0, 100.0]);
        bars.Items.Select(b => b.Volume).ShouldBe([10L, 50L]);
    }

    private static BarRow DayRow(int month, int day, double open, double high, double low, double close, long volume) =>
        new(new DateTimeOffset(2026, month, day, 0, 0, 0, Ist), open, high, low, close, volume, 375, true, null);

    [Fact]
    public async Task A_week_or_month_that_lies_wholly_before_or_after_the_ex_date_is_adjusted_by_the_factor_of_its_days()
    {
        Applied(new DateOnly(2026, 9, 9), CorporateActionKind.Split, 1, 5);
        var weeks = new[] { Day(9, 14, 100), Day(8, 31, 500) }; // the week of 14 Sept is after the ex-date, the week of 31 Aug wholly before
        var months = new[] { Day(10, 1, 100), Day(8, 1, 500) };

        var week = await ReadAsync(Timeframe.W1, weeks, adjusted: true);
        var month = await ReadAsync(Timeframe.Mo1, months, adjusted: true);

        week.Items.Select(b => b.Close).ShouldBe([100.0, 100.0]);
        month.Items.Select(b => b.Close).ShouldBe([100.0, 100.0]);
        await _queries.DidNotReceiveWithAnyArgs().ReadDailyBarsAsync(default, default, default, Ct);
    }

    [Fact]
    public async Task A_week_with_the_ex_date_inside_is_built_from_the_adjusted_day_bars()
    {
        // Monday 7 Sept to Friday 11 Sept, ex-date Wednesday 9 Sept: Monday and Tuesday are before it (x 0.2), the rest are not
        Applied(new DateOnly(2026, 9, 9), CorporateActionKind.Split, 1, 5);
        var days = new[]
        {
            DayRow(9, 7, 500, 510, 495, 505, 1000),
            DayRow(9, 8, 505, 515, 490, 500, 2000),
            DayRow(9, 9, 100, 104, 98, 102, 5000),
            DayRow(9, 10, 102, 110, 101, 108, 4000),
            DayRow(9, 11, 108, 109, 99, 100, 3000),
        };
        _queries.ReadDailyBarsAsync(_abb.Id, new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 14), Arg.Any<CancellationToken>()).Returns(days);
        var raw = new BarRow(days[0].Ts, 500, 515, 490, 100, 15000, 1875, true, null);

        var bars = await ReadAsync(Timeframe.W1, [raw], adjusted: true);

        var week = bars.Items.ShouldHaveSingleItem();
        (week.Open, week.High, week.Low, week.Close, week.Volume).ShouldBe((100.0, 110.0, 98.0, 100.0, 5000L + 10000 + 5000 + 4000 + 3000));
        (week.Ts, week.Bars, week.Complete).ShouldBe((raw.Ts, 1875L, true));
        (bars.Adjustment!.Applied, bars.Adjustment.Factor).ShouldBe((1, 0.2));
    }

    [Fact]
    public async Task A_month_with_the_ex_date_inside_is_built_from_the_adjusted_day_bars_and_the_others_in_the_read_are_adjusted_as_before()
    {
        Applied(new DateOnly(2026, 9, 17), CorporateActionKind.Bonus, 1, 1); // x 0.5
        var days = new[]
        {
            DayRow(9, 1, 200, 220, 190, 210, 100),
            DayRow(9, 16, 210, 230, 205, 220, 100),
            DayRow(9, 17, 110, 118, 105, 115, 300),
            DayRow(9, 30, 115, 125, 112, 120, 200),
        };
        _queries.ReadDailyBarsAsync(_abb.Id, new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1), Arg.Any<CancellationToken>()).Returns(days);
        var september = new BarRow(days[0].Ts, 200, 230, 105, 120, 700, 1500, false, null);
        var october = DayRow(10, 1, 120, 130, 118, 125, 50);
        var august = DayRow(8, 3, 190, 200, 185, 195, 40);

        var bars = await ReadAsync(Timeframe.Mo1, [october, september, august], adjusted: true);

        var rebuilt = bars.Items[1];
        (rebuilt.Open, rebuilt.High, rebuilt.Low, rebuilt.Close, rebuilt.Volume).ShouldBe((100.0, 125.0, 95.0, 120.0, 200L + 200 + 300 + 200));
        rebuilt.Complete.ShouldBeFalse();
        (bars.Items[0].Close, bars.Items[2].Close).ShouldBe((125.0, 97.5)); // October after the ex-date; August before it, x 0.5
    }

    [Fact]
    public async Task A_bucket_without_day_bars_falls_back_to_the_factor_of_its_first_day()
    {
        Applied(new DateOnly(2026, 9, 9), CorporateActionKind.Split, 1, 5);
        _queries.ReadDailyBarsAsync(default, default, default, Ct).ReturnsForAnyArgs(Array.Empty<BarRow>());

        var bars = await ReadAsync(Timeframe.W1, [Day(9, 7, 500)], adjusted: true);

        bars.Items[0].Close.ShouldBe(100.0);
    }

    [Fact]
    public async Task Without_an_action_the_adjusted_weeks_and_months_equal_the_raw_ones_and_no_day_bars_are_read()
    {
        var rows = new[] { Day(9, 14, 100), Day(9, 7, 500) };

        var week = await ReadAsync(Timeframe.W1, rows, adjusted: true);
        var month = await ReadAsync(Timeframe.Mo1, rows, adjusted: true);

        week.Items.ShouldBe(rows);
        month.Items.ShouldBe(rows);
        await _queries.DidNotReceiveWithAnyArgs().ReadDailyBarsAsync(default, default, default, Ct);
    }

    [Fact]
    public async Task Intraday_buckets_follow_the_same_rule()
    {
        Applied(new DateOnly(2026, 9, 1), CorporateActionKind.Bonus, 1, 3);
        var rows = new[] { Day(9, 1, 100, 10, 9, 15), Day(8, 31, 400, 10, 15, 15) };

        var bars = await ReadAsync(Timeframe.H1, rows, adjusted: true);

        bars.Items.Select(b => b.Close).ShouldBe([100.0, 300.0]); // a bonus 1:3 takes the price to 3/4
    }

    [Fact]
    public async Task No_bars_gives_an_empty_adjustment()
    {
        Applied(new DateOnly(2026, 9, 1), CorporateActionKind.Split, 1, 5);

        var bars = await ReadAsync(Timeframe.D1, [], adjusted: true);

        (bars.Items.Count, bars.Adjustment!.Applied, bars.Adjustment.Factor).ShouldBe((0, 0, 1.0));
    }

    [Fact]
    public async Task The_reusable_service_gives_the_factors_of_the_applied_actions_only()
    {
        Applied(new DateOnly(2026, 9, 1), CorporateActionKind.Split, 1, 5);
        _data.Actions.Add(CorporateAction.Suggest(_abb.Id, new DateOnly(2026, 6, 1), CorporateActionKind.Split, 1, 2, null, Now));

        var factors = await new AdjustmentFactorsProvider(_data).GetAsync(_abb.Id, Ct);
        var none = await new AdjustmentFactorsProvider(_data).GetAsync(Guid.NewGuid(), Ct);

        factors.FactorAt(new DateOnly(2026, 8, 31)).ShouldBe(0.2);
        factors.FactorAt(new DateOnly(2026, 9, 1)).ShouldBe(1.0);
        none.Actions.ShouldBeEmpty();
    }
}
