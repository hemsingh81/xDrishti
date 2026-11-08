using XDrishti.Application.Abstractions.Lab;
using XDrishti.Domain.Lab;

namespace XDrishti.Application.Lab;

/// <summary>The strategy version a run tests.</summary>
public sealed record LabStrategyRef(string Id, int Version, string Hash);

/// <summary>The India dates a run replays, inclusive.</summary>
public sealed record LabWindow(DateOnly From, DateOnly To);

/// <summary>Where a running run is: instruments done of instruments in total (0 and 0 outside the quick test).</summary>
public sealed record LabProgressView(int Done, int Total);

/// <summary>The options a run was posted with, as the effective values (CF-05, ADR 0229): <c>slippage</c> is <c>default</c> or <c>conservative</c>; <c>stress</c> is whether the cost stress block was wanted (on for <c>robust</c> unless posted off, off for the others unless posted on). <c>Nulls</c> is false when the chance and plateau block was posted left out (CF-07, ADR 0231) and <c>Draws</c> is the posted number of random-entry seeds (null: the configured number). <c>IsDefault</c> is true when all are what a post without options gets; only such a run may be the scorecard's evidence basis.</summary>
public sealed record LabRunOptionsView(string Slippage, bool Stress, bool IsDefault, bool Nulls = true, int? Draws = null);

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
    LabVerdictView? Verdict,
    LabRunOptionsView? Options = null)
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
            verdict,
            OptionsOf(run));
    }

    private static LabRunOptionsView OptionsOf(LabRun run)
    {
        var options = LabRunOptions.Parse(run.RunOptions);
        var robust = string.Equals(run.Method, LabSettings.RobustMethod, StringComparison.Ordinal);
        return new LabRunOptionsView(options.ConservativeSlippage ? "conservative" : "default", options.StressOn(robust), options.KeyText(robust) is null, !options.NullsOff, options.Draws);
    }
}
