using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Assistant.Drafting;

namespace XDrishti.Application.Ai.Drafting;

/// <summary>Asks whether a model answer is an acceptable draft (spec P9-07 SubmitDraft): the validator and the checks C3 to C6. Writes nothing, calls no model.</summary>
public sealed record SubmitDraftQuery(string Idea, DraftAnswer Answer) : IQuery<DraftEvaluation>;

/// <summary>
/// What the validator and the checks said. <paramref name="Status"/> is <c>valid</c> (the Lab validator accepts the text; findings may still be there and are shown), <c>invalid</c> or <c>declined</c>. <see cref="NeedsRepair"/> is true for an invalid text and for any
/// finding: it is the reason for one more model turn.
/// </summary>
public sealed record DraftEvaluation(string Status, string? Yaml, IReadOnlyList<StrategyIssue> Problems, IReadOnlyList<DraftFinding> Findings, string? Id)
{
    public bool NeedsRepair => Status == DraftStatus.Invalid || Findings.Count > 0;
}
