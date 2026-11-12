namespace XDrishti.Api.Endpoints;

/// <summary>The baskets and instruments a replay covers; both empty means the owner's stored auto scope, its baskets' members read as of each session.</summary>
internal sealed record ReplayScopeBody(IReadOnlyList<Guid>? BasketIds, IReadOnlyList<Guid>? InstrumentIds);

/// <summary>The body of the start of a replay (SR-01 part C). Defaults: to is the last readable session before the hold-out and its embargo, capital 1000000, the seeded standard profile, gross costs (costs null or "gross"; otherwise the id of a stored charge sheet), versions start (the version active today, a labelled hindsight), modes frozen.</summary>
internal sealed record ReplayStartBody(
    DateOnly From,
    DateOnly? To,
    ReplayScopeBody? Scope,
    decimal? Capital,
    Guid? RiskProfileId,
    string? Costs,
    string? Versions,
    IReadOnlyList<string>? Modes,
    bool FiltersWithoutHistory = false,
    bool IncludeResearchCells = false,
    bool Baseline = false);

/// <summary>The body of the start of a start-date sweep (SR-02): 2 to 12 start dates that share the end date <c>to</c> (default the last readable session) and every other parameter of a start (frozen mode, selection arm).</summary>
internal sealed record ReplaySweepStartBody(
    IReadOnlyList<DateOnly> Starts,
    DateOnly? To,
    ReplayScopeBody? Scope,
    decimal? Capital,
    Guid? RiskProfileId,
    string? Costs,
    string? Versions,
    bool FiltersWithoutHistory = false,
    bool IncludeResearchCells = false);

/// <summary>The answer of a cancel: the new state, cancelled for a run that had not started and cancelling for one that is running.</summary>
internal sealed record ReplayCancelView(string State);
