using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Abstractions.Market.Calendar;

/// <summary>The rows of <c>market.trading_days</c>. Rows are tracked: changes are saved by <see cref="IMarketUnitOfWork"/>.</summary>
public interface ITradingDayStore
{
    /// <summary>Every row, whatever its status, by date (a few dozen per year).</summary>
    ValueTask<IReadOnlyList<TradingDay>> ListAsync(CancellationToken cancellationToken);

    ValueTask<TradingDay?> FindAsync(DateOnly date, CancellationToken cancellationToken);

    /// <summary>How many days wait for the owner: proposed rows and proposals next to a confirmed row.</summary>
    ValueTask<int> CountProposedAsync(CancellationToken cancellationToken);

    void Add(TradingDay day);

    /// <summary>Deletes the row. A save is refused when the row changed since it was read (row version), so a row the owner just confirmed is never deleted by a pass working from older data.</summary>
    void Remove(TradingDay day);
}
