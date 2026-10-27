using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Assistant.Evaluation;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Assistant.Evaluation;

/// <summary>
/// The owner accepts a run as the baseline (spec P9-09 AC 11, ADR 0143). Refused unless the run is a gating PASS; a set that got worse than the current baseline needs a stated reason, which is stored. At most one baseline row per run; the newest row is the current one.
/// There is no auto option: nothing is applied by it, and an automatic "passed" would defeat the purpose. The actor is the owner's verb, never a model or a job.
/// </summary>
internal sealed class AcceptEvalHandler(IEvalStore store, TimeProvider time, ILogger<AcceptEvalHandler> logger) : ICommandHandler<AcceptEvalCommand, EvalRunView>
{
    public async ValueTask<Result<EvalRunView>> HandleAsync(AcceptEvalCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Actor != EvalErrors.CliActor)
        {
            return Result.Failure<EvalRunView>(Error.Forbidden("assistant.owner_only", "Only the owner's own verb accepts an eval run."));
        }

        var run = await store.GetAsync(command.RunId, cancellationToken);
        if (run is null)
        {
            return Result.Failure<EvalRunView>(EvalErrors.NotFound(command.RunId));
        }

        var summary = EvalMapping.SummaryOf(run);
        var current = await store.CurrentBaselineAsync(cancellationToken);
        var currentRun = current is null ? null : await store.GetAsync(current.RunId, cancellationToken);
        var checkedRun = summary with { NonGating = !run.Gating || summary.NonGating };
        var allowed = EvalAcceptance.Check(checkedRun, currentRun is null || currentRun.Id == run.Id ? null : EvalMapping.SummaryOf(currentRun), command.Reason);
        if (allowed.IsFailure)
        {
            return Result.Failure<EvalRunView>(allowed.Error);
        }

        var reason = string.IsNullOrWhiteSpace(command.Reason) ? null : command.Reason.Trim();
        var (_, created) = await store.InsertBaselineAsync(run.Id, command.Actor, reason, time.GetUtcNow(), cancellationToken);
        EvalLog.Accepted(logger, run.Id, created);
        return Result.Success(EvalMapping.Run(run, summary, true));
    }
}
