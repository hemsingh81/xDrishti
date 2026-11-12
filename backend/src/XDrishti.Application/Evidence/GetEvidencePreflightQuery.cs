using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Evidence;

/// <summary><c>xd evidence preflight</c> (spec EV-01): which prerequisite of the first-evidence runbook is met, not met or unavailable. A read; it writes nothing.</summary>
public sealed record GetEvidencePreflightQuery : IQuery<EvidencePreflightView>;

/// <summary>One prerequisite: <c>State</c> is <c>MET</c>, <c>NOT MET</c> or <c>UNAVAILABLE</c>; <c>Fix</c> says what to do.</summary>
public sealed record EvidencePreflightItem(string Item, string State, string Reason, string Fix);

/// <summary>The twelve items, in the runbook's order.</summary>
public sealed record EvidencePreflightView(IReadOnlyList<EvidencePreflightItem> Items);
