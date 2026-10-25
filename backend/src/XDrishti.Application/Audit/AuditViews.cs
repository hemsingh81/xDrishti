namespace XDrishti.Application.Audit;

/// <summary>The days a response covers (India dates, inclusive).</summary>
public sealed record AuditWindowView(DateOnly From, DateOnly To);

/// <summary>What happened to one store in this request: <c>value</c> (rows read), <c>empty</c> (none; the reason says whether it was not asked for or had none) or <c>unavailable</c> (the read failed; the reason is a fixed code).</summary>
public sealed record AuditSourceState(string Source, string State, string? Reason);

/// <summary>One row of the audit, redacted. <paramref name="ActorKind"/> is owner, system, rule, cli, other or other_address (a sign-in address that is not the owner's; no address is shown then).</summary>
public sealed record AuditEntryView(
    string Id,
    DateTimeOffset At,
    string Source,
    string Category,
    string Kind,
    string Outcome,
    string ActorKind,
    string? Actor,
    Guid? AccountId,
    string? AccountName,
    string Summary,
    bool Redacted,
    long? PlanId,
    long? ItemId);

/// <summary>A labelled plain-text value.</summary>
public sealed record AuditFieldView(string Label, string Value);

/// <summary>One row with its detail fields (IP address and user agent for a sign-in row; confirm reason, modification and cutoff for an approval).</summary>
public sealed record AuditEntryDetailView(AuditEntryView Entry, IReadOnlyList<AuditFieldView> Fields);

/// <summary>A page of the merged audit. <paramref name="NextCursor"/> is null at the end. <paramref name="Limits"/> are sentences the screen must show.</summary>
public sealed record AuditPageView(AuditWindowView Window, IReadOnlyList<AuditEntryView> Items, string? NextCursor, IReadOnlyList<AuditSourceState> Sources, IReadOnlyList<string> Limits);

/// <summary>A count under a key (source, category or outcome).</summary>
public sealed record AuditCount(string Key, int Count);

/// <summary>Counts of the window; <paramref name="Total"/> is every row of the window, before any filter other than the dates.</summary>
public sealed record AuditSummaryView(AuditWindowView Window, int Total, IReadOnlyList<AuditCount> BySource, IReadOnlyList<AuditCount> ByCategory, IReadOnlyList<AuditCount> ByOutcome, IReadOnlyList<AuditSourceState> Sources);

/// <summary>A kind a store can hold, with its category, outcome and words.</summary>
public sealed record AuditKindView(string Source, string Kind, string Label, string Category, string Outcome);

/// <summary>Every kind per source, for the filter lists.</summary>
public sealed record AuditKindsView(IReadOnlyList<AuditKindView> Kinds, IReadOnlyList<string> Categories, IReadOnlyList<string> Outcomes, IReadOnlyList<string> Actors);
