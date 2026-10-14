using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Dividends;

/// <summary>Voids one dividend (ADR 0026): it stops counting and is never deleted. Voiding a voided dividend is a no-op. Audited with the id only.</summary>
public sealed record VoidDividendCommand(Guid DividendId, string Actor) : ICommand;

internal sealed class VoidDividendHandler(
    IDividendStore dividends,
    IAccountStore accounts,
    IAccountEventLog events,
    IPortfolioUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<VoidDividendHandler> logger) : ICommandHandler<VoidDividendCommand, DividendView>
{
    public async ValueTask<Result<DividendView>> HandleAsync(VoidDividendCommand command, CancellationToken cancellationToken)
    {
        var dividend = await dividends.FindAsync(command.DividendId, cancellationToken);
        if (dividend is null)
        {
            return Result.Failure<DividendView>(PortfolioErrors.DividendNotFound);
        }

        var now = clock.GetUtcNow();
        if (dividend.Void(command.Actor, now))
        {
            events.Record(AccountEvent.Record(AccountEventType.DividendVoided, dividend.AccountId, $"Dividend {dividend.Id} voided", command.Actor, now));
            await unitOfWork.SaveAsync(cancellationToken);
            var account = await accounts.FindReadOnlyAsync(dividend.AccountId, cancellationToken);
            DividendsLog.Voided(logger, dividend.Id, account?.Slug ?? "?");
        }

        return Result.Success(DividendViews.Dividend(dividend));
    }
}
