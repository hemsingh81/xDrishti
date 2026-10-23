using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Planning;

/// <summary>
/// Produces the owner's suggestions for a stored plan by the deterministic rules (P7-09, ADR 0092) and appends the new ones to the inbox. Rules only, no model. It changes no plan and no ticket and cannot approve or place anything.
/// </summary>
/// <param name="ForDate">The planned session; null for the newest stored plan.</param>
public sealed record GenerateSuggestionsCommand(DateOnly? ForDate) : ICommand;

/// <summary>What a suggestions run did.</summary>
/// <param name="PlanId">The plan looked at; null when there is none.</param>
/// <param name="ForDate">Its planned session.</param>
/// <param name="Produced">Suggestions the rules produced.</param>
/// <param name="Stored">Of those, how many were new (the rest were already stored for the plan, kind and subject).</param>
/// <param name="Suggestions">The produced suggestions.</param>
public sealed record SuggestionsOutcome(long? PlanId, DateOnly? ForDate, int Produced, int Stored, IReadOnlyList<NewSuggestion> Suggestions);
