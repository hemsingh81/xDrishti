namespace XDrishti.Application.Abstractions.LiveTrading;

/// <summary>
/// The stored market facts the execution feedback needs (spec P11-02): the tick, the 20-session average daily turnover before a session, the open of the 1-minute bar an order was created in, and the extreme prices of the bars of an order's life. Reads of
/// stored data only; nothing here calls a broker. A fact that does not exist is null, never zero.
/// </summary>
public interface ILiveExecutionMarket
{
    /// <summary>The tick in rupees from the instrument master; null when the master has none or it is outside (0, 1].</summary>
    ValueTask<decimal?> TickAsync(string symbol, CancellationToken cancellationToken);

    /// <summary>The mean of close times volume over the 20 daily bars before the date, in crore rupees; null with fewer than 20 bars.</summary>
    ValueTask<decimal?> AdvTurnoverCrAsync(string symbol, DateOnly date, CancellationToken cancellationToken);

    /// <summary>The open of the 1-minute bar that contains the instant; null when there is no such bar.</summary>
    ValueTask<decimal?> MinuteOpenAsync(string symbol, DateTimeOffset at, CancellationToken cancellationToken);

    /// <summary>The lowest low and the highest high of the 1-minute bars from the minute containing <paramref name="from"/> to the minute containing <paramref name="to"/>; both null without a bar.</summary>
    ValueTask<(decimal? Low, decimal? High)> MinuteExtremesAsync(string symbol, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
