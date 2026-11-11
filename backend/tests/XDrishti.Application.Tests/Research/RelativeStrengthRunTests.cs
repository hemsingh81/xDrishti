#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
#pragma warning disable xUnit1051 // The NSubstitute matchers take Arg.Any<CancellationToken>(), not a token
using NSubstitute;
using XDrishti.Application.Abstractions.Market.Strength;
using XDrishti.Application.Market.Baskets;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Tests.Research;

/// <summary>
/// BK-09 through the handler: a strategy that names <c>mom_rank</c>, <c>rs_vs_index</c> or <c>sector_rs</c> gets the stored rows of the instrument (only sessions before the window end), the run records the hash of the rows, the basket ids, the basis (dated or single snapshot), "members as of" and the
/// parameter hash in its header, key and label; a strategy that names none reads nothing and its key is unchanged.
/// </summary>
public sealed class RelativeStrengthRunTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Guid Nifty100 = Guid.Parse("00000000-0000-7000-8000-0000000000b1");

    private static void SetBasket(RunFixture fixture, bool dated)
    {
        fixture.Baskets.ListAsync(false, Arg.Any<CancellationToken>()).Returns(
            [new BasketView(Nifty100, "NSE Nifty 100", "#111111", "watch", null, 0, 1, DateTimeOffset.UnixEpoch, null, "index", true, null, null)]);
        var at = DateTimeOffset.UnixEpoch;
        BasketHistoryEntry[] entries =
        [
            new(Guid.NewGuid(), "added", at, Guid.NewGuid(), "A", "A", "share", null, null),
            new(Guid.NewGuid(), "added", dated ? at.AddDays(30) : at.AddMinutes(1), Guid.NewGuid(), "B", "B", "share", null, null),
        ];
        fixture.Baskets.HistoryAsync(Nifty100, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new BasketHistory(Nifty100, "NSE Nifty 100", entries));
    }

    private static StrengthRow Row(Guid instrument, DateOnly asOf, double? pct, string state = "value") =>
        new(asOf, instrument, Nifty100, 126, 21, 1.0, pct, 12, state, "single_snapshot");

    [Fact]
    public async Task A_strategy_that_names_a_relative_strength_feature_reads_the_rows_before_the_window_end_and_records_them_in_the_header_the_key_and_the_label()
    {
        var fixture = new RunFixture { };
        fixture.Engine.ReadsRelativeStrength = true;
        var abb = fixture.Add("NSE:ABB", RunFixture.ThreeDays());
        SetBasket(fixture, dated: true);
        fixture.Strength.ForInstrumentAsync(abb, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(
        [
            Row(abb, RunFixture.D1, 0.8),
            Row(abb, RunFixture.D2, null, "insufficient"),
            new StrengthRow(RunFixture.D1, abb, Guid.Empty, 20, 0, 2.5, null, 0, "value", "none"),
        ]);

        var result = await fixture.RunAsync(RunFixture.List([abb], RunFixture.D1, RunFixture.D3), Ct);

        result.IsSuccess.ShouldBeTrue();
        await fixture.Strength.Received(1).ForInstrumentAsync(abb, RunFixture.D1.AddDays(-14), RunFixture.D3.AddDays(-1), Arg.Any<CancellationToken>());
        var points = fixture.Engine.Replays.Single().RelativeStrength.ShouldNotBeNull().Points;
        points.Select(p => (p.Feature, p.P1, p.P2, p.P3, double.IsNaN(p.Value) ? -1 : p.Value)).ShouldBe(
        [
            ("rs_vs_index", 20, 0, 0, 2.5),
            ("mom_rank", 2, 126, 21, 0.8),
            ("mom_rank", 2, 126, 21, -1),
        ], ignoreOrder: true, "the code of Nifty 100 is 2; a state that is not a value reads NaN, never a number");
        var record = result.Value.RelativeStrength.ShouldNotBeNull();
        (record.Rows, record.MembersAsOf, record.Version).ShouldBe((3, RunFixture.D3, "rsf-1"));
        var basket = record.Baskets.Single();
        (basket.BasketId, basket.Name, basket.Basis, basket.ParamsHash).ShouldBe((Nifty100, "Nifty 100", "dated", null));
        record.Label().ShouldContain("members as of " + RunFixture.D3.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        record.Label().ShouldContain("basket " + Nifty100.ToString("D"));
        RunLabels.Of(result.Value, FillRules.Default).Lines.ShouldContain(record.Label());
        var header = new RunHeader(result.Value.Strategy, result.Value.EngineVersion, result.Value.From, result.Value.To, result.Value.Options, result.Value.AcknowledgeCsvAdjusted, result.Value.Fills, result.Value.Universe, new XDrishti.Domain.Market.Calendar.TradingCalendar(), null, null, null, null, Domain.Research.RunPurpose.Research, record);
        RunAssembler.KeyText(header, result.Value.Instruments, result.Value.CalendarExceptions).ShouldContain("\nrstrength=" + record.Sha256 + "|rsf-1|" + RunFixture.D3.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + "\n");
    }

    [Fact]
    public async Task A_basket_with_changes_on_one_day_only_is_a_single_snapshot_and_the_label_says_survivorship_applies()
    {
        var fixture = new RunFixture();
        fixture.Engine.ReadsRelativeStrength = true;
        var abb = fixture.Add("NSE:ABB", RunFixture.ThreeDays());
        SetBasket(fixture, dated: false);

        var result = await fixture.RunAsync(RunFixture.List([abb], RunFixture.D1, RunFixture.D3), Ct);

        var record = result.Value.RelativeStrength.ShouldNotBeNull();
        record.Baskets.Single().Basis.ShouldBe("single_snapshot");
        record.Label().ShouldContain("survivorship applies");
    }

    [Fact]
    public async Task The_same_rows_give_the_same_hash_and_other_rows_another_run_key()
    {
        var one = new RunFixture();
        var two = new RunFixture();
        var other = new RunFixture();
        foreach (var f in new[] { one, two, other })
        {
            f.Engine.ReadsRelativeStrength = true;
            SetBasket(f, dated: true);
        }

        var a = one.Add("NSE:ABB", RunFixture.ThreeDays());
        var b = two.Add("NSE:ABB", RunFixture.ThreeDays());
        var c = other.Add("NSE:ABB", RunFixture.ThreeDays());
        one.Strength.ForInstrumentAsync(a, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([Row(a, RunFixture.D1, 0.8)]);
        two.Strength.ForInstrumentAsync(b, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([Row(b, RunFixture.D1, 0.8)]);
        other.Strength.ForInstrumentAsync(c, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([Row(c, RunFixture.D1, 0.2)]);

        var first = (await one.RunAsync(RunFixture.List([a], RunFixture.D1, RunFixture.D3), Ct)).Value;
        var second = (await two.RunAsync(RunFixture.List([b], RunFixture.D1, RunFixture.D3), Ct)).Value;
        var changed = (await other.RunAsync(RunFixture.List([c], RunFixture.D1, RunFixture.D3), Ct)).Value;

        first.RelativeStrength!.Rows.ShouldBe(1);
        changed.RelativeStrength!.Sha256.ShouldNotBe(first.RelativeStrength.Sha256);
        second.RelativeStrength!.Sha256.ShouldNotBe(first.RelativeStrength.Sha256, "the instrument id is part of the digest");
    }

    [Fact]
    public async Task A_strategy_that_names_none_reads_nothing_and_has_no_relative_strength_record()
    {
        var fixture = new RunFixture();
        var abb = fixture.Add("NSE:ABB", RunFixture.ThreeDays());

        var result = await fixture.RunAsync(RunFixture.List([abb], RunFixture.D1, RunFixture.D3), Ct);

        result.Value.RelativeStrength.ShouldBeNull();
        await fixture.Strength.DidNotReceiveWithAnyArgs().ForInstrumentAsync(default, default, default, default);
        fixture.Engine.Replays.ShouldAllBe(i => i.RelativeStrength == null);
    }
}
