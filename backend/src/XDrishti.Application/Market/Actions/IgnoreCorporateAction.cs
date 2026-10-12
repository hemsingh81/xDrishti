using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Actions;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Market.Actions;

/// <summary>
/// Declines an action: it stays in the list as ignored (nothing is deleted) and the detection does not suggest it again. Works on a suggestion and on an applied action (taking it back). Idempotent: an ignored action
/// stays as it is and is not audited again. <c>data.corporate_action_not_found</c> (404). Audited.
/// </summary>
public sealed record IgnoreCorporateActionCommand(Guid Id, string Actor) : ICommand;

internal sealed class IgnoreCorporateActionHandler(
    ICorporateActionStore store,
    ICorporateActionQueries queries,
    IMarketUnitOfWork unitOfWork,
    IAccountEventLog events,
    TimeProvider clock) : ICommandHandler<IgnoreCorporateActionCommand, CorporateActionView>
{
    public async ValueTask<Result<CorporateActionView>> HandleAsync(IgnoreCorporateActionCommand command, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < CorporateActionChecks.Attempts; attempt++)
        {
            var action = await store.FindAsync(command.Id, cancellationToken);
            if (action is null)
            {
                return Result.Failure<CorporateActionView>(DataErrors.CorporateActionNotFound);
            }

            var instrument = await queries.FindInstrumentAsync(action.InstrumentId, cancellationToken)
                ?? new ActionInstrument(action.InstrumentId, string.Empty, string.Empty);
            var now = clock.GetUtcNow();
            if (!action.Ignore(command.Actor, now))
            {
                return Result.Success(await CorporateActionView.From(action, instrument.Symbol, instrument.Name).WithThroughAsync(queries, cancellationToken));
            }

            events.Record(AccountEvent.Record(AccountEventType.CorporateActionIgnored, null, CorporateActionChecks.Describe(action, instrument.Symbol), command.Actor, now));
            if (await unitOfWork.TrySaveAsync(cancellationToken))
            {
                return Result.Success(await CorporateActionView.From(action, instrument.Symbol, instrument.Name).WithThroughAsync(queries, cancellationToken));
            }
        }

        return Result.Failure<CorporateActionView>(DataErrors.CorporateActionNotFound);
    }
}
