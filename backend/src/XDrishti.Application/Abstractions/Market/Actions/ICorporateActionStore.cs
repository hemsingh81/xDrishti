using XDrishti.Domain.Market.Actions;

namespace XDrishti.Application.Abstractions.Market.Actions;

/// <summary>
/// The rows of <c>market.corporate_actions</c> a use case changes. Rows are tracked: changes are saved by <see cref="IMarketUnitOfWork"/>, together with the audit event.
/// The runtime role may insert and update them, never delete.
/// </summary>
public interface ICorporateActionStore
{
    ValueTask<CorporateAction?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Whether an action of the instrument has this ex-date and kind, apart from <paramref name="except"/> (the one being edited).</summary>
    ValueTask<bool> ExistsAsync(Guid instrumentId, DateOnly exDate, CorporateActionKind kind, Guid? except, CancellationToken cancellationToken);

    /// <summary>
    /// The actions of these instruments that are not ignored (suggested and applied), tracked so a use case can ignore a suggestion in the same save. Used to see what another kind on the same
    /// ex-date is up against.
    /// </summary>
    ValueTask<IReadOnlyList<CorporateAction>> ListNotIgnoredAsync(IReadOnlyCollection<Guid> instrumentIds, CancellationToken cancellationToken);

    void Add(CorporateAction action);

    /// <summary>
    /// Stores suggestions the detection found, each in its own right and without failing when one exists already (same instrument, ex-date and kind): the detection is idempotent
    /// even when two runs overlap. Returns how many were new. Not tracked: saved at once.
    /// </summary>
    ValueTask<int> AddSuggestionsAsync(IReadOnlyCollection<CorporateAction> suggestions, CancellationToken cancellationToken);
}
