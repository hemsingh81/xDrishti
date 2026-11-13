using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.OptionBackfill;
using XDrishti.Application.Recording;
using XDrishti.Domain.Market;

namespace XDrishti.Application.OptionBackfill;

/// <summary>
/// One night of the expired-options backfill (P14-03 criteria 3, 4, 5, 7): it walks the planned keys in their fixed order, skips the ones the fetch log says are done or given up (so a restart never fetches a done key twice), and fetches the rest one at
/// a time through <see cref="IOptionBackfillClient"/>, which keeps the spacing of the whole process. Each attempt writes one fetch-log row (and its bars, with <c>ON CONFLICT DO NOTHING</c>) in one transaction. A failed attempt (rate limit, timeout,
/// server error, unreadable body) is tried again at once up to three times and then the key is <b>given up</b>, left and listed; <c>DH-904</c> pauses 60 seconds and halves the rate for the rest of the night; a refused token or a missing plan ends
/// the run. It runs only inside the night window, never beyond the day's request cap, and the disk guard looks before every window (own cap, shared floor): it stops and <b>deletes nothing</b>. It reads no bar.
/// </summary>
internal sealed class OptionBackfillJob(IServiceScopeFactory scopes, OptionBackfillSettings settings, TimeProvider clock, ILogger<OptionBackfillJob> logger) : IBackfillSession
{
    /// <summary>The pause after a rate limit.</summary>
    public static readonly TimeSpan RatePause = TimeSpan.FromSeconds(60);

    /// <summary>The longest gap between two requests, however often the rate was halved.</summary>
    public static readonly TimeSpan MaxSpacing = TimeSpan.FromSeconds(30);

    /// <summary>Answers in a row that could not be stored before the run ends.</summary>
    public const int StoreFailuresBeforeStop = 3;

    /// <summary>Rate-limited answers in a row, across keys, before the run ends.</summary>
    public const int RateLimitsBeforeStop = 5;

    /// <summary>Keys given up in a row before the run ends.</summary>
    public const int GivenUpBeforeStop = 5;

    private volatile BackfillRunStats _stats = new(0, 0, 0, 0, 0, 0, 0);
    private volatile string _state = BackfillStates.Backfilling;
    private volatile bool _freeReadable = true;
    private bool _warned;
    private TimeSpan _spacing;
    private DateOnly? _spacingNight;
    private int _unlogged;
    private DateTimeOffset _unloggedDay;

    public string State => _state;

    public BackfillRunStats Stats => _stats;

    public bool FreeSpaceReadable => _freeReadable;

    public async Task<BackfillSessionResult> RunAsync(IReadOnlyList<BackfillTarget> targets, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targets);
        _stats = new BackfillRunStats(0, 0, 0, 0, 0, 0, 0);
        _state = BackfillStates.Backfilling;
        try
        {
            var end = await RunNightAsync(targets, cancellationToken);
            OptionBackfillLog.Done(logger, _stats.Requests, _stats.Ok, _stats.Empty, _stats.Gaps, _stats.GivenUp, _stats.Bars);
            return new BackfillSessionResult(end, _stats);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new BackfillSessionResult(BackfillSessionEnd.Stopped, _stats);
        }
    }

    private async Task<BackfillSessionEnd> RunNightAsync(IReadOnlyList<BackfillTarget> targets, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IOptionBackfillClient>();
        var store = scope.ServiceProvider.GetRequiredService<IRollingBarStore>();
        var log = scope.ServiceProvider.GetRequiredService<IBackfillFetchLog>();
        var started = clock.GetUtcNow();
        var states = new Dictionary<BackfillKey, KeyState>(await log.LoadStateAsync(cancellationToken));
        var dayStart = BackfillWindow.DayStart(started);
        var sentToday = await log.CountSentSinceAsync(dayStart, cancellationToken);
        var night = BackfillWindow.NightOf(started);
        if (_spacingNight != night)
        {
            (_spacingNight, _spacing) = (night, settings.Spacing); // the halved rate lasts the rest of the night, across runs; a new night starts at the configured rate
        }

        _warned = false;
        int storeStreak = 0, rateStreak = 0, givenUpStreak = 0;
        if (_unloggedDay != dayStart)
        {
            (_unloggedDay, _unlogged) = (dayStart, 0);
        }

        sentToday += _unlogged;
        Report(sentToday);

        foreach (var item in BackfillPlanner.Plan(targets, settings, IndiaTime.Today(started)))
        {
            states.TryGetValue(item.Key, out var state);
            if (state is { IsDone: true } or { IsGivenUp: true })
            {
                continue;
            }

            var attempts = state?.Attempts ?? 0;
            var failed = state?.Failed ?? 0;
            var storeFailures = state?.StoreFailures ?? 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var now = clock.GetUtcNow();
                if (!BackfillWindow.IsOpen(now))
                {
                    return BackfillSessionEnd.WindowEnd;
                }

                if (BackfillWindow.DayStart(now) != dayStart)
                {
                    dayStart = BackfillWindow.DayStart(now);
                    sentToday = 0;
                    (_unloggedDay, _unlogged) = (dayStart, 0);
                }

                if (sentToday >= settings.MaxRequestsPerDay)
                {
                    OptionBackfillLog.DailyCap(logger, sentToday);
                    _state = BackfillStates.DailyCap;
                    return BackfillSessionEnd.DailyCap;
                }

                if (await GuardDiskAsync(store, cancellationToken) is { } stop)
                {
                    await TryAppendAsync(store, new BackfillFetchRecord(item.Key, attempts + 1, now, now, BackfillOutcomes.DiskStop, null, null, 0, 0), [], cancellationToken);
                    return stop;
                }

                var reply = await client.SendAsync(item.ToRequest(), _spacing, cancellationToken);
                if (reply.NoCredentials)
                {
                    return BackfillSessionEnd.NoToken;
                }

                sentToday++;
                attempts++;
                var (outcome, bars) = Judge(reply, item);
                var record = new BackfillFetchRecord(item.Key, attempts, now, clock.GetUtcNow(), outcome, reply.HttpStatus, reply.DhanCode, bars.Count, reply.LatencyMs);
                var inserted = await TryAppendAsync(store, record, bars, cancellationToken);
                OptionBackfillLog.Window(logger, outcome, reply.HttpStatus ?? 0, bars.Count, reply.LatencyMs);
                if (inserted is null)
                {
                    // The answer is thrown away, but the request was sent: a row without bars says so (and keeps the daily cap honest across a restart); if even that cannot be written, the count is kept in memory.
                    var marker = record with { Outcome = BackfillOutcomes.StoreFailed, Bars = 0 };
                    if (await TryAppendAsync(store, marker, [], cancellationToken) is null)
                    {
                        _unlogged++;
                    }

                    storeStreak++;
                    storeFailures++;
                    states[item.Key] = new KeyState(attempts, failed, null, storeFailures);
                }
                else
                {
                    storeStreak = 0;
                }

                rateStreak = outcome == BackfillOutcomes.RateLimited ? rateStreak + 1 : 0;

                // The exits that depend on what Dhan said come first, whether or not the answer could be stored.
                if (outcome == BackfillOutcomes.BadRequest)
                {
                    OptionBackfillLog.RunBreaker(logger, "bad_request");
                    Add(requests: 1, gaps: 1, today: sentToday);
                    _state = BackfillStates.BadRequest;
                    return BackfillSessionEnd.BadRequest;
                }

                if (outcome == BackfillOutcomes.TokenRefused)
                {
                    OptionBackfillLog.TokenRefused(logger);
                    Add(requests: 1, gaps: 1, today: sentToday);
                    return BackfillSessionEnd.TokenRefused;
                }

                if (outcome == BackfillOutcomes.NotSubscribed)
                {
                    Add(requests: 1, gaps: 1, today: sentToday);
                    return BackfillSessionEnd.NotSubscribed;
                }

                if (outcome == BackfillOutcomes.RateLimited)
                {
                    // A rate limit is not the key's fault: no failed attempt. The pause comes before anything else, and the rate is halved for the rest of the night.
                    OptionBackfillLog.Gap(logger, outcome, reply.DhanCode ?? "-", attempts, item.Key.UnderlyingId, SeriesOf(item.Key), item.Key.From);
                    Add(requests: 1, gaps: 1, today: sentToday);
                    _spacing = TimeSpan.FromTicks(Math.Min(_spacing.Ticks * 2, MaxSpacing.Ticks));
                    OptionBackfillLog.RateLimited(logger, (int)RatePause.TotalSeconds, (int)_spacing.TotalMilliseconds);
                    if (rateStreak >= RateLimitsBeforeStop)
                    {
                        OptionBackfillLog.RunBreaker(logger, "rate_limited");
                        _state = BackfillStates.RatePaused;
                        return BackfillSessionEnd.RateLimited;
                    }

                    await Task.Delay(RatePause, clock, cancellationToken);
                }
                else if (inserted is not null && BackfillOutcomes.IsDone(outcome))
                {
                    states[item.Key] = new KeyState(attempts, failed, outcome, storeFailures);
                    givenUpStreak = 0;
                    Add(requests: 1, ok: outcome == BackfillOutcomes.Ok ? 1 : 0, empty: outcome == BackfillOutcomes.Ok ? 0 : 1, bars: inserted.Value, today: sentToday);
                    break;
                }
                else if (inserted is not null)
                {
                    failed++; // a timeout, a server error or an unreadable body
                    states[item.Key] = new KeyState(attempts, failed, null, storeFailures);
                    OptionBackfillLog.Gap(logger, outcome, reply.DhanCode ?? "-", attempts, item.Key.UnderlyingId, SeriesOf(item.Key), item.Key.From);
                    Add(requests: 1, gaps: 1, today: sentToday);
                }
                else
                {
                    Add(requests: 1, gaps: 1, today: sentToday);
                }

                if (inserted is null)
                {
                    if (storeStreak >= StoreFailuresBeforeStop)
                    {
                        OptionBackfillLog.RunBreaker(logger, "store_failed");
                        _state = BackfillStates.StoreFailed;
                        return BackfillSessionEnd.StoreFailed;
                    }

                    if (storeFailures >= KeyState.MaxStoreFailures && GiveUp(item, attempts, ref givenUpStreak, sentToday))
                    {
                        return BackfillSessionEnd.TooManyGivenUp;
                    }

                    break; // the key stays open for the next night (or is given up after three answers that cannot be stored)
                }

                if (failed >= KeyState.MaxFailedAttempts)
                {
                    if (GiveUp(item, attempts, ref givenUpStreak, sentToday))
                    {
                        return BackfillSessionEnd.TooManyGivenUp;
                    }

                    break;
                }
            }
        }

        _state = BackfillStates.Done;
        return BackfillSessionEnd.Completed;
    }

    /// <summary>The outcome of a reply and, for a good one, its bars: an answer that does not parse is <c>bad_body</c>, one with no bar is <c>empty</c>.</summary>
    private static (string Outcome, IReadOnlyList<BackfillBar> Bars) Judge(BackfillReply reply, BackfillItem item)
    {
        if (reply.Outcome != BackfillOutcomes.Ok)
        {
            return (reply.Outcome, []);
        }

        var parsed = RollingBarParser.Parse(reply.Body ?? [], item);
        if (parsed.Outcome != BackfillOutcomes.Ok)
        {
            return (BackfillOutcomes.BadBody, []);
        }

        return parsed.Bars.Count == 0 ? (BackfillOutcomes.Empty, []) : (BackfillOutcomes.Ok, parsed.Bars);
    }

    /// <summary>The disk guard before a window: its own cap, the shared floor, a warning at 80 %. An unreadable free space leaves the size cap alone in force.</summary>
    private async Task<BackfillSessionEnd?> GuardDiskAsync(IRollingBarStore store, CancellationToken cancellationToken)
    {
        var stored = await store.StoredBytesAsync(cancellationToken);
        var free = await store.FreeBytesAsync(cancellationToken);
        if (free is null && _freeReadable)
        {
            OptionBackfillLog.FreeSpaceUnreadable(logger);
        }

        _freeReadable = free is not null;
        var verdict = RecorderDiskGuard.Check(stored, free, settings.MaxStoredBytes, settings.MinFreeBytes);
        switch (verdict)
        {
            case RecorderDiskVerdict.OverBudget:
                OptionBackfillLog.DiskStop(logger, "disk_budget");
                _state = RecorderStates.DiskBudget;
                return BackfillSessionEnd.DiskBudget;
            case RecorderDiskVerdict.LowFree:
                OptionBackfillLog.DiskStop(logger, "disk_free");
                _state = RecorderStates.DiskFree;
                return BackfillSessionEnd.DiskFree;
            case RecorderDiskVerdict.Warning when !_warned:
                _warned = true;
                OptionBackfillLog.DiskWarning(logger, stored / (1024 * 1024), settings.MaxStoredBytes / (1024 * 1024));
                break;
            default:
                break;
        }

        return null;
    }

    /// <summary>Writes the attempt; a store that fails is logged by type and is not an ok (null), and the session goes on.</summary>
    private async Task<int?> TryAppendAsync(IRollingBarStore store, BackfillFetchRecord record, IReadOnlyList<BackfillBar> bars, CancellationToken cancellationToken)
    {
        try
        {
            return await store.AppendAsync(record, bars, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            OptionBackfillLog.StoreFailed(logger, ex.GetType().Name);
            return null;
        }
    }

    /// <summary>Leaves a key: logged with its ids, counted; true when this is the fifth key in a row and the run must end.</summary>
    private bool GiveUp(BackfillItem item, int attempts, ref int givenUpStreak, int sentToday)
    {
        OptionBackfillLog.GivenUp(logger, attempts, item.Key.UnderlyingId, SeriesOf(item.Key), item.Key.From);
        Add(givenUp: 1, today: sentToday);
        if (++givenUpStreak < GivenUpBeforeStop)
        {
            return false;
        }

        OptionBackfillLog.RunBreaker(logger, "too_many_given_up");
        _state = BackfillStates.TooManyFailures;
        return true;
    }

    private static string SeriesOf(BackfillKey key) => $"{key.ExpiryFlag}{key.ExpiryCode}{key.Offset:+0;-0;0}{key.Side}/{key.IntervalMinutes}m";

    private void Report(int today) => _stats = _stats with { RequestsToday = today };

    private void Add(int requests = 0, int ok = 0, int empty = 0, int gaps = 0, int givenUp = 0, long bars = 0, int today = 0)
    {
        var s = _stats;
        _stats = new BackfillRunStats(s.Requests + requests, s.Ok + ok, s.Empty + empty, s.Gaps + gaps, s.GivenUp + givenUp, s.Bars + bars, today > 0 ? today : s.RequestsToday);
    }
}
