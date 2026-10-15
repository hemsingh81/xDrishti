using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Heal;

/// <summary>
/// Notes how an attempt of a sync ended in the health figures (P2-16, ADR 0027). It is bookkeeping about a sync that has ended already, so it never fails the sync: a database error is logged and swallowed (cancellation is not). The failure is noted with the
/// time of the clock; reasons are the codes of <see cref="SyncFailures"/>.
/// </summary>
internal sealed class SyncHealthRecorder(ISyncHealthStore health, TimeProvider clock, ILogger<SyncHealthRecorder> logger)
{
    public async ValueTask FailedAsync(Guid accountId, SyncHealthKind kind, string reason, CancellationToken cancellationToken)
    {
        try
        {
            await health.RecordFailureAsync(accountId, kind, reason, clock.GetUtcNow(), cancellationToken);
            var name = kind.ToString().ToLowerInvariant();
            HealLog.Failed(logger, name, reason);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // the health figures are bookkeeping: the sync that ended must not fail because they could not be written
        catch (Exception ex)
#pragma warning restore CA1031
        {
            HealLog.RecordFailed(logger, ex);
        }
    }

    public async ValueTask SucceededAsync(Guid accountId, SyncHealthKind kind, CancellationToken cancellationToken)
    {
        try
        {
            await health.RecordSuccessAsync(accountId, kind, clock.GetUtcNow(), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // see FailedAsync
        catch (Exception ex)
#pragma warning restore CA1031
        {
            HealLog.RecordFailed(logger, ex);
        }
    }

    /// <summary>The kind was read with the Dhan mapping of <paramref name="version"/> (ADR 0027).</summary>
    public async ValueTask MapperVersionAsync(Guid accountId, SyncHealthKind kind, int version, CancellationToken cancellationToken)
    {
        try
        {
            await health.StampMapperVersionAsync(accountId, kind, version, clock.GetUtcNow(), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // see FailedAsync; the windows are read again at the next run, which is harmless
        catch (Exception ex)
#pragma warning restore CA1031
        {
            HealLog.RecordFailed(logger, ex);
        }
    }

    /// <summary>An earlier-history read of the trades was made (ADR 0027): how far it reached and whether no earlier year is read any more.</summary>
    public async ValueTask DeepenedAsync(Guid accountId, DateOnly? deepenedTo, bool exhausted, DateOnly on, CancellationToken cancellationToken)
    {
        try
        {
            await health.RecordDeepenedAsync(accountId, deepenedTo, exhausted, on, clock.GetUtcNow(), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // see FailedAsync; the year is read again tomorrow, which is harmless
        catch (Exception ex)
#pragma warning restore CA1031
        {
            HealLog.RecordFailed(logger, ex);
        }
    }
}
