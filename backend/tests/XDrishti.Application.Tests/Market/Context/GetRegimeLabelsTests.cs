using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Application.Market.Context;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Market.Context;

namespace XDrishti.Application.Tests.Market.Context;

/// <summary>P6-02 (ADR 0146): the labels per session: a label or the reason, counts per regime and basis with zeros shown, the range checks.</summary>
public sealed class GetRegimeLabelsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IContextFigures _figures = Substitute.For<IContextFigures>();

    private readonly IContextDayStore _store = Substitute.For<IContextDayStore>();

    private GetRegimeLabelsHandler Handler(Func<DateOnly, ContextRead> script, int storedDays = 0)
    {
        _figures.AsOfManyAsync(Arg.Any<IReadOnlyCollection<DateOnly>>(), Arg.Any<CancellationToken>())
            .Returns(ci => (IReadOnlyDictionary<DateOnly, ContextRead>)ci.Arg<IReadOnlyCollection<DateOnly>>().ToDictionary(d => d, script));
        var start = new DateOnly(2025, 1, 1);
        _store.ListAsync(Arg.Any<CancellationToken>()).Returns([.. Enumerable.Range(0, storedDays).Select(i => new ContextDayRecord(start.AddDays(i), 100, 90, 95, 15, 375, 375))]);
        var calendar = Substitute.For<ITradingCalendar>();
        calendar.GetAsync(Arg.Any<CancellationToken>()).Returns(new TradingCalendar());
        return new GetRegimeLabelsHandler(_figures, _store, calendar, new FakeTimeProvider(new DateTimeOffset(2026, 3, 11, 12, 0, 0, TimeSpan.Zero)));
    }

    private static ContextRead Regime(DateOnly date, int code, string basis) =>
        ContextRead.Of(new ContextFigures(date.AddDays(-1), basis, 1, 20, 50, 15, 40, 0, 1, 0, code, null));

    [Fact]
    public async Task Each_session_has_a_label_or_a_reason_and_the_counts_show_every_regime_even_with_zero_days()
    {
        var handler = Handler(d => d.DayOfWeek == DayOfWeek.Monday ? ContextRead.Not(ContextRead.BeforeHistory) : d.Day % 2 == 0 ? Regime(d, 1, ContextBasis.IndexAndVix) : Regime(d, 9, ContextBasis.IndexOnly), 300);

        var result = await handler.HandleAsync(new GetRegimeLabelsQuery("2026-03-02", "2026-03-11"), Ct);

        result.IsSuccess.ShouldBeTrue();
        var view = result.Value;
        view.Sessions.ShouldBe(8);
        view.Days.Select(x => x.Date.Day).ShouldBe([2, 3, 4, 5, 6, 9, 10, 11]);
        view.Days[0].Unavailable.ShouldBe(ContextRead.BeforeHistory);
        view.Days[0].Regime.ShouldBeNull();
        view.Days[1].Regime.ShouldBe("DOWN_HIGH_VOL");
        view.Days[1].Basis.ShouldBe(ContextBasis.IndexOnly);
        view.Days[2].Regime.ShouldBe("UP_LOW_VOL");
        view.Days[2].ContextAsOf.ShouldBe(new DateOnly(2026, 3, 3));
        view.Counts.Count.ShouldBe(9);
        view.Counts[0].ShouldBe(new RegimeCountView("UP_LOW_VOL", 1, 3, 0));
        view.Counts[8].ShouldBe(new RegimeCountView("DOWN_HIGH_VOL", 9, 0, 3));
        view.Counts[4].ShouldBe(new RegimeCountView("RANGE_NORMAL_VOL", 5, 0, 0));
        view.Unavailable.ShouldBe([new RegimeUnavailableView(ContextRead.BeforeHistory, 2)]);
        view.ContextDays.ShouldBe(300);
        view.FirstLabel.ShouldBe(new DateOnly(2025, 1, 1).AddDays(265));
        view.Note.ShouldContain("read before the day");
    }

    [Fact]
    public async Task With_no_range_given_the_last_90_days_up_to_the_last_closed_session_are_shown()
    {
        var result = await Handler(d => ContextRead.Not(ContextRead.NoContext)).HandleAsync(new GetRegimeLabelsQuery(null, null), Ct);

        result.Value.To.ShouldBe(new DateOnly(2026, 3, 11), "17:30 India time on the 11th: the session of the 11th has closed");
        result.Value.From.ShouldBe(new DateOnly(2026, 3, 11).AddDays(-90));
        result.Value.Days.ShouldAllBe(d => d.Unavailable == ContextRead.NoContext);
        result.Value.FirstLabel.ShouldBeNull();
        result.Value.FirstIndexAndVix.ShouldBeNull();
    }

    [Theory]
    [InlineData("2026-13-01", null)]
    [InlineData(null, "tomorrow")]
    [InlineData("2026-03-10", "2026-03-01")]
    [InlineData("2024-01-01", "2026-03-10")]
    public async Task A_range_that_cannot_be_read_is_in_the_wrong_order_or_is_too_long_is_refused(string? from, string? to)
    {
        var result = await Handler(_ => ContextRead.Not(ContextRead.NoContext)).HandleAsync(new GetRegimeLabelsQuery(from, to), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("data.invalid");
        await _figures.DidNotReceiveWithAnyArgs().AsOfManyAsync(default!, Ct);
    }
}
