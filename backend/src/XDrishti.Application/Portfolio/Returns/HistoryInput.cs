using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Portfolio.Returns;

/// <summary>
/// Everything <see cref="PortfolioHistoryCalculator"/> needs, already read: the holdings of the scope with their lots, the adjusted day closes of their instruments (by instrument id, oldest first), the benchmark's closes (empty: none),
/// the trading calendar and, optionally, today's live prices.
/// </summary>
public sealed record HistoryInput(
    IReadOnlyList<HoldingData> Holdings,
    IReadOnlyDictionary<Guid, IReadOnlyList<DailyClose>> Closes,
    IReadOnlyList<DailyClose> BenchmarkCloses,
    TradingCalendar Calendar,
    LivePrices? Live);
