using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Common;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Portfolio;

/// <summary>
/// The scoped context tracks the holdings and account events of one use case; saving commits them, a run commits them together with the sync record. Whatever is saved here may change a lot, a holding or the benchmark setting, so the cached portfolio histories
/// are dropped after every save and every committed run (<see cref="IPortfolioHistoryCache"/>). A save that may have changed the live feed's active set (<see cref="ActiveSetSignal"/>) sends <c>NOTIFY active_set_changed</c> once
/// it is committed - after the outer transaction of a run, never inside it - best effort: the signal can fail without failing the business operation (the feed's one-minute recompute is the safety net).
/// </summary>
internal sealed partial class PortfolioUnitOfWork(XDrishtiDbContext db, IPortfolioHistoryCache histories, ILiveNotifier notifier, TimeProvider clock, ILogger<PortfolioUnitOfWork> logger) : IPortfolioUnitOfWork
{
    private static readonly TimeSpan SignalWait = TimeSpan.FromSeconds(5);

    private bool _signalPending;

    public async ValueTask SaveAsync(CancellationToken cancellationToken)
    {
        var affected = ActiveSetSignal.IsAffectedBy(db.ChangeTracker);
        await db.SaveChangesAsync(cancellationToken);
        histories.Invalidate();
        await SignalAsync(affected);
    }

    public async ValueTask<bool> TrySaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            var affected = ActiveSetSignal.IsAffectedBy(db.ChangeTracker);
            await db.SaveChangesAsync(cancellationToken);
            histories.Invalidate();
            await SignalAsync(affected);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.CheckViolation or PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }

    public async ValueTask<Result> RunAsync(Func<CancellationToken, ValueTask<Result>> work, CancellationToken cancellationToken)
    {
        // The retrying execution strategy requires user transactions to run inside it; it may run the whole block again after a transient failure.
        var strategy = db.Database.CreateExecutionStrategy();
        var result = await strategy.ExecuteAsync(async token =>
        {
            try
            {
                return await AttemptAsync(work, token);
            }
            catch (DbUpdateConcurrencyException)
            {
                // A holding the work changed was changed by someone else meanwhile (a quote written between the work's read and its save, ADR 0019): the transaction is rolled back,
                // and the work runs once more from scratch, reading the rows as they are now (the work reads its own state). A second conflict is a failure of the run.
                return await AttemptAsync(work, token);
            }
        }, cancellationToken);
        var signal = result.IsSuccess && _signalPending;
        _signalPending = false;
        if (result.IsSuccess)
        {
            histories.Invalidate();
        }

        await SignalAsync(signal);
        return result;
    }

    /// <summary>Sends the signal now, or - inside a run's transaction - once it has committed.</summary>
    private async ValueTask SignalAsync(bool affected)
    {
        if (!affected)
        {
            return;
        }

        if (db.Database.CurrentTransaction is not null)
        {
            _signalPending = true;
            return;
        }

        using var wait = new CancellationTokenSource(SignalWait, clock);
        try
        {
            await notifier.ActiveSetChangedAsync(wait.Token);
        }
#pragma warning disable CA1031 // best effort: the change is committed; the feed notices it at its next recompute
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogSignalFailed(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The signal that the live feed's active set changed could not be sent")]
    private static partial void LogSignalFailed(ILogger logger, Exception exception);

    private async Task<Result> AttemptAsync(Func<CancellationToken, ValueTask<Result>> work, CancellationToken token)
    {
        _signalPending = false;
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        try
        {
            var result = await work(token);
            if (result.IsSuccess)
            {
                await transaction.CommitAsync(token);
            }
            else
            {
                await transaction.RollbackAsync(token);
                db.ChangeTracker.Clear();
            }

            return result;
        }
        catch
        {
            // Leaving the block rolls the transaction back; what was collected must not be saved by a later use of the context.
            db.ChangeTracker.Clear();
            throw;
        }
    }
}
