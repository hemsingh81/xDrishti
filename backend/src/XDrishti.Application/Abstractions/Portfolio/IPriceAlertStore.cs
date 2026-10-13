using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>A master-list instrument as an alert needs it: its type code (<c>share</c>, <c>etf</c>, <c>index</c>, <c>future</c>), exchange (<c>NSE</c>), whether it is retired, and whether Dhan has an NSE equity id for it (it can be quoted).</summary>
public sealed record AlertInstrument(Guid Id, string Symbol, string Name, string Type, string Exchange, bool Retired, bool Quotable);

/// <summary>The last price known of an instrument and where it comes from: <c>holding</c> (the last price of an open holding of it) or <c>close</c> (its last stored daily close, with the India date of that close in <see cref="AsOf"/>).</summary>
public sealed record KnownPrice(decimal Price, string Source, DateOnly? AsOf = null);

/// <summary>A price alert with its instrument's symbol and name.</summary>
public sealed record AlertRow(
    Guid Id,
    Guid InstrumentId,
    string Symbol,
    string Name,
    AlertDirection Direction,
    decimal Level,
    string? Note,
    AlertStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? TriggeredAt,
    decimal? TriggerPrice);

/// <summary>The owner's side of the price alerts (ADR 0022): create, list, cancel. Reads are projections without tracking; <see cref="Add"/> is collected and saved with its audit event.</summary>
public interface IPriceAlertStore
{
    /// <summary>The master-list instrument with this id; null when it does not exist.</summary>
    ValueTask<AlertInstrument?> FindInstrumentAsync(Guid instrumentId, CancellationToken cancellationToken);

    /// <summary>The holding's last price (the newest price of an open, priced holding of the instrument in any account), else the last stored daily close (however old: the caller judges its age); null when neither is known.</summary>
    ValueTask<KnownPrice?> GetKnownPriceAsync(Guid instrumentId, CancellationToken cancellationToken);

    ValueTask<int> CountActiveAsync(CancellationToken cancellationToken);

    /// <summary>Collects the new alert; <see cref="IPortfolioUnitOfWork"/> saves it with its audit event.</summary>
    void Add(PriceAlert alert);

    ValueTask<AlertRow?> FindAsync(Guid alertId, CancellationToken cancellationToken);

    /// <summary>One page, newest first (ties by id); <paramref name="status"/> null for all. Total counts all matches.</summary>
    ValueTask<(IReadOnlyList<AlertRow> Items, int Total)> ListAsync(AlertStatus? status, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>The alerts that triggered at or after <paramref name="since"/>, newest first.</summary>
    ValueTask<IReadOnlyList<AlertRow>> ListTriggeredSinceAsync(DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>Sets the alert <c>cancelled</c> if it is still <c>active</c> (one conditional statement; executes at once, inside the current transaction): false when it was not active, so a triggered alert stays triggered.</summary>
    ValueTask<bool> TryCancelAsync(Guid alertId, CancellationToken cancellationToken);
}
