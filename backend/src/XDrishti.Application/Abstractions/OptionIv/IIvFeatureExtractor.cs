using XDrishti.Domain.Filings;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Abstractions.OptionIv;

/// <summary>
/// Reads, for the trades of one run, everything the features of <c>iv-1</c> need and hands it to the pure functions of the domain (P14-11, ADR 0199). Every read is bounded by the underlying and the dates of its trades; each trade is computed from what was public at its decision instant.
/// A port so that the use case that stores the rows names no archive type. Research only: no decision path reads the result.
/// </summary>
public interface IIvFeatureExtractor
{
    ValueTask<IvExtraction> ExtractAsync(IReadOnlyList<IvTrade> trades, EventCalendarSnapshot snapshot, TradingCalendar calendar, CancellationToken cancellationToken);
}
