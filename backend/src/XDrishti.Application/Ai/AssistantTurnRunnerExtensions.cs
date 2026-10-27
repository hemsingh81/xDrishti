using XDrishti.Domain.Common;

namespace XDrishti.Application.Ai;

/// <summary>The one-call form of a turn, for callers without a stream (the evaluation, jobs).</summary>
public static class AssistantTurnRunnerExtensions
{
    public static async ValueTask<Result<TurnResult>> RunAsync(this IAssistantTurnRunner runner, TurnRequest request, Func<TurnEvent, ValueTask> sink, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(sink);
        var begun = await runner.BeginAsync(request, cancellationToken);
        if (begun.IsFailure)
        {
            return Result.Failure<TurnResult>(begun.Error);
        }

        await using var session = begun.Value;
        return Result.Success(await session.RunAsync(sink, cancellationToken));
    }
}
