#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Abstractions.Reporting;
using XDrishti.Application.Market.Bars;
using XDrishti.Application.Market.Baskets;
using XDrishti.Application.Market.Search;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Tests.Reporting;

/// <summary>P8-07 behind the ports: membership as of a date, one read per shared instrument, the index, the window and the refusals.</summary>
public sealed class GetReportBenchmarksHandlerTests
{
    private static readonly Guid B1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid B2 = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateOnly From = new(2026, 1, 5);
    private static readonly DateOnly To = new(2026, 3, 31);
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    private readonly IBasketQueries _baskets = Substitute.For<IBasketQueries>();
    private readonly IBarQueries _bars = Substitute.For<IBarQueries>();
    private readonly IPortfolioBenchmark _benchmark = Substitute.For<IPortfolioBenchmark>();
    private readonly IReportTradeSource _source = Substitute.For<IReportTradeSource>();
    private readonly Dictionary<Guid, (double First, double Last)> _prices = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public GetReportBenchmarksHandlerTests()
    {
        _source.ListRunsAsync(Arg.Any<CancellationToken>()).Returns(new ValueTask<IReadOnlyList<ReportRunInfo>>([]));
        _benchmark.GetAsync(Arg.Any<CancellationToken>()).Returns(new ValueTask<ConfiguredBenchmark?>((ConfiguredBenchmark?)null));
        _bars.ReadDailyBarsAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var id = call.ArgAt<Guid>(0);
            IReadOnlyList<BarRow> rows = _prices.TryGetValue(id, out var p) ? [Bar(From, p.First), Bar(To, p.Last)] : [];
            return new ValueTask<IReadOnlyList<BarRow>>(rows);
        });
    }

    private GetReportBenchmarksHandler Handler(ReportBenchmarkOptions? options = null) =>
        new(_baskets, _bars, _benchmark, _source, options ?? new ReportBenchmarkOptions(), new FakeTimeProvider(Now), NullLogger<GetReportBenchmarksHandler>.Instance);

    private static BarRow Bar(DateOnly day, double close) =>
        new(new DateTimeOffset(day.Year, day.Month, day.Day, 0, 0, 0, TimeSpan.FromMinutes(330)), close, close, close, close, 1, 1, true, null);

    private Guid Instrument(string symbol, double? first = null, double last = 0)
    {
        var id = Guid.NewGuid();
        if (first is { } f)
        {
            _prices[id] = (f, last);
        }

        _names[id] = symbol;
        return id;
    }

    private readonly Dictionary<Guid, string> _names = [];

    private void Basket(Guid id, string name, DateTimeOffset? archived = null, params Guid[] instruments)
    {
        var items = instruments.Select((i, n) => new BasketMemberView(Guid.NewGuid(), Summary(i), n, Now, null, null, "owner", null, null)).ToList();
        _baskets.MembersAsync(id, Arg.Any<DateTimeOffset?>(), Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<BasketMembers?>(new BasketMembers(id, name, archived, null, items.Count, items)));
    }

    private InstrumentSummary Summary(Guid id) =>
        new(id, _names[id], _names[id], _names[id], "share", null, null, null, 1, null, null, false, false, false, TrackedInfo.Never);

    private async Task<ReportBenchmarksView> OkAsync(GetReportBenchmarksQuery query)
    {
        var result = await Handler().HandleAsync(query, Ct);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        return result.Value;
    }

    private async Task<Error> RefusedAsync(GetReportBenchmarksQuery query, ReportBenchmarkOptions? options = null)
    {
        var result = await Handler(options).HandleAsync(query, Ct);
        result.IsFailure.ShouldBeTrue();
        return result.Error;
    }

    private static GetReportBenchmarksQuery Query(string? basket, string? from = "2026-01-05", string? to = "2026-03-31", string? asOf = null) => new(basket, from, to, asOf);

    [Fact]
    public async Task A_basket_of_three_is_the_mean_of_its_returns_with_the_evidence_block()
    {
        Basket(B1, "Banks", null, Instrument("A", 100, 110), Instrument("B", 50, 45), Instrument("C", 200, 250));

        var view = await OkAsync(Query(B1.ToString()));

        var basket = view.Baskets.ShouldHaveSingleItem();
        basket.Members.ShouldBe(3);
        basket.Included.ShouldBe(3);
        basket.BuyAndHold.State.ShouldBe("value");
        basket.BuyAndHold.Number!.Value.ShouldBe(25.0 / 3, 1e-9);
        view.NonAdditive.ShouldBeTrue();
        view.Window.Source.ShouldBe("query");
        view.Evidence.Basis.ShouldBe("price return, dividends not included");
        view.Evidence.N.ShouldBe(3);
        view.Evidence.Range.ShouldBe(view.Window);
        view.Evidence.Limits.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Members_without_bars_are_named_and_too_few_left_gives_no_number()
    {
        var gone = Instrument("GONE");
        Basket(B1, "Banks", null, Instrument("A", 100, 110), gone, Instrument("B", 50, 45));

        var basket = (await OkAsync(Query(B1.ToString()))).Baskets.Single();

        basket.BuyAndHold.State.ShouldBe("insufficient");
        basket.BuyAndHold.Number.ShouldBeNull();
        basket.Excluded.Select(e => (e.Symbol, e.Reason)).ShouldBe([("GONE", "no_bars")]);
        basket.Excluded[0].InstrumentId.ShouldBe(gone);
    }

    [Fact]
    public async Task An_instrument_in_two_baskets_is_read_once_and_counts_in_both()
    {
        var shared = Instrument("S", 100, 120);
        Basket(B1, "One", null, shared, Instrument("A", 100, 100), Instrument("B", 100, 100));
        Basket(B2, "Two", null, shared, Instrument("C", 100, 100), Instrument("D", 100, 100));

        var view = await OkAsync(Query($"{B1},{B2}"));

        view.Baskets.Select(b => b.Included).ShouldBe([3, 3]);
        await _bars.Received(1).ReadDailyBarsAsync(shared, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
        view.Evidence.N.ShouldBe(5);
    }

    [Fact]
    public async Task Members_are_asked_as_of_the_given_date_and_the_response_prints_it()
    {
        Basket(B1, "Banks", null, Instrument("A", 100, 110));

        var view = await OkAsync(Query(B1.ToString(), asOf: "2026-02-10"));

        view.MembersAsOf.ShouldBe(new DateOnly(2026, 2, 10));
        await _baskets.Received(1).MembersAsync(B1, Arg.Any<DateTimeOffset?>(), new DateOnly(2026, 2, 10), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Without_a_date_or_a_run_the_members_are_as_of_today_in_india()
    {
        Basket(B1, "Banks", null, Instrument("A", 100, 110));

        (await OkAsync(Query(B1.ToString()))).MembersAsOf.ShouldBe(new DateOnly(2026, 10, 20));
    }

    [Fact]
    public async Task The_window_and_the_members_date_come_from_the_default_run_set_when_not_given()
    {
        var run = new ReportRunInfo("k", "ORB", "basket", "label", "basket:" + B1.ToString("D"), From, To, null, null, null, null, null, 10, 0, new DateTimeOffset(2026, 4, 2, 20, 0, 0, TimeSpan.Zero));
        _source.ListRunsAsync(Arg.Any<CancellationToken>()).Returns(new ValueTask<IReadOnlyList<ReportRunInfo>>([run]));
        Basket(B1, "Banks", null, Instrument("A", 100, 110));

        var view = await OkAsync(Query(B1.ToString(), from: null, to: null));

        view.Window.ShouldBe(new BenchmarkWindowView(From, To, "runs", "runs", "runs"));
        view.MembersAsOf.ShouldBe(new DateOnly(2026, 4, 3));
    }

    [Fact]
    public async Task The_index_has_its_return_when_it_has_bars_and_is_unavailable_with_the_reason_when_not()
    {
        Basket(B1, "Banks", null, Instrument("A", 100, 110));
        var nifty = Instrument("NIFTY", 20000, 21000);
        _benchmark.GetAsync(Arg.Any<CancellationToken>()).Returns(new ValueTask<ConfiguredBenchmark?>(new ConfiguredBenchmark(new BenchmarkInstrument(nifty, "NIFTY", "Nifty 50", "index", false, true, true), true)));

        var withBars = await OkAsync(Query(B1.ToString()));
        _prices.Remove(nifty);
        var without = await OkAsync(Query(B1.ToString()));

        withBars.Index.Return.Number!.Value.ShouldBe(5, 1e-9);
        withBars.Index.IsDefault.ShouldBeTrue();
        without.Index.Return.State.ShouldBe("unavailable");
        without.Index.Return.Reason!.ShouldContain("Portfolio");
        without.Evidence.UnavailableInputs.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task No_benchmark_in_the_master_is_unavailable()
    {
        Basket(B1, "Banks", null, Instrument("A", 100, 110));

        var view = await OkAsync(Query(B1.ToString()));

        view.Index.Return.State.ShouldBe("unavailable");
        view.Index.Symbol.ShouldBeNull();
    }

    [Fact]
    public async Task An_archived_or_unknown_basket_is_not_found()
    {
        Basket(B1, "Old", Now);

        (await RefusedAsync(Query(B1.ToString()))).Code.ShouldBe("reports.not_found");
        (await RefusedAsync(Query(B2.ToString()))).Code.ShouldBe("reports.not_found");
    }

    [Theory]
    [InlineData(null, "2026-01-05", "2026-03-31")]
    [InlineData("not-a-guid", "2026-01-05", "2026-03-31")]
    [InlineData("11111111-1111-1111-1111-111111111111", "2026-04-01", "2026-03-31")]
    [InlineData("11111111-1111-1111-1111-111111111111", "5 Jan", "2026-03-31")]
    [InlineData("11111111-1111-1111-1111-111111111111", null, null)]
    public async Task A_malformed_or_missing_input_is_refused_as_invalid(string? basket, string? from, string? to)
    {
        (await RefusedAsync(Query(basket, from, to))).Code.ShouldBe("reports.invalid");
    }

    [Fact]
    public async Task More_than_twelve_baskets_are_refused()
    {
        var ids = string.Join(',', Enumerable.Range(0, 13).Select(_ => Guid.NewGuid()));

        (await RefusedAsync(Query(ids))).Code.ShouldBe("reports.invalid");
    }

    [Fact]
    public async Task A_basket_above_the_instrument_cap_or_the_distinct_cap_or_a_too_long_window_is_refused()
    {
        Basket(B1, "Big", null, Instrument("A", 100, 110), Instrument("B", 100, 110), Instrument("C", 100, 110));

        (await RefusedAsync(Query(B1.ToString()), new ReportBenchmarkOptions { MaxInstruments = 2 })).Code.ShouldBe("reports.invalid");
        (await RefusedAsync(Query(B1.ToString()), new ReportBenchmarkOptions { MaxDistinctInstruments = 2 })).Code.ShouldBe("reports.invalid");
        (await RefusedAsync(Query(B1.ToString()), new ReportBenchmarkOptions { MaxWindowDays = 30 })).Code.ShouldBe("reports.invalid");
    }

    [Fact]
    public async Task Each_bound_of_the_window_says_where_it_came_from()
    {
        var run = new ReportRunInfo("k", "ORB", "basket", "label", "basket:" + B1.ToString("D"), From, To, null, null, null, null, null, 10, 0, Now);
        _source.ListRunsAsync(Arg.Any<CancellationToken>()).Returns(new ValueTask<IReadOnlyList<ReportRunInfo>>([run]));
        Basket(B1, "Banks", null, Instrument("A", 100, 110));

        var view = await OkAsync(Query(B1.ToString(), from: "2026-01-06", to: null));

        view.Window.FromSource.ShouldBe("query");
        view.Window.ToSource.ShouldBe("runs");
        view.Window.Source.ShouldBe("runs");
    }

    [Fact]
    public async Task The_same_stored_bars_give_identical_bytes()
    {
        Basket(B1, "Banks", null, Instrument("A", 100, 110), Instrument("B", 50, 45), Instrument("C", 200, 250), Instrument("GONE"));

        var first = System.Text.Json.JsonSerializer.Serialize(await OkAsync(Query(B1.ToString())));
        var second = System.Text.Json.JsonSerializer.Serialize(await OkAsync(Query(B1.ToString())));

        second.ShouldBe(first);
    }

    [Fact]
    public async Task Every_response_shape_carries_the_evidence_block()
    {
        Basket(B1, "Three", null, Instrument("A", 100, 110), Instrument("B", 50, 45), Instrument("C", 200, 250));
        Basket(B2, "Empty", null, Instrument("X"));

        foreach (var query in new[] { Query(B1.ToString()), Query(B2.ToString()), Query($"{B1},{B2}") })
        {
            var evidence = (await OkAsync(query)).Evidence;

            evidence.Basis.ShouldBe("price return, dividends not included");
            evidence.Limits.ShouldNotBeEmpty();
            evidence.Range.ShouldNotBeNull();
        }
    }

    [Theory]
    [InlineData(nameof(ReportBenchmarkOptions.MinMembers), 0)]
    [InlineData(nameof(ReportBenchmarkOptions.MaxStartGapDays), 61)]
    [InlineData(nameof(ReportBenchmarkOptions.MaxInstruments), 0)]
    [InlineData(nameof(ReportBenchmarkOptions.MaxBaskets), 101)]
    [InlineData(nameof(ReportBenchmarkOptions.MaxDistinctInstruments), 0)]
    [InlineData(nameof(ReportBenchmarkOptions.MaxWindowDays), 1)]
    public void Settings_out_of_range_are_rejected(string name, int value)
    {
        var options = new ReportBenchmarkOptions();
        typeof(ReportBenchmarkOptions).GetProperty(name)!.SetValue(options, value);

        System.ComponentModel.DataAnnotations.Validator.TryValidateObject(options, new System.ComponentModel.DataAnnotations.ValidationContext(options), [], true).ShouldBeFalse();
    }

    [Fact]
    public void The_default_settings_are_valid()
    {
        var options = new ReportBenchmarkOptions();

        System.ComponentModel.DataAnnotations.Validator.TryValidateObject(options, new System.ComponentModel.DataAnnotations.ValidationContext(options), [], true).ShouldBeTrue();
    }
}
