using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Actions;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Market.Actions;

/// <summary>
/// Edits an action and makes it applied: confirms a suggestion (an empty body confirms it as it is), brings back an ignored one or corrects an applied one. Fields left out keep their value.
/// Idempotent: confirming what is applied already with the same values changes nothing and is not audited again. <c>data.invalid</c> with the field (as when adding),
/// <c>data.corporate_action_not_found</c> (404), <c>data.corporate_action_exists</c> (409) when the new ex-date and kind belong to another action of the instrument,
/// <c>data.corporate_action_conflicts_same_day</c> (409) when an applied action of the other kind is on the ex-date (a suggestion of the other kind is declined by itself, and audited),
/// <c>data.csv_already_adjusted</c> (409) when the result is applied (it was not before, or its ex-date, kind or ratio changes) with an ex-date on or before the date the instrument's loaded CSV files
/// reach, unless <c>acknowledgeCsvAdjusted</c> is true. Audited
/// (<c>CorporateActionConfirmed</c> when it was not applied before, else <c>CorporateActionEdited</c>).
/// </summary>
public sealed record UpdateCorporateActionCommand(Guid Id, UpdateCorporateActionRequest Request, string Actor) : ICommand;

internal sealed class UpdateCorporateActionHandler(
    ICorporateActionStore store,
    ICorporateActionQueries queries,
    IMarketUnitOfWork unitOfWork,
    IAccountEventLog events,
    TimeProvider clock) : ICommandHandler<UpdateCorporateActionCommand, CorporateActionView>
{
    public async ValueTask<Result<CorporateActionView>> HandleAsync(UpdateCorporateActionCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        for (var attempt = 0; attempt < CorporateActionChecks.Attempts; attempt++)
        {
            var action = await store.FindAsync(command.Id, cancellationToken);
            if (action is null)
            {
                return Result.Failure<CorporateActionView>(DataErrors.CorporateActionNotFound);
            }

            var now = clock.GetUtcNow();
            var current = new ActionInput(action.ExDate, action.Kind, action.Numerator, action.Denominator, action.Note);
            var checkedInput = CorporateActionChecks.Check(request.ExDate, request.Kind, request.Numerator, request.Denominator, request.Note, current, IndiaTime.Today(now));
            if (checkedInput.IsFailure)
            {
                return Result.Failure<CorporateActionView>(checkedInput.Error);
            }

            var input = checkedInput.Value;
            if ((input.ExDate != action.ExDate || input.Kind != action.Kind) && await store.ExistsAsync(action.InstrumentId, input.ExDate, input.Kind, action.Id, cancellationToken))
            {
                return Result.Failure<CorporateActionView>(DataErrors.CorporateActionExists);
            }

            var instrument = await queries.FindInstrumentAsync(action.InstrumentId, cancellationToken)
                ?? new ActionInstrument(action.InstrumentId, string.Empty, string.Empty);
            var changesPrices = action.Status != CorporateActionStatus.Applied || input.ExDate != action.ExDate || input.Kind != action.Kind
                || input.Numerator != action.Numerator || input.Denominator != action.Denominator;
            var others = SameDayActions.OthersOn(await store.ListNotIgnoredAsync([action.InstrumentId], cancellationToken), action.InstrumentId, input.ExDate, input.Kind, action.Id);
            if (changesPrices)
            {
                if (SameDayActions.Blocks(others))
                {
                    return Result.Failure<CorporateActionView>(SameDayActions.Conflict);
                }

                if (await CsvAdjustment.GuardAsync(queries, action.InstrumentId, input.ExDate, request.AcknowledgeCsvAdjusted == true, cancellationToken) is { } adjustsTwice)
                {
                    return Result.Failure<CorporateActionView>(adjustsTwice);
                }
            }

            var before = action.Status;
            var was = $"{(action.Kind == CorporateActionKind.Split ? "split" : "bonus")} {action.Ratio} ex {action.ExDate:yyyy-MM-dd}";
            if (!action.Confirm(input.ExDate, input.Kind, input.Numerator, input.Denominator, input.Note, command.Actor, now))
            {
                return Result.Success(await CorporateActionView.From(action, instrument.Symbol, instrument.Name).WithThroughAsync(queries, cancellationToken));
            }

            foreach (var declined in others.Where(o => o.Ignore(command.Actor, now)))
            {
                events.Record(AccountEvent.Record(AccountEventType.CorporateActionIgnored, null, SameDayActions.Describe(declined, instrument.Symbol), command.Actor, now));
            }

            var type = before == CorporateActionStatus.Applied ? AccountEventType.CorporateActionEdited : AccountEventType.CorporateActionConfirmed;
            var detail = CorporateActionChecks.Describe(action, instrument.Symbol) + (before == CorporateActionStatus.Applied ? $" (was {was})" : string.Empty);
            events.Record(AccountEvent.Record(type, null, detail, command.Actor, now));
            if (await unitOfWork.TrySaveAsync(cancellationToken))
            {
                return Result.Success(await CorporateActionView.From(action, instrument.Symbol, instrument.Name).WithThroughAsync(queries, cancellationToken));
            }
        }

        return Result.Failure<CorporateActionView>(DataErrors.CorporateActionExists);
    }
}
