using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Accounts;

/// <summary>Replaces the portfolio settings of an account (default bucket, where the trade history starts, include in totals).</summary>
public sealed record UpdatePortfolioCommand(Guid AccountId, PortfolioInput Input, string Actor) : ICommand;

internal sealed class UpdatePortfolioHandler(
    IAccountStore accounts,
    IBucketStore buckets,
    IAccountEventLog events,
    IAccountUnitOfWork unitOfWork,
    AccountViews views,
    TimeProvider clock,
    ILogger<UpdatePortfolioHandler> logger) : ICommandHandler<UpdatePortfolioCommand, AccountDetail>
{
    public async ValueTask<Result<AccountDetail>> HandleAsync(UpdatePortfolioCommand command, CancellationToken cancellationToken)
    {
        var account = await accounts.FindAsync(command.AccountId, cancellationToken);
        if (account is null)
        {
            return Result.Failure<AccountDetail>(AccountErrors.NotFound);
        }

        var spec = AccountInputs.ParsePortfolio(command.Input);
        if (!string.IsNullOrWhiteSpace(spec.DefaultBucket))
        {
            var bucket = await buckets.FindNameAsync(spec.DefaultBucket.Trim(), cancellationToken);
            if (bucket is null)
            {
                return Result.Failure<AccountDetail>(AccountErrors.Invalid("defaultBucket", "Unknown bucket."));
            }

            spec = spec with { DefaultBucket = bucket };
        }

        var now = clock.GetUtcNow();
        var configured = account.ConfigurePortfolio(spec, AccountTime.TodayInIndia(now), now);
        if (configured.IsFailure)
        {
            return Result.Failure<AccountDetail>(configured.Error);
        }

        var from = account.Portfolio.HistoryFrom is { } date ? date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : "not set";
        events.Record(AccountEvent.Record(
            AccountEventType.PortfolioUpdated,
            account.Id,
            $"Portfolio ({(account.Portfolio.Enabled ? "role on" : "role off")}): default bucket {account.Portfolio.DefaultBucket ?? "none"}, history from {from}, {(account.Portfolio.IncludeInTotals ? "included in" : "excluded from")} totals",
            command.Actor,
            now));
        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
        {
            AccountsLog.ChangeCollided(logger, account.Slug);
            return Result.Failure<AccountDetail>(AccountErrors.ConcurrentChange);
        }

        AccountsLog.PortfolioUpdated(logger, account.Slug);
        return Result.Success(await views.DetailAsync(account, cancellationToken));
    }
}
