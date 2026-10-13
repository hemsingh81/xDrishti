using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Portfolio;
using XDrishti.Application.Tests.Market;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using static XDrishti.Application.Tests.Portfolio.History.HistoryData;

namespace XDrishti.Application.Tests.Portfolio.History;

/// <summary>"Track my holdings" and "Track the benchmark" through the bulk tracking command (acceptance criterion 9 of P2-07).</summary>
public sealed class TrackPortfolioHandlerTests
{
    private readonly TrackingFixture _tracking = new();

    private readonly FakeHoldingQueries _holdings = new();

    private readonly FakePortfolioBenchmark _benchmarks = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private TrackPortfolioHoldingsHandler Holdings() =>
        new(_holdings, _tracking.Data, _tracking.Track, NullLogger<TrackPortfolioHoldingsHandler>.Instance);

    private TrackBenchmarkHandler Benchmark() =>
        new(_benchmarks, _tracking.Track, NullLogger<TrackBenchmarkHandler>.Instance);

    private Task<Result<PortfolioTrackResult>> TrackHoldingsAsync(Guid? account = null, Guid? bucket = null) =>
        Holdings().HandleAsync(new TrackPortfolioHoldingsCommand(account, bucket, TrackingFixture.Owner), Ct).AsTask();

    private Guid Listed(string symbol, bool retired = false)
    {
        var instrument = _tracking.Data.AddInstrument(symbol, retired);
        return instrument.Id;
    }

    private static PortfolioTrackResult Ok(Result<PortfolioTrackResult> result)
    {
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code + ": " + result.Error.Message : string.Empty);
        return result.Value;
    }

    [Fact]
    public async Task The_open_holdings_that_are_in_the_master_and_untracked_are_tracked_and_the_hint_says_to_run_Update_data()
    {
        var a = Listed("NSE:AAA");
        var b = Listed("NSE:BBB");
        _holdings.Holdings.AddRange([Holding(a, Lot(1, 1m, 5)), Holding(b, Lot(1, 1m, 5)), Holding(a, Lot(2, 1m, 6))]);

        var result = Ok(await TrackHoldingsAsync());

        result.ShouldBe(new PortfolioTrackResult(2, 2, 0, 0, 0, 2, TrackPortfolioHoldingsHandler.UpdateDataHint));
        _tracking.Data.Tracked.Select(t => t.Id).ShouldBe([a, b], ignoreOrder: true);
        _tracking.Data.Events.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Doing_it_again_adds_nothing_and_says_so()
    {
        var a = Listed("NSE:AAA");
        _holdings.Holdings.Add(Holding(a, Lot(1, 1m, 5)));
        await TrackHoldingsAsync();

        var again = Ok(await TrackHoldingsAsync());

        again.ShouldBe(new PortfolioTrackResult(1, 0, 1, 0, 0, 1, TrackPortfolioHoldingsHandler.NothingHint));
        _tracking.Data.Events.Count.ShouldBe(1);
    }

    [Fact]
    public async Task An_instrument_the_owner_untracked_is_tracked_again()
    {
        var a = Listed("NSE:AAA");
        _holdings.Holdings.Add(Holding(a, Lot(1, 1m, 5)));
        await TrackHoldingsAsync();
        _tracking.Data.Tracked.Single().Deactivate(DateTimeOffset.UtcNow);

        var result = Ok(await TrackHoldingsAsync());

        result.Added.ShouldBe(1);
        _tracking.Data.Tracked.Single().Active.ShouldBeTrue();
    }

    [Fact]
    public async Task Holdings_outside_the_master_are_counted_and_retired_instruments_are_skipped_and_closed_holdings_ignored()
    {
        var a = Listed("NSE:AAA");
        var gone = Listed("NSE:GONE", retired: true);
        var old = Listed("NSE:OLD");
        _holdings.Holdings.AddRange(
        [
            Holding(a, Lot(1, 1m, 5)),
            Holding(null, Lot(1, 1m, 5)),
            Holding(gone, Lot(1, 1m, 5)),
            HoldingFixtures.Holding(1m, "OLD", "closed", old, Sold(1, 1m, 5, 6, 2m)),
        ]);

        var result = Ok(await TrackHoldingsAsync());

        (result.Requested, result.Added, result.NotInMaster, result.Retired).ShouldBe((1, 1, 1, 1));
        _tracking.Data.Tracked.Select(t => t.Id).ShouldBe([a]);
    }

    [Fact]
    public async Task Only_the_scope_s_holdings_are_tracked()
    {
        var a = Listed("NSE:AAA");
        var b = Listed("NSE:BBB");
        _holdings.Holdings.Add(Holding(a, Lot(1, 1m, 5)));
        _holdings.Holdings.Add(HoldingFixtures.Holding(1m, "BBB", "open", b, Lot(1, 1m, 5)) with { AccountId = HistoryFixture.OtherAccount, BucketId = HistoryFixture.OtherBucket });

        var byAccount = Ok(await TrackHoldingsAsync(account: HistoryFixture.OtherAccount));
        var unknown = Ok(await TrackHoldingsAsync(account: Guid.NewGuid()));

        byAccount.Added.ShouldBe(1);
        _tracking.Data.Tracked.Select(t => t.Id).ShouldBe([b]);
        (unknown.Requested, unknown.Added, unknown.TotalTracked).ShouldBe((0, 0, 1));
    }

    [Fact]
    public async Task Nothing_to_track_calls_no_tracking_and_reports_the_total()
    {
        _holdings.Holdings.Add(Holding(null, Lot(1, 1m, 5)));

        var result = Ok(await TrackHoldingsAsync());

        (result.Requested, result.Added, result.NotInMaster, result.TotalTracked).ShouldBe((0, 0, 1, 0));
        _tracking.Data.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task More_than_five_hundred_instruments_are_tracked_in_chunks()
    {
        for (var i = 0; i < TrackingLimits.MaxIdsPerCall + 1; i++)
        {
            _holdings.Holdings.Add(Holding(Listed($"NSE:S{i}"), Lot(1, 1m, 5)));
        }

        var result = Ok(await TrackHoldingsAsync());

        (result.Requested, result.Added, result.TotalTracked).ShouldBe((501, 501, 501));
        _tracking.Data.Tracked.Count.ShouldBe(501);
    }

    [Fact]
    public async Task The_benchmark_is_tracked_idempotently()
    {
        var nifty = _benchmarks.Add("NSE:NIFTY", "Nifty 50");
        _benchmarks.Default = nifty.Id;
        _tracking.Data.Instruments.Add(new TrackableInstrument(nifty.Id, nifty.Symbol, false));

        var first = Ok(await Benchmark().HandleAsync(new TrackBenchmarkCommand(TrackingFixture.Owner), Ct));
        var second = Ok(await Benchmark().HandleAsync(new TrackBenchmarkCommand(TrackingFixture.Owner), Ct));

        first.ShouldBe(new PortfolioTrackResult(1, 1, 0, 0, 0, 1, TrackPortfolioHoldingsHandler.UpdateDataHint));
        second.ShouldBe(new PortfolioTrackResult(1, 0, 1, 0, 0, 1, TrackBenchmarkHandler.AlreadyTrackedHint));
        _tracking.Data.Tracked.ShouldHaveSingleItem().Id.ShouldBe(nifty.Id);
    }

    [Fact]
    public async Task Tracking_a_benchmark_the_master_does_not_have_is_not_found()
    {
        var result = await Benchmark().HandleAsync(new TrackBenchmarkCommand(TrackingFixture.Owner), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("portfolio.not_found");
        result.Error.Kind.ShouldBe(ErrorKind.NotFound);
    }

    [Fact]
    public async Task A_retired_benchmark_index_cannot_be_tracked()
    {
        var old = _benchmarks.Add("NSE:OLD_INDEX", "Old index", retired: true);
        _benchmarks.Chosen = old.Id;

        var result = await Benchmark().HandleAsync(new TrackBenchmarkCommand(TrackingFixture.Owner), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("portfolio.invalid");
        _tracking.Data.Tracked.ShouldBeEmpty();
    }
}
