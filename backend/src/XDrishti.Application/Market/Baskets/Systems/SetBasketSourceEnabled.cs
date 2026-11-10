using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Market.Baskets;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Application.Market.Baskets;

/// <summary>Turns the fetch of one system basket's file on or off (an owner act: the endpoint needs a step-up, both directions). Audited with the fetch setting's snapshot; idempotent (no change, no audit). It makes no request.</summary>
public sealed record SetBasketSourceEnabledCommand(Guid BasketId, bool Enabled, string Actor) : ICommand;

/// <summary>The body of the switch.</summary>
public sealed record SourceSwitchInput(bool? Enabled);

internal sealed class SetBasketSourceEnabledHandler(IBasketSourceStore sources, IBasketFetchSettings settings, IAccountEventLog events, TimeProvider clock)
    : ICommandHandler<SetBasketSourceEnabledCommand, BasketSourceView>
{
    public async ValueTask<Result<BasketSourceView>> HandleAsync(SetBasketSourceEnabledCommand command, CancellationToken cancellationToken)
    {
        var found = await sources.ListAsync([command.BasketId], cancellationToken);
        if (found.Count == 0)
        {
            return Result.Failure<BasketSourceView>(BasketErrors.NotFound);
        }

        var source = found[0];
        if (source.Enabled != command.Enabled)
        {
            events.Record(AccountEvent.Record(
                AccountEventType.BasketSourceSwitched, null, $"System basket '{source.Name}' source switched {(command.Enabled ? "on" : "off")} (settings {settings.Snapshot()})", command.Actor, clock.GetUtcNow()));
            await sources.SetEnabledAsync(command.BasketId, command.Enabled, cancellationToken);
        }

        var after = (await sources.ListAsync([command.BasketId], cancellationToken))[0];
        var fetches = await sources.RecentFetchesAsync(after.BasketId, GetBasketSourceHandler.Recent, cancellationToken);
        return Result.Success(new BasketSourceView(
            after.BasketId, after.Name, after.FileName, after.SourceUrl, after.Enabled, after.State, after.LastOkAt, after.LastCheckedAt, after.NextAttemptAt, after.ExpectedMin, after.ExpectedMax, fetches));
    }
}
