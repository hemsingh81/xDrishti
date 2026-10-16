using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Dividends;

/// <summary>Dismisses a suggested dividend (ADR 0026): the ledger entry is not offered again; nothing else changes. The decision is final. 404 when the entry is not a dividend entry of the account; 409 when it was decided meanwhile. Audited with ids only.</summary>
public sealed record DismissDividendSuggestionCommand(Guid AccountId, Guid LedgerEntryId, string Actor) : ICommand;

internal sealed class DismissDividendSuggestionHandler(
    IAccountStore accounts,
    IDividendSuggestionStore suggestions,
    IAccountEventLog events,
    IPortfolioUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<DismissDividendSuggestionHandler> logger) : ICommandHandler<DismissDividendSuggestionCommand, SuggestionDecisionView>
{
    public async ValueTask<Result<SuggestionDecisionView>> HandleAsync(DismissDividendSuggestionCommand command, CancellationToken cancellationToken)
    {
        var account = await accounts.FindReadOnlyAsync(command.AccountId, cancellationToken);
        if (account is null)
        {
            return Result.Failure<SuggestionDecisionView>(AccountErrors.NotFound);
        }

        var entry = await suggestions.FindEntryAsync(command.LedgerEntryId, cancellationToken);
        if (entry is null || !DividendSuggestions.IsOpenCandidate(entry, account.Id))
        {
            return Result.Failure<SuggestionDecisionView>(PortfolioErrors.SuggestionNotFound);
        }

        if (await suggestions.IsDecidedAsync(entry.Id, cancellationToken))
        {
            return Result.Failure<SuggestionDecisionView>(PortfolioErrors.SuggestionDecided);
        }

        var now = clock.GetUtcNow();
        var decision = DividendSuggestion.Dismiss(entry.Id, account.Id, command.Actor, now);
        suggestions.Add(decision);
        events.Record(AccountEvent.Record(AccountEventType.DividendSuggestionDismissed, account.Id, $"Suggested dividend {entry.Id} dismissed", command.Actor, now));
        if (!await unitOfWork.TrySaveAsync(cancellationToken))
        {
            return Result.Failure<SuggestionDecisionView>(PortfolioErrors.SuggestionDecided);
        }

        DividendsLog.SuggestionDismissed(logger, entry.Id, account.Slug);
        return Result.Success(DividendViews.Decision(decision, entry.Id));
    }
}
