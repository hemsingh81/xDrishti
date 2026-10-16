using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Dividends;

/// <summary>
/// Accepts a suggested dividend (ADR 0026): creates a dividend of the ledger entry's amount and date — for the holding the owner chose (one of the account's) or, with none, unallocated — and remembers the decision, so the entry is not offered again. The
/// decision is final (a dividend voided later does not bring the suggestion back). Nothing is created without this call. 404 when the entry is not a dividend entry of the account; 400 when its amount is one a dividend cannot have (above the highest amount); 409 when it was decided meanwhile. Audited with ids only.
/// </summary>
public sealed record AcceptDividendSuggestionCommand(Guid AccountId, Guid LedgerEntryId, AcceptSuggestionInput? Input, string Actor) : ICommand;

internal sealed class AcceptDividendSuggestionHandler(
    IAccountStore accounts,
    IHoldingQueries holdings,
    IDividendStore dividends,
    IDividendSuggestionStore suggestions,
    IAccountEventLog events,
    IPortfolioUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<AcceptDividendSuggestionHandler> logger) : ICommandHandler<AcceptDividendSuggestionCommand, DividendView>
{
    public async ValueTask<Result<DividendView>> HandleAsync(AcceptDividendSuggestionCommand command, CancellationToken cancellationToken)
    {
        var account = await accounts.FindReadOnlyAsync(command.AccountId, cancellationToken);
        if (account is null)
        {
            return Result.Failure<DividendView>(AccountErrors.NotFound);
        }

        if (!account.Portfolio.Enabled)
        {
            return Result.Failure<DividendView>(PortfolioErrors.RoleOff);
        }

        var entry = await suggestions.FindEntryAsync(command.LedgerEntryId, cancellationToken);
        if (entry is null || !DividendSuggestions.IsOpenCandidate(entry, account.Id))
        {
            return Result.Failure<DividendView>(PortfolioErrors.SuggestionNotFound);
        }

        if (await suggestions.IsDecidedAsync(entry.Id, cancellationToken))
        {
            return Result.Failure<DividendView>(PortfolioErrors.SuggestionDecided);
        }

        var errors = new FieldErrors();
        if (!DividendRules.CanEnter(entry.Amount))
        {
            errors.Add("amount", $"The ledger amount cannot be entered as a dividend (above 0 and at most {DividendRules.MaxAmount:N0}); dismiss the suggestion and enter the dividend yourself.");
        }

        var note = DividendRules.CleanNote(command.Input?.Note);
        if (note is { Length: > DividendRules.MaxNoteLength })
        {
            errors.Add("note", $"At most {DividendRules.MaxNoteLength} characters.");
        }

        HoldingData? holding = null;
        if (command.Input?.HoldingId is { } holdingId)
        {
            holding = await holdings.GetAsync(holdingId, cancellationToken);
            if (holding is null)
            {
                errors.Add("holdingId", "The holding does not exist.");
            }
            else if (holding.AccountId != account.Id)
            {
                errors.Add("holdingId", "The holding belongs to another account.");
            }
        }

        if (errors.HasErrors)
        {
            return Result.Failure<DividendView>(PortfolioErrors.Invalid(errors));
        }

        var now = clock.GetUtcNow();
        var dividend = Dividend.Create(account.Id, holding?.Id, holding?.InstrumentId, holding?.Isin, holding?.TradingSymbol, entry.EntryDate, entry.Amount, null, note, command.Actor, now);
        dividends.Add(dividend);
        suggestions.Add(DividendSuggestion.Accept(entry.Id, account.Id, dividend.Id, command.Actor, now));
        events.Record(AccountEvent.Record(AccountEventType.DividendSuggestionAccepted, account.Id, $"Suggested dividend {entry.Id} accepted as dividend {dividend.Id}", command.Actor, now));
        if (!await unitOfWork.TrySaveAsync(cancellationToken))
        {
            return Result.Failure<DividendView>(PortfolioErrors.SuggestionDecided);
        }

        DividendsLog.SuggestionAccepted(logger, entry.Id, account.Slug, dividend.Id);
        return Result.Success(DividendViews.Dividend(dividend));
    }
}
