using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Lab;

namespace XDrishti.Application.Lab;

/// <summary>
/// Cancels a Lab run (spec P5-02, criterion 6). A queued run becomes cancelled at once and its event is sent here; a running run is only asked to stop - the job notices between instruments, stores nothing and ends the run as cancelled, and
/// sends the event itself.
/// </summary>
internal sealed class CancelLabRunHandler(ILabRunStore runs, ILabProgress progress, TimeProvider time, Microsoft.Extensions.Logging.ILogger<CancelLabRunHandler> logger) : ICommandHandler<CancelLabRunCommand, LabRunView>
{
    public async ValueTask<Result<LabRunView>> HandleAsync(CancelLabRunCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var outcome = await runs.RequestCancelAsync(command.Id, time.GetUtcNow(), cancellationToken);
        var run = await runs.GetAsync(command.Id, cancellationToken);
        if (outcome == LabCancelOutcome.NotFound || run is null)
        {
            return Result.Failure<LabRunView>(LabErrors.RunNotFound(command.Id));
        }

        if (outcome == LabCancelOutcome.NotCancellable)
        {
            return Result.Failure<LabRunView>(LabErrors.NotCancellable(LabRunStates.Code(run.State)));
        }

        if (outcome == LabCancelOutcome.Cancelled)
        {
            LabRunLog.Cancelled(logger, run.Id, "queued");
            await progress.FinishedAsync(new LabRunFinished(run.Id, LabRunStates.Code(run.State)), cancellationToken);
        }

        return Result.Success(LabRunView.Of(run));
    }
}
