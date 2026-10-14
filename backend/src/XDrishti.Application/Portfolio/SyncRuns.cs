using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio;

/// <summary>What the work of a run produced for the caller, and the counts to record on the run.</summary>
internal sealed record Applied<TResult>(TResult Value, SyncCounts Counts);

/// <summary>A run that ended well: its id and what the work produced.</summary>
internal sealed record SyncRunOutcome<TResult>(Guid RunId, TResult Value);

/// <summary>
/// Carries out a run of the holdings sync, shared by "Sync now", the automatic refresh and the import: registers the run (one per account at a
/// time), fetches Dhan's holdings, applies them in one database transaction together with the run's end, and records a failure on the run without
/// touching any holding. A run that fails leaves the data exactly as it was.
/// </summary>
internal sealed class SyncRuns(IHoldingSyncStore syncs, IPortfolioUnitOfWork unitOfWork, TimeProvider clock, ILogger<SyncRuns> logger)
{
    /// <summary>A run that has been "running" this long was interrupted (Dhan's timeout is 30 seconds).</summary>
    public static readonly TimeSpan StaleAfter = SyncLimits.HoldingsStaleAfter;

    private const string Interrupted = "The sync was interrupted because the application is stopping.";

    public async ValueTask<Result<SyncRunOutcome<TResult>>> RunAsync<TResult>(
        BrokerAccount account,
        SyncTrigger trigger,
        string actor,
        Func<CancellationToken, ValueTask<Result<FetchedHoldings>>> fetch,
        Func<FetchedHoldings, CancellationToken, ValueTask<Result<Applied<TResult>>>> apply,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var run = HoldingSync.Start(account.Id, trigger, actor, now);
        if (!await syncs.TryStartAsync(run, StaleAfter, now, cancellationToken))
        {
            return Result.Failure<SyncRunOutcome<TResult>>(PortfolioErrors.SyncRunning);
        }

        var triggerName = HoldingViews.Name(trigger);
        PortfolioLog.SyncStarted(logger, run.Id, account.Slug, triggerName);
        try
        {
            var fetched = await fetch(cancellationToken);
            if (fetched.IsFailure)
            {
                return await FailAsync<TResult>(run, fetched.Error);
            }

            Applied<TResult>? applied = null;
            var committed = await unitOfWork.RunAsync(
                async token =>
                {
                    var result = await apply(fetched.Value, token);
                    if (result.IsFailure)
                    {
                        return Result.Failure(result.Error);
                    }

                    applied = result.Value;
                    await unitOfWork.SaveAsync(token);
                    await syncs.CompleteAsync(run.Id, SyncStatus.Succeeded, result.Value.Counts, null, clock.GetUtcNow(), token);
                    return Result.Success();
                },
                cancellationToken);
            if (committed.IsFailure)
            {
                return committed.Error.Code is PortfolioErrors.ConfirmationRequiredCode or PortfolioErrors.ConfirmationStaleCode
                    ? await HoldBackAsync<TResult>(run, trigger, committed.Error)
                    : await FailAsync<TResult>(run, committed.Error);
            }

            var counts = applied!.Counts;
            PortfolioLog.SyncSucceeded(logger, run.Id, counts.Added, counts.Increased, counts.Decreased, counts.Closed, counts.Unchanged);
            return Result.Success(new SyncRunOutcome<TResult>(run.Id, applied.Value));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await syncs.CompleteAsync(run.Id, SyncStatus.Failed, SyncCounts.None, Interrupted, clock.GetUtcNow(), CancellationToken.None);
            throw;
        }
#pragma warning disable CA1031 // any failure must end the run record as failed (and leave every holding untouched), never leave it running
        catch (Exception ex)
#pragma warning restore CA1031
        {
            PortfolioLog.SyncCrashed(logger, run.Id, ex);
            return await FailAsync<TResult>(run, PortfolioErrors.SyncFailed);
        }
    }

    /// <summary>
    /// The work refused to apply a suspicious answer. A manual run leaves no record (the owner gets the 409 and may confirm); an automatic run is
    /// recorded as skipped with the reason, and replaces the skipped record of the same India day, so there is one a day.
    /// </summary>
    private async ValueTask<Result<SyncRunOutcome<TResult>>> HoldBackAsync<TResult>(HoldingSync run, SyncTrigger trigger, Error error)
    {
        PortfolioLog.SyncHeldBack(logger, run.Id, HoldingViews.Name(trigger));
        var now = clock.GetUtcNow();
        if (trigger != SyncTrigger.Auto)
        {
            await syncs.DiscardAsync(run.Id, CancellationToken.None);
            return Result.Failure<SyncRunOutcome<TResult>>(error);
        }

        await syncs.CompleteAsync(run.Id, SyncStatus.Skipped, SyncCounts.None, error.Message, now, CancellationToken.None);
        var (start, end) = BrokerTokenExpiry.IndianDay(now);
        await syncs.DeleteOtherSkippedAsync(run.AccountId, start, end, run.Id, CancellationToken.None);
        return Result.Failure<SyncRunOutcome<TResult>>(error);
    }

    private async ValueTask<Result<SyncRunOutcome<TResult>>> FailAsync<TResult>(HoldingSync run, Error error)
    {
        PortfolioLog.SyncFailed(logger, run.Id, error.Code);
        await syncs.CompleteAsync(run.Id, SyncStatus.Failed, SyncCounts.None, error.Message, clock.GetUtcNow(), CancellationToken.None);
        return Result.Failure<SyncRunOutcome<TResult>>(error);
    }
}
