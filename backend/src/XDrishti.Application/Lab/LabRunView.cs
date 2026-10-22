using XDrishti.Application.Abstractions.Lab;
using XDrishti.Domain.Lab;

namespace XDrishti.Application.Lab;

/// <summary>The strategy version a run tests.</summary>
public sealed record LabStrategyRef(string Id, int Version, string Hash);

/// <summary>The India dates a run replays, inclusive.</summary>
public sealed record LabWindow(DateOnly From, DateOnly To);

/// <summary>Where a running run is: instruments done of instruments in total (0 and 0 outside the quick test).</summary>
public sealed record LabProgressView(int Done, int Total);

/// <summary>
/// A Lab run as the API serves it (spec P5-02, Contract): the same data the hub's events carry, so a missed event costs nothing. <c>verdict</c> is always null in this slice (P5-03); <c>labels</c> carry the cost basis, the sample and the line that a
/// quick test is exploration, not a verdict (D6).
/// </summary>
public sealed record LabRunView(
    Guid Id,
    string Key,
    string State,
    string? Stage,
    LabStrategyRef Strategy,
    LabUniverse Universe,
    LabWindow Window,
    string Method,
    LabProgressView Progress,
    IReadOnlyList<string> SimRunKeys,
    LabCounts? Counts,
    IReadOnlyList<LabProblem> Problems,
    IReadOnlyList<LabStageLog> Stages,
    IReadOnlyList<string> Labels,
    string? Error,
    bool CancelRequested,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    LabVerdictView? Verdict)
{
    /// <summary>The served form of a stored run.</summary>
    public static LabRunView Of(LabRun run, LabVerdictView? verdict = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        return new LabRunView(
            run.Id,
            run.Key,
            LabRunStates.Code(run.State),
            run.Stage,
            new LabStrategyRef(run.StrategyId, run.Version, run.StrategyHash),
            run.Universe,
            new LabWindow(run.From, run.To),
            run.Method,
            new LabProgressView(run.Done, run.Total),
            run.SimRunKeys,
            run.Counts,
            run.Problems,
            run.Stages,
            run.Labels,
            run.Error,
            run.CancelRequested,
            run.CreatedAt,
            run.StartedAt,
            run.FinishedAt,
            verdict);
    }
}
