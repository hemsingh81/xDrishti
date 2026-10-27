using XDrishti.Application.Abstractions.Ai;
using XDrishti.Domain.Assistant.Proposals;

namespace XDrishti.Application.Ai.Proposals;

/// <summary>The knob of a hypothesis proposal.</summary>
public sealed record ProposalKnobView(string Name, string Kind, string BaselineValue, string CandidateValue);

/// <summary>The payload as the API shows it: the fields of the kind are set, the others are null.</summary>
public sealed record ProposalPayloadView(string? BaselineRunKey, string? CellKey, ProposalKnobView? Knob, Guid? BasketId, string? Symbol);

/// <summary>The decision on a proposal. <paramref name="Auto"/> is true for the auto option: such a decision is never an owner approval.</summary>
public sealed record ProposalDecisionView(string State, string Actor, bool Auto, string? Reason, string? ResultRef, DateTimeOffset DecidedAt);

/// <summary>One proposal as the API shows it (spec P9-08 Contract). The text fields are text: a screen renders them as text.</summary>
public sealed record ProposalView(
    long Id,
    string Kind,
    string Subject,
    string State,
    string Reason,
    ProposalPayloadView Payload,
    ProposalEvidence Evidence,
    IReadOnlyList<ProposalCheck> Checks,
    string Effect,
    IReadOnlyList<string> Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string CreatedBy,
    string? ModelId,
    ProposalDecisionView? Decision)
{
    internal static ProposalView Of(StoredProposal p, DateTimeOffset now)
    {
        var payload = ProposalJson.ReadPayload(p.Kind, p.PayloadJson);
        var view = payload switch
        {
            HypothesisPayload h => new ProposalPayloadView(h.BaselineRunKey, h.CellKey, new ProposalKnobView(h.KnobName, h.KnobKind, h.BaselineValue, h.CandidateValue), null, null),
            BasketPayload b => new ProposalPayloadView(null, null, null, b.BasketId, b.Symbol),
            _ => new ProposalPayloadView(null, null, null, null, null),
        };
        var decision = p.Decision is { } d ? new ProposalDecisionView(d.Decision, d.Actor, d.Auto, d.Reason, d.ResultRef, d.DecidedAt) : null;
        var notes = p.Kind == ProposalKind.Hypothesis ? new[] { ProposalWording.AcceptNote, ProposalWording.TrialNote } : [ProposalWording.AcceptNote];
        return new ProposalView(
            p.Id, p.Kind, p.Subject, ProposalLifecycle.StateOf(p.Decision?.Decision, p.ExpiresAt, now), p.Reason, view, ProposalJson.ReadEvidence(p.EvidenceJson), ProposalJson.ReadChecks(p.ChecksJson), EffectOf(p), notes, p.CreatedAt, p.ExpiresAt, p.CreatedBy, p.ModelId, decision);
    }

    /// <summary>What accepting does, in words, built from the subject the engine made (never from the model's text).</summary>
    internal static string EffectOf(StoredProposal p) => p.Kind switch
    {
        ProposalKind.BasketAdd when p.Subject.StartsWith("Add ", StringComparison.Ordinal) => "Adds " + p.Subject[4..] + ".",
        ProposalKind.BasketRemove when p.Subject.StartsWith("Remove ", StringComparison.Ordinal) => "Removes " + p.Subject[7..] + ".",
        ProposalKind.Hypothesis => "Writes hypothesis " + p.Subject + " as proposed; it will not run or change anything until you approve it.",
        _ => p.Subject,
    };
}

/// <summary>A proposal the engine refused, by kind and refusal code (never the model's text).</summary>
public sealed record ProposalRefusedView(string Kind, string Code);

/// <summary>
/// What one generation run did (spec P9-08 AC 13). <paramref name="State"/>: <c>stored</c>, <c>nothing_stored</c>, <c>nothing_to_propose</c>, <c>model_unavailable</c>, <c>answer_shape</c>, <c>busy</c>. A run that stores nothing changes nothing else.
/// <paramref name="Previews"/> holds the subjects a dry run would have stored.
/// </summary>
public sealed record ProposalRunView(string State, string? Reason, int PackCells, int PackCandidates, int Read, int Stored, int AutoAccepted, IReadOnlyList<ProposalRefusedView> Refused, IReadOnlyList<string> Previews, string? PackHash, bool DryRun);
