namespace XDrishti.Domain.Assistant.Proposals;

/// <summary>What a proposal asks for (spec P9-08 AC 2): one record per kind. The model fills it; the engine checks every field against stored data before anything is shown.</summary>
public abstract record ProposalPayload;

/// <summary>A one-knob hypothesis for a cell (AC 4): the stored baseline run, the cell it is about and the knob with its baseline and candidate values (as text, numbers in invariant form).</summary>
public sealed record HypothesisPayload(string BaselineRunKey, string CellKey, string KnobName, string KnobKind, string BaselineValue, string CandidateValue) : ProposalPayload;

/// <summary>A basket change (AC 5): one instrument of the owner's manual basket, added or removed. The kind (add or remove) is the proposal's.</summary>
public sealed record BasketPayload(Guid BasketId, string Symbol) : ProposalPayload;

/// <summary>One item of the model's answer after it was read as data: the model's own subject is kept for the record only (the shown subject is built by the engine from the payload).</summary>
public sealed record ProposalItem(string Kind, string Subject, ProposalPayload Payload, string Reason, IReadOnlyList<string> EvidenceRefs);

/// <summary>One figure of the evidence, built by the engine (P8-00 D1). <paramref name="Value"/> is empty unless <paramref name="State"/> is <c>value</c>: a figure that is insufficient or unavailable shows a word and no digit.</summary>
public sealed record EvidenceFigure(string Label, string Value, string State, int? N);

/// <summary>The evidence of a proposal (AC 3): basis, population run keys, date range, n, figures with states and the honest limits. Built by the engine from the references, never from the model's text.</summary>
public sealed record ProposalEvidence(string Basis, IReadOnlyList<string> Population, string? DateRange, int N, IReadOnlyList<EvidenceFigure> Figures, IReadOnlyList<string> Limits);

/// <summary>One engine check of a proposal: a stable id, whether it passed, and the detail in words.</summary>
public sealed record ProposalCheck(string Code, bool Ok, string Detail);
