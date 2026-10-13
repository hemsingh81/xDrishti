using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Returns;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Actions;

namespace XDrishti.Application.Portfolio;

/// <summary>
/// Totals of the holdings of one account, one bucket, or (without an account) every active account that has the Portfolio role and counts in totals:
/// invested, current value, unrealised and realised P&amp;L, XIRR from the union of all cash flows, day change, counts, lots on the default date, the price times and the last sync, and the benchmark XIRR and alpha of the same cash flows (ADR 0020):
/// the same holdings and lots as the XIRR, put into the benchmark index at its stored day closes (carried forward to the valuation date); one more read, of the index's bars only.
/// </summary>
public sealed record GetHoldingsSummaryQuery(Guid? AccountId, Guid? BucketId) : IQuery<Result<HoldingsSummary>>;

internal sealed class GetHoldingsSummaryHandler(
    IHoldingQueries queries,
    IHoldingSyncStore syncs,
    IAccountStore accounts,
    IPortfolioBenchmark benchmarks,
    IPortfolioPrices prices,
    ITradingCalendar calendar,
    TimeProvider clock) : IQueryHandler<GetHoldingsSummaryQuery, Result<HoldingsSummary>>
{
    public const string AlphaBasisLastClose = "last_close";

    public const string AlphaBasisLiveVsLastClose = "live_vs_last_close";

    public async ValueTask<Result<HoldingsSummary>> HandleAsync(GetHoldingsSummaryQuery query, CancellationToken cancellationToken)
    {
        if (query.AccountId is { } accountId && await accounts.FindReadOnlyAsync(accountId, cancellationToken) is null)
        {
            return Result.Failure<HoldingsSummary>(AccountErrors.NotFound);
        }

        var valuationDate = IndiaTime.Today(clock.GetUtcNow());
        var holdings = await queries.ListAsync(new HoldingFilter(query.AccountId, query.BucketId, null, null, TotalsOnly: query.AccountId is null), cancellationToken);
        var figures = ReturnsCalculator.ForGroup(holdings, valuationDate);
        var open = holdings.Where(h => h.IsOpen).ToList();
        var dayChange = DayChangeCalculator.ForGroup(holdings);
        var last = await syncs.GetLastAsync(query.AccountId, cancellationToken);
        var benchmark = await BenchmarkOfAsync(holdings, valuationDate, cancellationToken);
        var alpha = BenchmarkReturns.Alpha(figures.Xirr, benchmark);
        var priceTimes = HoldingViews.PriceTimes(holdings);
        var newestPrice = priceTimes.Newest;
        return Result.Success(new HoldingsSummary(
            HoldingViews.Returns(figures),
            open.Count,
            holdings.Count - open.Count,
            open.Count(h => h.InstrumentId is null),
            open.Count(h => !h.IsPriced),
            open.Sum(h => h.Lots.Count(l => l.IsOpen && l.UsesDefaultDate)),
            valuationDate,
            last is null ? null : HoldingViews.Run(last),
            dayChange.Amount,
            dayChange.Pct,
            dayChange.Excluded,
            priceTimes,
            BenchmarkReturns.Percent(benchmark),
            alpha,
            BenchmarkStateCodes.Name(benchmark.State),
            benchmark.AsOf,
            await AlphaBasisAsync(benchmark, alpha, newestPrice, cancellationToken)));
    }

    /// <summary>The benchmark XIRR of the holdings the portfolio XIRR is made of (those with a usable price), from the benchmark index's stored closes.</summary>
    private async ValueTask<BenchmarkXirrResult> BenchmarkOfAsync(IReadOnlyList<HoldingData> holdings, DateOnly valuationDate, CancellationToken cancellationToken)
    {
        var priced = holdings.Where(h => h.IsPriced && h.Lots.Count > 0).ToList();
        if (priced.Count == 0)
        {
            return new BenchmarkXirrResult(BenchmarkState.Insufficient, null, BenchmarkAvailability.NoFlows);
        }

        if (await benchmarks.GetAsync(cancellationToken) is not { } configured)
        {
            return new BenchmarkXirrResult(BenchmarkState.Unavailable, null, BenchmarkAvailability.NoBars);
        }

        var earliest = priced.SelectMany(h => h.Lots).Min(l => l.AcquiredOn);
        var stored = await prices.ReadDailyAsync([configured.Instrument.Id], earliest.AddDays(-GetPortfolioHistoryHandler.LookbackDays), valuationDate, cancellationToken);
        var closes = stored.TryGetValue(configured.Instrument.Id, out var own) ? AdjustedCloses.Adjust(own.Closes, AdjustmentFactors.From(own.Actions)) : [];
        return BenchmarkReturns.Compute(priced, valuationDate, closes, await calendar.GetAsync(cancellationToken));
    }

    /// <summary>
    /// What the alpha compares: "live_vs_last_close" when the newest price of the portfolio is dated a later trading day than the index close the benchmark was valued at (the index has no live quote), otherwise "last_close". Null without an alpha.
    /// </summary>
    private async ValueTask<string?> AlphaBasisAsync(BenchmarkXirrResult benchmark, decimal? alpha, DateTimeOffset? newestPrice, CancellationToken cancellationToken)
    {
        if (alpha is null || benchmark.AsOf is not { } asOf)
        {
            return null;
        }

        var priceDate = newestPrice is { } at ? IndiaTime.Today(at) : asOf;
        var later = priceDate > asOf && (await calendar.GetAsync(cancellationToken)).TradingDaysBetween(asOf, priceDate) > 0;
        return later ? AlphaBasisLiveVsLastClose : AlphaBasisLastClose;
    }
}
