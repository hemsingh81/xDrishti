using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.SimAll;
using XDrishti.Domain.Research.SimAll;

namespace XDrishti.Application.Replay.Job;

/// <summary>The yield rules of the replay jobs (SR-01 part B; shared by the SR-04 simulation runner, ADR 0293): the rules of <c>SimAllRunner.YieldAsync</c> and a simulate-everything chunk in flight. One instance per runner: it keeps the once-a-minute backfill answer.</summary>
public sealed class ReplayYieldRules(SystemReplayOptions options, TimeProvider time)
{
    /// <summary>No run starts a session within this time of the market window opening (the session is short, the margin is as for the simulate-everything chunk).</summary>
    private static readonly TimeSpan WindowMargin = TimeSpan.FromMinutes(30);

    private static readonly TimeSpan BackfillEvery = TimeSpan.FromMinutes(1);

    private DateTimeOffset? _backfillCheckedAt;
    private bool _backfillIncomplete;

    /// <summary>Why the job should wait now, or null. The rules of <c>SimAllRunner.YieldAsync</c>, and a simulate-everything chunk in flight. Cheap checks first; the backfill is asked once a minute.</summary>
    public async ValueTask<string?> ReasonAsync(IServiceProvider services, bool includeBackfill, CancellationToken cancellationToken, bool marketHours = true)
    {
        var now = time.GetUtcNow();
        if (services.GetRequiredService<IFreeDiskProbe>().FreeBytes() is not { } free || free < options.MinFreeBytes)
        {
            return SimAllPause.LowDisk;
        }

        if (marketHours && !options.RunInMarketHours)
        {
            var calendar = await services.GetRequiredService<ITradingCalendar>().GetAsync(cancellationToken);
            if (SimAllPause.InMarketHours(calendar, now) || (SimAllPause.NextWindowStart(calendar, now) is { } next && next - now < WindowMargin))
            {
                return SimAllPause.MarketHours;
            }
        }

        var gate = services.GetRequiredService<ISimAllBackfillGate>();
        if (await gate.DataJobRunningAsync(cancellationToken))
        {
            return SimAllPause.DataJob; // a bar-history job or a recovery pass
        }

        if (await services.GetRequiredService<ISimAllStore>().GetActiveAsync(cancellationToken) is { State: SimAllStates.Running })
        {
            return ReplayRunner.SimAllRunning;
        }

        if (includeBackfill)
        {
            if (_backfillCheckedAt is not { } checkedAt || now - checkedAt >= BackfillEvery)
            {
                _backfillIncomplete = await gate.IncompleteReasonAsync(cancellationToken) is not null;
                _backfillCheckedAt = now;
            }

            if (_backfillIncomplete)
            {
                return SimAllPause.Backfill;
            }
        }

        return null;
    }

}
