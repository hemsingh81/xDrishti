using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>
/// An active price alert as the quote poll needs it. <see cref="SecurityId"/> is Dhan's id of the instrument in the NSE equity segment (null when the master has none: the alert cannot be quoted and stays active).
/// </summary>
public sealed record ActiveAlert(Guid Id, Guid InstrumentId, string Symbol, string? SecurityId, AlertDirection Direction, decimal Level);

/// <summary>The quote poll's side of the price alerts (ADR 0022): which alerts are active, and triggering one.</summary>
public interface IPriceAlerts
{
    /// <summary>Every active alert (at most 200): one read.</summary>
    ValueTask<IReadOnlyList<ActiveAlert>> ListActiveAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Sets the alert <c>triggered</c> with the price and time, but only if it is still <c>active</c> (one conditional statement): true for the run that did it, false for a run that lost the race or an alert cancelled meanwhile, so an alert
    /// triggers at most once. Executes at once, inside the transaction of <see cref="IPortfolioUnitOfWork.RunAsync"/> when there is one, so that the change and its audit event commit together.
    /// </summary>
    ValueTask<bool> TryTriggerAsync(Guid alertId, decimal price, DateTimeOffset at, CancellationToken cancellationToken);
}
