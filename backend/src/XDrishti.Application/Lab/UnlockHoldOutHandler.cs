using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;

namespace XDrishti.Application.Lab;

/// <summary>
/// The owner's unlock (spec CF-06, D5; part C): the password was checked at the endpoint; this refuses an empty actor, checks the cheap preconditions with the same answers the pass would give, and queues a <c>holdout</c> job on the Lab dispatcher.
/// The pass itself runs there. A job of this run that is already queued or running is returned instead of a second one.
/// </summary>
internal sealed class UnlockHoldOutHandler(HoldOutUnlocker unlocker, ILabRunStore runs, TimeProvider time) : ICommandHandler<UnlockHoldOutCommand, LabRunView>
{
    public async ValueTask<Result<LabRunView>> HandleAsync(UnlockHoldOutCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.Actor))
        {
            return Result.Failure<LabRunView>(Error.Forbidden("lab.no_owner", "The hold-out is unlocked by the owner."));
        }

        var plan = await unlocker.CheckAsync(command.RunId, cancellationToken);
        if (plan.IsFailure)
        {
            return Result.Failure<LabRunView>(plan.Error);
        }

        var run = plan.Value.Run;
        var labels = new[] { HoldOutJob.Label(run.Id, command.Actor), "Hold-out look: the frozen plan of this run's version, run once over the locked block; the result is stored beside the report." };
        var queued = await runs.InsertOrGetAsync(
            new NewLabRun(Guid.NewGuid(), HoldOutJob.KeyOf(run.Id), run.StrategyId, run.Version, run.StrategyHash, run.Universe, run.From, run.To, HoldOutJob.Method, HoldOutJob.ConfigHash, run.EngineVersion, labels, time.GetUtcNow()),
            cancellationToken);
        return Result.Success(LabRunView.Of(queued.Run));
    }
}

/// <summary>Reads the hold-out: the block (or the proposal) and the looks taken. Numbers only in a look that was taken.</summary>
internal sealed class GetHoldOutHandler(IHoldOutStore store, LabHoldOutGuard guard, HoldOutOptions options, HoldOutEpochs epochs) : IQueryHandler<GetHoldOutQuery, Result<HoldOutView>>
{
    public async ValueTask<Result<HoldOutView>> HandleAsync(GetHoldOutQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var chain = await epochs.StatusAsync(cancellationToken);
        var inForce = chain.FirstOrDefault(b => b.State != XDrishti.Domain.Lab.HoldOutBlockState.Released);
        HoldOutPlanView? proposed = null;
        if (inForce is null && await guard.HistoryAsync(cancellationToken) is { } h)
        {
            var p = guard.Plan(h.First, h.Last);
            proposed = new HoldOutPlanView(p.HistoryMonths, p.LengthMonths, p.DevelopmentMonths, p.Folds, p.Start, p.Status);
        }

        var unlocks = await store.ListUnlocksAsync(200, cancellationToken);
        return Result.Success(new HoldOutView(
            options.Enforce,
            options.AutoUnlock,
            inForce is null ? null : BlockViews.Of(inForce),
            proposed,
            [.. unlocks.Select(u => u.Result)],
            [.. chain.Select(BlockViews.Of)],
            options.AutoRelease));
    }
}
