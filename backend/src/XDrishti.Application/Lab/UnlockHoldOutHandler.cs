using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;

namespace XDrishti.Application.Lab;

/// <summary>The owner's unlock (spec CF-06, D5): the password was checked at the endpoint; this refuses an empty actor and runs the one look.</summary>
internal sealed class UnlockHoldOutHandler(HoldOutUnlocker unlocker) : ICommandHandler<UnlockHoldOutCommand, HoldOutResultView>
{
    public ValueTask<Result<HoldOutResultView>> HandleAsync(UnlockHoldOutCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return string.IsNullOrWhiteSpace(command.Actor)
            ? ValueTask.FromResult(Result.Failure<HoldOutResultView>(Error.Forbidden("lab.no_owner", "The hold-out is unlocked by the owner.")))
            : unlocker.UnlockAsync(command.RunId, HoldOutUnlocker.OwnerMode, command.Actor, cancellationToken);
    }
}

/// <summary>Reads the hold-out: the block (or the proposal) and the looks taken. Numbers only in a look that was taken.</summary>
internal sealed class GetHoldOutHandler(IHoldOutStore store, LabHoldOutGuard guard, HoldOutOptions options) : IQueryHandler<GetHoldOutQuery, Result<HoldOutView>>
{
    public async ValueTask<Result<HoldOutView>> HandleAsync(GetHoldOutQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var block = await store.CurrentAsync(cancellationToken);
        HoldOutPlanView? proposed = null;
        if (block is null && await guard.HistoryAsync(cancellationToken) is { } h)
        {
            var p = guard.Plan(h.First, h.Last);
            proposed = new HoldOutPlanView(p.HistoryMonths, p.LengthMonths, p.DevelopmentMonths, p.Folds, p.Start, p.Status);
        }

        var unlocks = await store.ListUnlocksAsync(200, cancellationToken);
        return Result.Success(new HoldOutView(
            options.Enforce,
            options.AutoUnlock,
            block is null ? null : new HoldOutBlockView(block.Start, block.HistoryFirst, block.HistoryLast, block.LengthMonths, block.SettingHash, block.CreatedAt),
            proposed,
            [.. unlocks.Select(u => u.Result)]));
    }
}
