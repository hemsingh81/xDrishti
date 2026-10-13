using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Alerts;

/// <summary>
/// Creates an active one-shot price alert (ADR 0022): the instrument must be an NSE share or ETF of the master list that is not retired and that Dhan can quote (indices and futures are out of scope); the level above 0, at most 10,000,000 with at most
/// 4 decimals; the note at most 200 characters of plain text. A condition that already holds at the last known price (the holding's last price, else the last stored daily close; unknown: the next tick decides) is refused with a <c>level</c> error
/// that names the price. At most 200 active alerts (<c>409 portfolio.alert_limit</c>), counted inside the creating transaction. Audited.
/// </summary>
public sealed record CreatePriceAlertCommand(PriceAlertInput Input, string Actor) : ICommand;

internal sealed class CreatePriceAlertHandler(
    IPriceAlertStore alerts,
    ITradingCalendar calendar,
    IAccountEventLog events,
    IPortfolioUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<CreatePriceAlertHandler> logger) : ICommandHandler<CreatePriceAlertCommand, PriceAlertView>
{
    /// <summary>A stored daily close older than this many trading days is not "the last price": the refusal of an already-true condition is not based on it.</summary>
    public const int MaxCloseAgeTradingDays = 5;

    private const string Nse = "NSE";

    public async ValueTask<Result<PriceAlertView>> HandleAsync(CreatePriceAlertCommand command, CancellationToken cancellationToken)
    {
        var input = command.Input;
        var errors = new FieldErrors();
        var instrumentId = input?.InstrumentId is { } id && id != Guid.Empty ? id : (Guid?)null;
        if (instrumentId is null)
        {
            errors.Add("instrumentId", "Required: the id of a share or ETF of the instrument master.");
        }

        var directionValid = AlertDirections.TryParse(input?.Direction, out var direction);
        if (!directionValid)
        {
            errors.Add("direction", "Use at_or_below or at_or_above.");
        }

        var levelValid = false;
        if (input?.Level is not { } level)
        {
            errors.Add("level", "Required: the price in rupees.");
        }
        else if (!PriceAlert.IsValidLevel(level))
        {
            errors.Add("level", $"Use a price above 0 and at most {PriceAlert.MaxLevel:N0}, with at most {PriceAlert.LevelDecimals} decimals.");
        }
        else
        {
            levelValid = true;
        }

        var note = input?.Note?.Trim();
        if (note is { Length: > PriceAlert.MaxNoteLength })
        {
            errors.Add("note", $"Use at most {PriceAlert.MaxNoteLength} characters.");
        }
        else if (note is not null && note.Any(char.IsControl))
        {
            errors.Add("note", "Control characters are not allowed.");
        }

        AlertInstrument? instrument = null;
        if (instrumentId is { } wanted)
        {
            instrument = await alerts.FindInstrumentAsync(wanted, cancellationToken);
            var problem = InstrumentProblem(instrument);
            if (problem is not null)
            {
                errors.Add("instrumentId", problem);
                instrument = null;
            }
        }

        if (instrument is not null && directionValid && levelValid && input?.Level is { } checkedLevel
            && await CurrentPriceAsync(instrument.Id, cancellationToken) is { } known
            && PriceAlert.Holds(direction, checkedLevel, known.Price))
        {
            errors.Add("level", $"The last price is {AlertText.Price(known.Price)}, so a price {AlertText.Condition(direction)} {AlertText.Price(checkedLevel)} would trigger at once. Choose a level the price has not reached.");
        }

        if (errors.HasErrors)
        {
            return Result.Failure<PriceAlertView>(PortfolioErrors.Invalid(errors));
        }

        var symbol = instrument!.Symbol;
        var created = Guid.Empty;
        var outcome = await unitOfWork.RunAsync(
            async token =>
            {
                if (await alerts.CountActiveAsync(token) >= PortfolioErrors.MaxActiveAlerts)
                {
                    return Result.Failure(PortfolioErrors.AlertLimit);
                }

                var now = clock.GetUtcNow();
                var alert = PriceAlert.Create(instrument.Id, direction, input!.Level!.Value, note, command.Actor, now);
                alerts.Add(alert);
                events.Record(AccountEvent.Record(
                    AccountEventType.PriceAlertCreated,
                    null,
                    $"Price alert created: {symbol} {AlertText.Condition(direction)} {AlertText.Price(alert.Level)}",
                    command.Actor,
                    now));
                await unitOfWork.SaveAsync(token);
                created = alert.Id;
                return Result.Success();
            },
            cancellationToken);
        if (outcome.IsFailure)
        {
            return Result.Failure<PriceAlertView>(outcome.Error);
        }

        AlertsLog.Created(logger, created, symbol);
        var row = await alerts.FindAsync(created, cancellationToken);
        return row is null ? Result.Failure<PriceAlertView>(PortfolioErrors.AlertNotFound) : Result.Success(PriceAlertViews.Of(row));
    }

    /// <summary>The holding's last price, or the last stored daily close if it is at most <see cref="MaxCloseAgeTradingDays"/> trading days old; null (unknown: the next tick decides) otherwise.</summary>
    private async ValueTask<KnownPrice?> CurrentPriceAsync(Guid instrumentId, CancellationToken cancellationToken)
    {
        if (await alerts.GetKnownPriceAsync(instrumentId, cancellationToken) is not { } known)
        {
            return null;
        }

        if (known is not { Source: "close", AsOf: { } closedOn })
        {
            return known;
        }

        var today = IndiaTime.Today(clock.GetUtcNow());
        return (await calendar.GetAsync(cancellationToken)).TradingDaysBetween(closedOn, today) <= MaxCloseAgeTradingDays ? known : null;
    }

    /// <summary>Why the instrument cannot have an alert; null when it can.</summary>
    private static string? InstrumentProblem(AlertInstrument? instrument)
    {
        if (instrument is null)
        {
            return "That instrument is not in the instrument master.";
        }

        if (instrument.Retired)
        {
            return "That instrument is no longer listed.";
        }

        if (instrument.Type is not (InstrumentTypeCodes.Share or InstrumentTypeCodes.Etf) || !string.Equals(instrument.Exchange, Nse, StringComparison.OrdinalIgnoreCase))
        {
            return "Price alerts are for NSE shares and ETFs; indices and futures are not supported.";
        }

        return instrument.Quotable ? null : "Dhan has no NSE equity id for this instrument, so it cannot be quoted.";
    }
}
