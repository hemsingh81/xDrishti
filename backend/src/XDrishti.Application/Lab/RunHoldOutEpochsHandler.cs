using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Lab;

namespace XDrishti.Application.Lab;

/// <summary>The evening run of the hold-out epochs (HO-01 D10).</summary>
public sealed record RunHoldOutEpochsCommand : ICommand;

/// <summary>What the evening run did: a one-line summary (words and block dates, no figure of the evidence) and how many steps failed.</summary>
public sealed record HoldOutEpochsOutcome(string Summary, int Failed);

/// <summary>
/// Runs the seal steps in order (HO-01 D10, ADR 0283). First it reads the chain (states are derived from the calendar, so a block is "marked sealed" by the day's close, never by a write) and puts the block in force and its state in the summary; then each registered
/// <see cref="IHoldOutSealStep"/> runs on its own: a step that fails or throws is recorded as failed and does not stop the next, and it leaves the block as it was. A chain that cannot be read fails the run with nothing done. Places no order.
/// </summary>
internal sealed class RunHoldOutEpochsHandler(HoldOutEpochs epochs, IEnumerable<IHoldOutSealStep> steps, ILogger<RunHoldOutEpochsHandler> logger) : ICommandHandler<RunHoldOutEpochsCommand, HoldOutEpochsOutcome>
{
    public async ValueTask<Result<HoldOutEpochsOutcome>> HandleAsync(RunHoldOutEpochsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        string head;
        try
        {
            var chain = await epochs.StatusAsync(cancellationToken);
            var current = chain.FirstOrDefault(b => b.State != HoldOutBlockState.Released);
            head = current is null ? "no block" : $"block {current.Block.Start:yyyy-MM-dd} {HoldOutChain.Word(current.State)}";
        }
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
        {
            LabRunLog.HoldOutSealStepFailed(logger, "chain", failure.GetType().Name);
            return Result.Failure<HoldOutEpochsOutcome>(LabErrors.HoldOutUnavailable("The hold-out chain could not be read; nothing was done."));
        }

        var parts = new List<string> { head };
        var failed = 0;
        foreach (var step in steps.OrderBy(s => s.Order).ThenBy(s => s.Name, StringComparer.Ordinal))
        {
            try
            {
                parts.Add($"{step.Name}: {await step.RunAsync(cancellationToken)}");
            }
            catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
            {
                failed++;
                LabRunLog.HoldOutSealStepFailed(logger, step.Name, failure.GetType().Name);
                parts.Add($"{step.Name}: failed");
            }
        }

        return Result.Success(new HoldOutEpochsOutcome(string.Join("; ", parts), failed));
    }
}
