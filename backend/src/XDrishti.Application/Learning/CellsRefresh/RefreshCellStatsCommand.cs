using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Learning.CellsRefresh;

/// <summary>Recomputes the cell statistics as of the last closed session from the sources of the newest snapshot (spec LC-01, ADR 0277). Only the service sends it.</summary>
/// <param name="Actor"><c>system:cells-refresh</c> for a scheduled run, the owner for "Run now".</param>
public sealed record RefreshCellStatsCommand(string Actor) : ICommand;

/// <summary>What a refresh did.</summary>
/// <param name="AsOf">The as-of date of the snapshot (the last closed session).</param>
/// <param name="DataThrough">The refresh end: the last date any source reaches.</param>
/// <param name="SnapshotKey">The snapshot key.</param>
/// <param name="AlreadyStored">The same snapshot was stored already; nothing was written.</param>
/// <param name="Kept">Sources kept as they were (up to date or not registered).</param>
/// <param name="Extended">Sources simulated again over a longer window.</param>
/// <param name="NewRuns">Runs newly stored by this refresh (each is one ledger row).</param>
public sealed record RefreshCellStatsOutcome(DateOnly AsOf, DateOnly DataThrough, string SnapshotKey, bool AlreadyStored, IReadOnlyList<string> Kept, IReadOnlyList<string> Extended, int NewRuns)
{
    /// <summary>The run message: what was done and the drift note (every message has it).</summary>
    public string Message =>
        $"Snapshot {SnapshotKey[..12]} as of {AsOf:yyyy-MM-dd} {(AlreadyStored ? "was stored already; nothing written" : "stored")}: {Kept.Count} source(s) kept, {Extended.Count} simulated again, {NewRuns} new run(s). "
        + XDrishti.Domain.Learning.CellsRefreshRules.DriftNote(DataThrough) + ".";
}
