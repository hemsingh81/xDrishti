using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Dividends;

/// <summary>
/// Adds one dividend typed in by the owner (ADR 0026), for a holding (<c>HoldingId</c>) or, unallocated, for an account (<c>AccountId</c>). Validated: the pay date from 1990-01-02 to today (India), <c>Amount</c> above 0 (to the paisa), <c>PerShare</c>
/// above 0 when given, <c>Note</c> at most 200 characters. A holding must belong to the account. The same account, holding, date and amount as a dividend that is not voided is a possible duplicate: <c>409 portfolio.possible_duplicate</c> unless
/// <c>Confirm</c> is true. Audited with the id only (never the amount).
/// </summary>
public sealed record AddDividendCommand(DividendInput Input, string Actor) : ICommand;

internal sealed class AddDividendHandler(
    IAccountStore accounts,
    IHoldingQueries holdings,
    IDividendStore dividends,
    IAccountEventLog events,
    IPortfolioUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<AddDividendHandler> logger) : ICommandHandler<AddDividendCommand, DividendView>
{
    public async ValueTask<Result<DividendView>> HandleAsync(AddDividendCommand command, CancellationToken cancellationToken)
    {
        var input = command.Input;
        var errors = new FieldErrors();
        if (input is null)
        {
            errors.Add("amount", "Required.");
            return Result.Failure<DividendView>(PortfolioErrors.Invalid(errors));
        }

        var now = clock.GetUtcNow();
        var today = IndiaTime.Today(now);
        var date = input.PayDate ?? default;
        if (input.PayDate is null)
        {
            errors.Add("payDate", "Required.");
        }
        else
        {
            DividendRules.CheckPayDate(errors, "payDate", date, today);
        }

        var amount = 0m;
        if (input.Amount is not { } rawAmount)
        {
            errors.Add("amount", "Required.");
        }
        else
        {
            amount = DividendRules.RoundAmount(rawAmount);
            if (DividendRules.AmountProblem(amount) is { } amountProblem)
            {
                errors.Add("amount", amountProblem);
            }
        }

        decimal? perShare = null;
        if (input.PerShare is { } rawShare)
        {
            perShare = DividendRules.RoundPerShare(rawShare);
            if (DividendRules.PerShareProblem(perShare.Value) is { } shareProblem)
            {
                errors.Add("perShare", shareProblem);
            }
        }

        var note = DividendRules.CleanNote(input.Note);
        if (note is { Length: > DividendRules.MaxNoteLength })
        {
            errors.Add("note", $"At most {DividendRules.MaxNoteLength} characters.");
        }

        var target = await ResolveAsync(input, errors, cancellationToken);
        if (errors.HasErrors || target is null)
        {
            return Result.Failure<DividendView>(PortfolioErrors.Invalid(errors));
        }

        var account = await accounts.FindReadOnlyAsync(target.AccountId, cancellationToken);
        if (account is null)
        {
            return Result.Failure<DividendView>(PortfolioErrors.AccountNotFound);
        }

        if (!account.Portfolio.Enabled)
        {
            return Result.Failure<DividendView>(PortfolioErrors.RoleOff);
        }

        if (input.Confirm != true && await dividends.ExistsSameAsync(account.Id, target.Holding?.Id, date, amount, cancellationToken))
        {
            return Result.Failure<DividendView>(PortfolioErrors.PossibleDuplicate);
        }

        var dividend = Dividend.Create(account.Id, target.Holding?.Id, target.Holding?.InstrumentId, target.Holding?.Isin, target.Holding?.TradingSymbol, date, amount, perShare, note, command.Actor, now);
        dividends.Add(dividend);
        events.Record(AccountEvent.Record(AccountEventType.DividendAdded, account.Id, $"Dividend {dividend.Id} added", command.Actor, now));
        await unitOfWork.SaveAsync(cancellationToken);
        DividendsLog.Added(logger, dividend.Id, account.Slug);
        return Result.Success(DividendViews.Dividend(dividend));
    }

    /// <summary>The account (and holding) the dividend is for; field errors otherwise.</summary>
    private async ValueTask<Target?> ResolveAsync(DividendInput input, FieldErrors errors, CancellationToken cancellationToken)
    {
        if (input.HoldingId is { } holdingId)
        {
            var holding = await holdings.GetAsync(holdingId, cancellationToken);
            if (holding is null)
            {
                errors.Add("holdingId", "The holding does not exist.");
                return null;
            }

            if (input.AccountId is { } given && given != holding.AccountId)
            {
                errors.Add("accountId", "Does not match the holding's account.");
                return null;
            }

            return new Target(holding.AccountId, holding);
        }

        if (input.AccountId is not { } accountId || accountId == Guid.Empty)
        {
            errors.Add("accountId", "Required (or give a holdingId).");
            return null;
        }

        return new Target(accountId, null);
    }

    private sealed record Target(Guid AccountId, HoldingData? Holding);
}
