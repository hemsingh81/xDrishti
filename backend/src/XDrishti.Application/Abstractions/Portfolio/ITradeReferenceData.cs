using XDrishti.Domain.Market.Actions;

namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>An NSE share or ETF of the instrument master: its id, ticker (without <c>NSE:</c>), ISIN and type (<c>share</c> or <c>etf</c>).</summary>
public sealed record TradeInstrument(Guid Id, string Symbol, string? Isin, string Type);

/// <summary>What the trades need from the market module (reads only): which securities are NSE shares and ETFs, and the applied corporate actions of instruments.</summary>
public interface ITradeReferenceData
{
    /// <summary>The NSE shares and ETFs of the master (retired ones too) with one of these ISINs or tickers. Ticker matches are upper case without <c>NSE:</c>.</summary>
    ValueTask<IReadOnlyList<TradeInstrument>> FindInstrumentsAsync(IReadOnlyCollection<string> isins, IReadOnlyCollection<string> symbols, CancellationToken cancellationToken);

    /// <summary>The applied (not suggested, not ignored) corporate actions of each instrument, by ex-date ascending; instruments without any are absent.</summary>
    ValueTask<IReadOnlyDictionary<Guid, IReadOnlyList<AppliedAction>>> ListAppliedActionsAsync(IReadOnlyCollection<Guid> instrumentIds, CancellationToken cancellationToken);
}
