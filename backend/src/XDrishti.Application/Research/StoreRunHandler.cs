using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Research;

/// <summary>
/// Keeps a finished run (spec P3-10, D8): validates it without the database, reads the instant once, asks the store to write the header and the trades in one transaction and logs the outcome (ids 4415 to 4418). A conflict under a stored
/// key is logged at error and returned as <c>store.run_conflict</c>; an unexpected exception is logged by code and raised again (the host decides the exit code). Nothing of the record, the trades or the strategy is logged.
/// </summary>
internal sealed class StoreRunHandler(IRunStore store, TimeProvider time, ILogger<StoreRunHandler> logger) : ICommandHandler<StoreRunCommand, StoreOutcome>
{
    public async ValueTask<Result<StoreOutcome>> HandleAsync(StoreRunCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var problems = StoreRunValidation.Check(command.Run, command.Record, command.Actor);
        if (problems.Count > 0)
        {
            return Result.Failure<StoreOutcome>(StoreErrors.RunInvalid(problems));
        }

        var run = command.Run!;
        var shortKey = run.RunKey[..12];
        var started = time.GetTimestamp();
        StoreLog.Started(logger, shortKey, run.Trades.Count, run.OpenAtEnd.Count);
        Result<StoreOutcome> stored;
        try
        {
            stored = await store.StoreAsync(new RunToStore(run, command.Record, command.Actor, time.GetUtcNow()), cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            StoreLog.Failed(logger, shortKey, StoreErrors.WriteFailedCode);
            throw;
        }

        if (stored.IsSuccess)
        {
            var outcome = stored.Value.WasAlreadyStored ? "already stored" : "stored";
            var totalMs = (long)time.GetElapsedTime(started).TotalMilliseconds;
            StoreLog.Completed(logger, shortKey, run.Trades.Count, run.OpenAtEnd.Count, outcome, stored.Value.CopyMilliseconds, totalMs);
        }
        else if (stored.Error.Code == StoreErrors.RunConflictCode)
        {
            StoreLog.Conflict(logger, shortKey, string.Join(", ", (stored.Error.Fields ?? []).Select(f => f.Field)));
        }
        else
        {
            StoreLog.Failed(logger, shortKey, stored.Error.Code);
        }

        return stored;
    }
}
