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
/// Adds a split or bonus by hand: applied at once (source manual). <c>data.invalid</c> with the field for an unreadable or out-of-range date, an unknown kind, a ratio outside 1 to 100000 or a split 1:1,
/// or a note over 200 characters; <c>data.instrument_not_found</c> (404); <c>data.corporate_action_exists</c> (409) when the instrument has an action of that kind on that ex-date, whatever its status;
/// <c>data.corporate_action_conflicts_same_day</c> (409) when an applied action of the other kind is on that ex-date (a suggestion of the other kind is declined by itself, and audited);
/// <c>data.csv_already_adjusted</c> (409) when the ex-date is on or before the date the instrument's loaded CSV files reach, unless <c>acknowledgeCsvAdjusted</c> is true (the files are adjusted at source). Audited.
/// </summary>
public sealed record AddCorporateActionCommand(AddCorporateActionRequest Request, string Actor) : ICommand;

internal sealed class AddCorporateActionHandler(
    ICorporateActionStore store,
    ICorporateActionQueries queries,
    IMarketUnitOfWork unitOfWork,
    IAccountEventLog events,
    TimeProvider clock) : ICommandHandler<AddCorporateActionCommand, CorporateActionView>
{
    public async ValueTask<Result<CorporateActionView>> HandleAsync(AddCorporateActionCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;
        var now = clock.GetUtcNow();
        var checkedInput = CorporateActionChecks.Check(request.ExDate, request.Kind, request.Numerator, request.Denominator, request.Note, null, IndiaTime.Today(now));
        if (checkedInput.IsFailure || request.InstrumentId is null)
        {
            var fields = checkedInput.IsFailure ? (checkedInput.Error.Fields ?? []).ToList() : [];
            if (request.InstrumentId is null)
            {
                fields.Insert(0, new FieldError("instrumentId", "Choose an instrument."));
            }

            return Result.Failure<CorporateActionView>(Error.Validation(DataErrors.InvalidCode, "Some values are not valid.") with { Fields = fields });
        }

        var instrument = await queries.FindInstrumentAsync(request.InstrumentId.Value, cancellationToken);
        if (instrument is null)
        {
            return Result.Failure<CorporateActionView>(DataErrors.InstrumentNotFound);
        }

        var input = checkedInput.Value;
        for (var attempt = 0; attempt < CorporateActionChecks.Attempts; attempt++)
        {
            if (await store.ExistsAsync(instrument.Id, input.ExDate, input.Kind, null, cancellationToken))
            {
                return Result.Failure<CorporateActionView>(DataErrors.CorporateActionExists);
            }

            var others = SameDayActions.OthersOn(await store.ListNotIgnoredAsync([instrument.Id], cancellationToken), instrument.Id, input.ExDate, input.Kind, null);
            if (SameDayActions.Blocks(others))
            {
                return Result.Failure<CorporateActionView>(SameDayActions.Conflict);
            }

            if (await CsvAdjustment.GuardAsync(queries, instrument.Id, input.ExDate, request.AcknowledgeCsvAdjusted == true, cancellationToken) is { } adjustsTwice)
            {
                return Result.Failure<CorporateActionView>(adjustsTwice);
            }

            foreach (var declined in others.Where(o => o.Ignore(command.Actor, now)))
            {
                events.Record(AccountEvent.Record(AccountEventType.CorporateActionIgnored, null, SameDayActions.Describe(declined, instrument.Symbol), command.Actor, now));
            }

            var action = CorporateAction.Apply(instrument.Id, input.ExDate, input.Kind, input.Numerator, input.Denominator, CorporateActionSource.Manual, input.Note, command.Actor, now);
            store.Add(action);
            events.Record(AccountEvent.Record(AccountEventType.CorporateActionAdded, null, CorporateActionChecks.Describe(action, instrument.Symbol), command.Actor, now));
            if (await unitOfWork.TrySaveAsync(cancellationToken))
            {
                return Result.Success(await CorporateActionView.From(action, instrument.Symbol, instrument.Name).WithThroughAsync(queries, cancellationToken));
            }
        }

        return Result.Failure<CorporateActionView>(DataErrors.CorporateActionExists);
    }
}
