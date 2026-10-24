using XDrishti.Domain.Planning;

namespace XDrishti.Application.Abstractions.Planning;

/// <summary>A stored suggestion.</summary>
/// <param name="Id">The row id.</param>
/// <param name="PlanId">The plan it is about.</param>
/// <param name="Suggestion">What was stored.</param>
/// <param name="CreatedAt">When.</param>
public sealed record StoredSuggestion(long Id, long PlanId, NewSuggestion Suggestion, DateTimeOffset CreatedAt);

/// <summary>
/// The owner's suggestion inbox (<c>workflow.suggestions</c>, spec P7-09, ADR 0092): append-only. The same plan, kind and subject is stored once, so generating twice stores once. Nothing here places an order or approves a ticket.
/// </summary>
public interface ISuggestionStore
{
    /// <summary>Stores the suggestions of a plan in one transaction; one that is already stored (same plan, kind, subject) is skipped. Returns how many rows were written.</summary>
    ValueTask<int> InsertAsync(long planId, IReadOnlyList<NewSuggestion> suggestions, DateTimeOffset at, CancellationToken cancellationToken);

    /// <summary>The suggestions of a plan in id order.</summary>
    ValueTask<IReadOnlyList<StoredSuggestion>> ForPlanAsync(long planId, CancellationToken cancellationToken);
}
