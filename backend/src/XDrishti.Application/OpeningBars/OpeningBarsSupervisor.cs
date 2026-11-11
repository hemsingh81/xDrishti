using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Market.Baskets;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.OpeningBars;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Live;
using XDrishti.Application.Platform.Services;
using XDrishti.Domain.Baskets.Derived;
using XDrishti.Domain.Market;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.OpeningBars;

/// <summary>
/// The opening-minute recorder's run owner in the feed host (BK-03b), beside the live feed's, the depth recorder's and the tick recorder's and never in their way: its own loop, its own try/catch, its own run record (<c>opening-bars</c>). Every
/// <see cref="Interval"/> it looks at the gates. <b>All three must be open</b> before anything touches a credential, a socket or a table: the deployment flag <c>OpeningBars:Enabled</c>, the owner's stored switch <c>baskets.opening_bars</c> and the
/// service <c>opening-bars</c> (enabled, inside its window on a trading day); a gate that cannot be read is a closed gate. The set is the derived universe of the previous session (capped, the most liquid first), and only while a frozen intraday
/// basket is switched on. Whatever the gates say, once the deployment flag is on and a basket is due at its formation time, the same loop asks the freeze (<see cref="FreezeOpeningBasketsCommand"/>) for the day: it forms the basket or writes the named
/// <c>not_formed</c> row, once. It never places an order and never calls anything but the market feed.
/// </summary>
internal sealed class OpeningBarsSupervisor(IServiceScopeFactory scopes, OpeningBarsRecorder session, OpeningBarsSettings settings, TimeProvider clock, ILogger<OpeningBarsSupervisor> logger) : IOpeningBarsSupervisor
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(2);

    public static readonly TimeSpan StopWait = TimeSpan.FromSeconds(20);

    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(1);

    /// <summary>The earliest formation time: 09:15 plus the minutes of basket 4. The freeze is not asked before it.</summary>
    private static readonly TimeOnly FirstFormation = OpeningBarsSettings.Open.AddMinutes(DerivedSchedule.GapperMinutes);

    private Running? _running;
    private DateOnly? _doneDay;
    private DateOnly? _frozenDay;
    private DateOnly? _prunedDay;
    private DateOnly? _lowDiskDay;
    private DateTimeOffset _resumeAt = DateTimeOffset.MinValue;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(Interval, clock);
            do
            {
                await TickAsync(cancellationToken);
            }
            while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // stopping
        }
        finally
        {
            await ShutdownAsync();
        }
    }

    /// <summary>One look at the gates and the day. Internal to the tests, which drive the clock. A failed look is logged and the next one tries again.</summary>
    internal async Task TickAsync(CancellationToken cancellationToken)
    {
        if (!settings.Enabled)
        {
            return; // gate 1: nothing below this line runs - no credentials, no socket, no table
        }

        try
        {
            await LookAsync(clock.GetUtcNow(), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // a failed look must not end the recorder; the next one is in a few seconds
        catch (Exception ex)
#pragma warning restore CA1031
        {
            OpeningBarsLog.SupervisorFailed(logger, ex);
        }
    }

    private async Task LookAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var today = IndiaTime.Today(now);
        // The gates are read first: a gate that cannot be read is a closed gate.
        var switchOn = false;
        ServiceSettings? service = null;
        try
        {
            switchOn = await services.GetRequiredService<IOpeningBarsSwitchStore>().IsOnAsync(cancellationToken);
            service = (await services.GetRequiredService<ServiceSettingsReader>().ReadAsync(OpeningBarsService.Definition, cancellationToken)).Settings;
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            OpeningBarsLog.Problem(logger, ex.GetType().Name);
        }

        // The daily retention touches a table, so it needs all three gates open (the deployment flag is gate 1 above), like everything else here; nothing is pruned while the recorder is off.
        if (switchOn && service is { Enabled: true } && _prunedDay != today)
        {
            _prunedDay = today; // once a day, whatever happens (a failure is tried again tomorrow)
            var removed = await services.GetRequiredService<IOpeningBarsStore>().PruneAsync(settings.KeepDays, cancellationToken);
            OpeningBarsLog.Pruned(logger, removed);
        }

        var calendar = await services.GetRequiredService<ITradingCalendar>().GetAsync(cancellationToken);
        if (!calendar.IsTradingDay(today))
        {
            await StopIfRunningAsync(services, now, cancellationToken); // nothing is due on a holiday or a weekend: no connection, no row
            return;
        }

        var wanted = switchOn && service is not null && FeedWindow.Decide(service, calendar, now, null).ShouldBeUp;
        wanted &= now < OpeningWindows.At(today, settings.RecordUntil) && _doneDay != today;
        if (_running is { Task.IsCompleted: true })
        {
            await EndRunAsync(services, now, cancellationToken);
        }

        if (_running is not null && !wanted)
        {
            await StopIfRunningAsync(services, now, cancellationToken);
        }
        else if (_running is null && wanted && now >= _resumeAt && _lowDiskDay != today)
        {
            await TryOpenRunAsync(services, calendar, today, now, cancellationToken);
        }

        if (_frozenDay != today && now >= OpeningWindows.At(today, FirstFormation))
        {
            var freeze = await services.GetRequiredService<ICommandHandler<FreezeOpeningBasketsCommand, OpeningFreezeRun>>().HandleAsync(new FreezeOpeningBasketsCommand(today), cancellationToken);
            if (freeze.IsSuccess)
            {
                OpeningBarsLog.Frozen(logger, freeze.Value.Waiting, freeze.Value.Baskets.Count);
                if (freeze.Value.Waiting == 0)
                {
                    _frozenDay = today;
                }
            }
        }
    }

    private async Task TryOpenRunAsync(IServiceProvider services, Domain.Market.Calendar.TradingCalendar calendar, DateOnly today, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var store = services.GetRequiredService<IOpeningBarsStore>();
        var free = await store.FreeBytesAsync(cancellationToken);
        if (free is not { } bytes || bytes < settings.MinFreeBytes) // unreadable free space counts as low
        {
            _lowDiskDay = today; // stays stopped for the day; the day's baskets are not formed, low_disk
            await store.AddEventAsync(today, new OpeningBarsEvent(OpeningBarsEvent.LowDisk, now), cancellationToken);
            OpeningBarsLog.LowDisk(logger);
            return;
        }

        var set = await BuildSetAsync(services, calendar, today, cancellationToken);
        if (set.Count == 0)
        {
            return; // no frozen intraday basket is switched on (or nothing to record): no connection
        }

        var runs = services.GetRequiredService<IServiceRunStore>();
        var run = ServiceRun.Start(OpeningBarsService.Id, ServiceRunTrigger.Scheduled, 1, now);
        if (!await runs.TryStartAsync(run, StaleAfter, cancellationToken))
        {
            return;
        }

        var stop = new CancellationTokenSource();
        _running = new Running(run, stop, session.RunAsync(today, set, stop.Token), today);
    }

    /// <summary>The derived universe of the previous session, capped to <c>OpeningBars:MaxInstruments</c> (the most liquid by ADV20), only while a frozen intraday basket is on. A share Dhan has no security id for is not recorded; the freeze then names it missing.</summary>
    private async Task<IReadOnlyList<OpeningSetEntry>> BuildSetAsync(IServiceProvider services, Domain.Market.Calendar.TradingCalendar calendar, DateOnly today, CancellationToken cancellationToken)
    {
        if (!services.GetRequiredService<IDerivedSettings>().Enabled
            || !(await services.GetRequiredService<IDerivedVersionStore>().ListDefinitionsAsync(cancellationToken)).Any(d => d.Enabled && DerivedCatalogue.Get(d.DefinitionNo).Cadence == DerivedCadence.FrozenIntraday))
        {
            return [];
        }

        var asOf = calendar.LastClosedSession(OpeningWindows.At(today, new TimeOnly(9, 0)));
        var all = await services.GetRequiredService<IDerivedUniverseReader>().ReadAsync(asOf.AddDays(-OpeningWindows.SetLookbackDays), asOf, cancellationToken);
        var (kept, _) = OpeningWindows.SetOf(all, asOf, settings.MaxInstruments);
        var securities = await services.GetRequiredService<IInstrumentSecurities>().FindAsync([.. kept.Select(k => "NSE:" + k.Bars.Symbol)], cancellationToken);
        return [.. kept.Select(k => securities.GetValueOrDefault("NSE:" + k.Bars.Symbol)).Where(s => s is not null && int.TryParse(s.SecurityId, out _)).Select(s => new OpeningSetEntry(s!.InstrumentId, int.Parse(s.SecurityId, System.Globalization.CultureInfo.InvariantCulture), s.Symbol))];
    }

    /// <summary>The session ended by itself: close the run with the day's counts and set up what follows.</summary>
    private async Task EndRunAsync(IServiceProvider services, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var running = _running!;
        OpeningSessionResult? result = null;
        try
        {
            result = await running.Task;
        }
#pragma warning disable CA1031 // a crashed session fails its run; the type is in the log
        catch (Exception ex)
#pragma warning restore CA1031
        {
            OpeningBarsLog.Problem(logger, ex.GetType().Name);
        }

        var message = result is null
            ? "The opening-minute recorder stopped unexpectedly. See the xd-feed log."
            : $"{result.End}: {result.Stats.Instruments} instruments, {result.Stats.Bars} bars, {result.Stats.Missing} missing minutes, {result.Stats.Late} late, {result.Stats.Duplicates} duplicates, {result.Stats.Gaps} gaps, {result.Stats.Outside} outside the minutes, median exchange-to-receive skew {result.Stats.SkewSeconds} s";
        switch (result?.End)
        {
            case OpeningSessionEnd.Until:
            case OpeningSessionEnd.ClockSkew: // not tried again today: the exchange time cannot be trusted
                _doneDay = running.Day;
                break;
            case OpeningSessionEnd.LowDisk:
                _lowDiskDay = running.Day;
                break;
            case OpeningSessionEnd.Stopped:
                break;
            default:
                _resumeAt = now + RetryAfter; // a refusal, no token or a crash: tried again in a minute while the window is open
                break;
        }

        var status = result?.End is OpeningSessionEnd.Until or OpeningSessionEnd.Stopped ? ServiceRunStatus.Succeeded : ServiceRunStatus.Failed;
        await services.GetRequiredService<IServiceRunStore>().CompleteAsync(running.Run.Id, status, message, now, cancellationToken);
        running.Stop.Dispose();
        _running = null;
    }

    private async Task StopIfRunningAsync(IServiceProvider services, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (_running is null)
        {
            return;
        }

        await _running.Stop.CancelAsync();
        try
        {
            await _running.Task.WaitAsync(StopWait, clock, cancellationToken);
        }
        catch (TimeoutException)
        {
            return; // not ended in time: the next look waits again
        }
#pragma warning disable CA1031 // the session reports its own failures; here only the end matters
        catch (Exception)
#pragma warning restore CA1031
        {
            // see above
        }

        await EndRunAsync(services, now, cancellationToken);
    }

    private async Task ShutdownAsync()
    {
        if (_running is null)
        {
            return;
        }

        await _running.Stop.CancelAsync();
        try
        {
            await _running.Task.WaitAsync(StopWait, clock, CancellationToken.None);
        }
#pragma warning disable CA1031 // the host is stopping
        catch (Exception)
#pragma warning restore CA1031
        {
            // see above
        }
    }

    private sealed record Running(ServiceRun Run, CancellationTokenSource Stop, Task<OpeningSessionResult> Task, DateOnly Day);
}
