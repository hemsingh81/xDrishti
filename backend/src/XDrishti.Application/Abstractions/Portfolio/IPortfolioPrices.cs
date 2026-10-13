using XDrishti.Domain.Market.Actions;

namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>A stored day close (the <c>1D</c> aggregate, raw: not adjusted for corporate actions) on an India date.</summary>
public sealed record StoredClose(DateOnly Date, double Close);

/// <summary>The day closes of one instrument, oldest first, and its applied corporate actions (by ex-date) to adjust them with on read (ADR 0017 §5).</summary>
public sealed record StoredPrices(IReadOnlyList<StoredClose> Closes, IReadOnlyList<AppliedAction> Actions);

/// <summary>The stored daily prices the portfolio history is built from (ADR 0020 §1). One read for all the instruments asked for.</summary>
public interface IPortfolioPrices
{
    /// <summary>
    /// The day closes from <paramref name="from"/> to <paramref name="toInclusive"/> (India dates) of all the instruments in one query, and the applied actions of the same instruments in one more, never one query per
    /// instrument or per day. An instrument without any stored day bar in the range is missing from the answer.
    /// </summary>
    ValueTask<IReadOnlyDictionary<Guid, StoredPrices>> ReadDailyAsync(
        IReadOnlyCollection<Guid> instrumentIds, DateOnly from, DateOnly toInclusive, CancellationToken cancellationToken);
}
