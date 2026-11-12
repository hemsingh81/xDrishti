using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Market.History;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Market.History;

/// <summary>One probe at a time per process (CF-04e decision D5); a second one is refused as <c>history.run_active</c>.</summary>
public sealed class HistoryProbeGate
{
    private int _busy;

    public bool TryEnter() => Interlocked.CompareExchange(ref _busy, 1, 0) == 0;

    public void Leave() => Volatile.Write(ref _busy, 0);
}

/// <summary>What both web acts check after the password (CF-04e decision D4): no run active, then the run window. Audit rows carry counts and codes only.</summary>
internal sealed class HistoryActGuard(IServiceRunStore runs, IBackfillSettings settings, IAccountEventLog events, IPlatformUnitOfWork unitOfWork, TimeProvider clock)
{
    public async ValueTask<Error?> CheckAsync(CancellationToken cancellationToken)
    {
        if ((await runs.GetRunningServicesAsync(cancellationToken)).Contains(HistoryBackfillService.Id))
        {
            return HistoryActErrors.RunActive();
        }

        return HistoryBackfillService.InWindow(clock.GetUtcNow(), settings.RunWindowFrom, settings.RunWindowTo)
            ? null
            : HistoryActErrors.OutsideWindow(settings.RunWindowFrom, settings.RunWindowTo);
    }

    /// <summary>Writes the audit row of an act that passed the password: counts and codes only, never a symbol, price, URL or token. A failed write is not allowed to undo the act.</summary>
    public async ValueTask AuditAsync(AccountEventType type, string detail, string actor)
    {
        events.Record(AccountEvent.Record(type, null, detail, actor, clock.GetUtcNow()));
        await unitOfWork.TrySaveAsync(CancellationToken.None);
    }
}
