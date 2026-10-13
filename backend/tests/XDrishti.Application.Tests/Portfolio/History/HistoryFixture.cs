using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Calendar;
using static XDrishti.Application.Tests.Portfolio.History.HistoryData;

namespace XDrishti.Application.Tests.Portfolio.History;

/// <summary>
/// The history handler on in-memory ports: the holdings of the scope, the stored day closes, the benchmark setting, the calendar and the cache, with a controllable clock (start: Saturday 2026-10-10 10:00 India, no session).
/// </summary>
internal sealed class HistoryFixture
{
    public static readonly Guid OtherAccount = Guid.Parse("00000000-0000-7000-8000-0000000000a2");

    public static readonly Guid OtherBucket = Guid.Parse("00000000-0000-7000-8000-0000000000b2");

    /// <summary>Saturday 2026-10-10 10:00 IST.</summary>
    public static readonly DateTimeOffset Saturday = new(2026, 10, 10, 4, 30, 0, TimeSpan.Zero);

    /// <summary>Monday 2026-10-12 10:00 IST: the session is open.</summary>
    public static readonly DateTimeOffset MondayMorning = new(2026, 10, 12, 4, 30, 0, TimeSpan.Zero);

    public FakeTimeProvider Clock { get; } = new(Saturday);

    public FakeHoldingQueries Holdings { get; } = new();

    public FakePortfolioPrices Prices { get; } = new();

    public FakePortfolioBenchmark Benchmarks { get; } = new();

    public FakeHistoryCache Cache { get; } = new();

    public TradingCalendar Calendar { get; set; } = Weekdays;

    public FakeTradingCalendar TradingCalendar => new(Calendar);

    public BenchmarkInstrument Nifty { get; }

    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    public HistoryFixture()
    {
        Nifty = Benchmarks.Add("NSE:NIFTY", "Nifty 50", tracked: true, hasBars: true);
        Benchmarks.Default = Nifty.Id;
    }

    public GetPortfolioHistoryHandler Handler() =>
        new(Holdings, Benchmarks, Prices, TradingCalendar, Cache, Clock, NullLogger<GetPortfolioHistoryHandler>.Instance);

    public Task<Result<PortfolioHistory>> HistoryAsync(HistoryRequest? request = null) =>
        Handler().HandleAsync(new GetPortfolioHistoryQuery(request ?? Request()), Ct).AsTask();

    public async Task<PortfolioHistory> OkAsync(HistoryRequest? request = null)
    {
        var result = await HistoryAsync(request);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code + ": " + result.Error.Message : string.Empty);
        return result.Value;
    }

    public static HistoryRequest Request(Guid? account = null, Guid? bucket = null, string? from = null, string? to = null, string? resolution = null) => new(account, bucket, from, to, resolution);

    /// <summary>Stores the closes of the day numbers (October 2026) for the instrument.</summary>
    public void Store(Guid instrument, params (int Day, double Close)[] closes) => Prices.Set(instrument, closes.Select(c => (Oct(c.Day), c.Close)));

    public void StoreBenchmark(params (int Day, double Close)[] closes) => Store(Nifty.Id, closes);

    public HoldingData Add(Guid? instrument, params LotData[] lots)
    {
        var holding = Holding(instrument, lots);
        Holdings.Holdings.Add(holding);
        return holding;
    }
}
