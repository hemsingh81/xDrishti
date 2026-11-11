using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Replay;
using XDrishti.Domain.Replay.Comparison;

namespace XDrishti.Application.Replay.Comparison;

/// <summary>The two runs of a pair and their comparison.</summary>
internal sealed record ReplayPairRun(ReplayRunResult Frozen, ReplayRunResult Learning, ReplayPairResult Comparison);

/// <summary>SR-01 part D: runs the same parameters once frozen and once learning, in memory, and compares them. Part C stores the runs and calls this per pair.</summary>
internal sealed class ReplayPairRunner(ReplayDriver driver)
{
    public async ValueTask<Result<ReplayPairRun>> RunAsync(ReplayParams parameters, CancellationToken cancellationToken, LoopOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var frozen = await driver.RunAsync(parameters with { Mode = ReplayMode.Frozen }, cancellationToken);
        if (frozen.IsFailure)
        {
            return Result.Failure<ReplayPairRun>(frozen.Error);
        }

        var learning = await driver.RunAsync(parameters with { Mode = ReplayMode.Learning }, cancellationToken);
        if (learning.IsFailure)
        {
            return Result.Failure<ReplayPairRun>(learning.Error);
        }

        var comparison = ReplayPairComparison.Compare(Series(frozen.Value), Series(learning.Value), parameters.Capital, options);
        return comparison.IsFailure ? Result.Failure<ReplayPairRun>(comparison.Error) : Result.Success(new ReplayPairRun(frozen.Value, learning.Value, comparison.Value));
    }

    private static List<ReplayDayEquity> Series(ReplayRunResult run) => [.. run.Sessions.Select(s => new ReplayDayEquity(s.Session, s.Equity))];
}
