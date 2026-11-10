using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Planning;

/// <summary>The auto scope in force (spec AU-16). Read only.</summary>
public sealed record GetAutoScopeQuery : IQuery<AutoScopeView>;

/// <summary>Sets the auto scope as the owner: baskets and individual instruments (master ids), at least one, all known. Appends a version and an audit row; nothing is edited.</summary>
/// <param name="Actor">The owner, <c>owner:&lt;e-mail&gt;</c>.</param>
/// <param name="BasketIds">The baskets.</param>
/// <param name="InstrumentIds">The individual instruments.</param>
public sealed record SetAutoScopeCommand(string Actor, IReadOnlyList<Guid>? BasketIds, IReadOnlyList<Guid>? InstrumentIds) : ICommand;

/// <summary>The scope in force. With none set, <c>IsSet</c> is false and every eligible instrument is considered.</summary>
/// <param name="IsSet">Whether a scope was ever set.</param>
/// <param name="Version">The version row, or null.</param>
/// <param name="BasketIds">The baskets.</param>
/// <param name="InstrumentIds">The individual instruments.</param>
/// <param name="Hash">The scope hash that plans carry, or null.</param>
/// <param name="ChangedBy">Who set it, or null.</param>
/// <param name="ChangedAt">When, or null.</param>
/// <param name="Summary"><c>eligible</c> with no scope, else the counts in words.</param>
public sealed record AutoScopeView(bool IsSet, long? Version, IReadOnlyList<Guid> BasketIds, IReadOnlyList<Guid> InstrumentIds, string? Hash, string? ChangedBy, DateTimeOffset? ChangedAt, string Summary);

/// <summary>The next-day suggestions of the plan for a date, newest plan of the date (spec AU-16).</summary>
/// <param name="ForDate">The session; null for the newest plan.</param>
public sealed record GetAutoSuggestionsQuery(DateOnly? ForDate) : IQuery<Result<AutoSuggestionsView>>;

/// <summary>What to look at for one session, with the basis and the words that no order is placed.</summary>
/// <param name="ForDate">The session.</param>
/// <param name="PlanId">The plan the suggestions come from.</param>
/// <param name="PlanState">The state of that plan.</param>
/// <param name="Basis">The plan's basis text.</param>
/// <param name="Notice">Always <see cref="AutoSuggestionRules.NoOrderNotice"/>.</param>
/// <param name="ScopeHash">The scope hash stored on the plan, or null when it was built without a scope.</param>
/// <param name="Suggestions">Primary tickets with all required evidence.</param>
/// <param name="NotSuggested">Primary tickets missing required evidence, with why.</param>
/// <param name="Reserve">Reserve tickets with reasons.</param>
/// <param name="Rejected">Rejected candidates with reasons.</param>
public sealed record AutoSuggestionsView(
    DateOnly ForDate,
    long PlanId,
    string PlanState,
    string Basis,
    string Notice,
    string? ScopeHash,
    IReadOnlyList<AutoSuggestionView> Suggestions,
    IReadOnlyList<AutoSuggestionView> NotSuggested,
    IReadOnlyList<AutoSuggestionView> Reserve,
    IReadOnlyList<AutoSuggestionView> Rejected);

/// <summary>One suggestion: what, why and on what evidence.</summary>
/// <param name="ItemId">The plan item.</param>
/// <param name="Rank">Order in the plan.</param>
/// <param name="Symbol">Instrument.</param>
/// <param name="Side">BUY or SELL.</param>
/// <param name="StrategyId">Strategy.</param>
/// <param name="StrategyVersionHash">Strategy version.</param>
/// <param name="Levels">Indicative levels as of the close of the session, or null.</param>
/// <param name="Reasons">Why, in words.</param>
/// <param name="Evidence">The evidence figures.</param>
/// <param name="Why">For a ticket not suggested: which evidence is missing; otherwise null.</param>
public sealed record AutoSuggestionView(
    long ItemId,
    int Rank,
    string Symbol,
    string Side,
    string StrategyId,
    string StrategyVersionHash,
    TicketLevelsView? Levels,
    IReadOnlyList<string> Reasons,
    AutoEvidenceView Evidence,
    string? Why);

/// <summary>The evidence figures as text; a missing figure reads <c>unavailable</c> and is never a pass.</summary>
/// <param name="N">Trades in the cell.</param>
/// <param name="NEff">Independent n.</param>
/// <param name="ConservativeExpectancyR">Conservative expectancy in R.</param>
/// <param name="Regime">The regime of the cell, <c>all regimes</c> for a cell that spans them.</param>
/// <param name="DataAsOf">The date the cell statistics are as of.</param>
public sealed record AutoEvidenceView(string N, string NEff, string ConservativeExpectancyR, string Regime, string DataAsOf);
