using XDrishti.Domain.Learning;

namespace XDrishti.Application.Abstractions.Confidence;

/// <summary>Which gate a lifecycle move is judged by (spec CF-11 D6).</summary>
public enum ReadinessGateKind
{
    /// <summary>Gate R: a cell goes from Candidate to Paper.</summary>
    ResearchToPaper = 0,

    /// <summary>Gate S: a cell goes from Paper to Active.</summary>
    PaperToActive = 1,
}

/// <summary>
/// The moves the readiness gates judge: Candidate to Paper and the owner's re-enable of a paused cell (Paused to Paper, CF-12 D3), both by gate R, and Paper to Active by gate S. Every other move (a pause, a retirement, a revert) is not judged by them: they lower risk.
/// </summary>
public static class ReadinessGateKinds
{
    /// <summary>The most evidence ids a gate adds to the row of a move it judges (the snapshot and the result); the lifecycle recorder refuses a gated move whose own evidence leaves less room than this.</summary>
    public const int MaxGateEvidenceIds = 2;

    /// <summary>The gate of a move, or null when none applies.</summary>
    public static ReadinessGateKind? For(CellState from, CellState to) => (from, to) switch
    {
        (CellState.Candidate, CellState.Paper) => ReadinessGateKind.ResearchToPaper,
        (CellState.Paused, CellState.Paper) => ReadinessGateKind.ResearchToPaper,
        (CellState.Paper, CellState.Active) => ReadinessGateKind.PaperToActive,
        _ => null,
    };
}

/// <summary>A gate use: the cell that moves, the gate, and who asks.</summary>
/// <param name="Cell">The cell key (its first part is the strategy).</param>
/// <param name="Kind">The gate.</param>
/// <param name="Actor">Who or what asks (recorded in the snapshot's body).</param>
public sealed record ReadinessGateRequest(string Cell, ReadinessGateKind Kind, string Actor);

/// <summary>The answer of a gate use (spec CF-11 D6): whether the gate passed, whether the move must be refused, and what to cite.</summary>
/// <param name="Passed">True when the gate passed.</param>
/// <param name="Enforced">Whether the owner's enforce switch counts as ON (also true when the switch could not be read: the stricter reading).</param>
/// <param name="Refused">True when the gate did not pass and <paramref name="Enforced"/> is true: the move must not be made.</param>
/// <param name="Gate"><c>R</c> or <c>S</c>.</param>
/// <param name="SnapshotId">The stored gate snapshot, or null when it could not be stored.</param>
/// <param name="FailingRows">The numbers of the rows that stop the gate (an unavailable row among them); for gate S a label that is not "Ready" is named in <paramref name="Message"/>.</param>
/// <param name="Message">The words: what stops it, or that it passed.</param>
/// <param name="EvidenceIds">What the changelog row of the move should cite (the snapshot and the failing rows).</param>
public sealed record ReadinessGateDecision(bool Passed, bool Enforced, bool Refused, string Gate, long? SnapshotId, IReadOnlyList<int> FailingRows, string Message, IReadOnlyList<string> EvidenceIds);

/// <summary>
/// The only route from the cell lifecycle to the readiness scorecard (spec CF-11 D6, ADR 0221). With the owner's enforce switch OFF (the default) a failed gate is a warning and the move goes on; ON it is refused. A gate that cannot be read is not a pass: it reads as a fail,
/// and the switch that cannot be read reads as ON. Nothing that chooses what to approve (Planning, Selection, Autonomy) may use it.
/// </summary>
public interface IReadinessGate
{
    /// <summary>Judges one move and stores the gate snapshot it cites.</summary>
    ValueTask<ReadinessGateDecision> CheckAsync(ReadinessGateRequest request, CancellationToken cancellationToken);
}
