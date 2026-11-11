using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Replay.Job;

/// <summary>Cancels a queued or running replay (SR-01, AC 15): the job sees it after the current session, the finished sessions are kept, and the run reads <c>cancelled</c>.</summary>
internal sealed class CancelReplayHandler(IReplayStore store, IReplayReads reads, TimeProvider time) : ICommandHandler<CancelReplayCommand, string>
{
    public async ValueTask<Result<string>> HandleAsync(CancelReplayCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await store.CancelAsync(command.RunId, time.GetUtcNow(), cancellationToken) is { } state)
        {
            return Result.Success(state);
        }

        return await reads.GetRunAsync(command.RunId, cancellationToken) is null
            ? Result.Failure<string>(Error.NotFound("replay.not_found", "No such replay run."))
            : Result.Failure<string>(Error.Conflict("replay.not_active", "The replay has finished; there is nothing to cancel."));
    }
}
