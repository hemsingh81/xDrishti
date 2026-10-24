namespace XDrishti.Domain.Planning;

/// <summary>One ticket of a plan as the suggestion rules read it.</summary>
/// <param name="Group">Primary, reserve or rejected.</param>
/// <param name="Symbol">The instrument.</param>
/// <param name="Reasons">Reason codes, for example <c>cost:unavailable</c>.</param>
public sealed record SuggestionItem(TicketGroup Group, string Symbol, IReadOnlyList<string> Reasons);

/// <summary>A stored plan as the suggestion rules read it.</summary>
/// <param name="ForDate">The planned session.</param>
/// <param name="Basis">The plan's basis text (it carries the banner flags).</param>
/// <param name="Items">Every ticket of the plan.</param>
/// <param name="StatsAsOf">The date of the newest cell statistics before the plan date; null when none is stored.</param>
public sealed record SuggestionInput(DateOnly ForDate, string Basis, IReadOnlyList<SuggestionItem> Items, DateOnly? StatsAsOf);
