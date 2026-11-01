using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.OptionQuality;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Options;
using XDrishti.Domain.Options.Quality;

namespace XDrishti.Application.OptionQuality;

/// <summary>
/// One run of the option-archive quality checks (spec P14-04, ADR 0178): chain sessions that have no result yet (at most <see cref="IOptionQualitySettings.MaxChainSessionsPerRun"/>), the recent trading days with no session row (reported as not recorded),
/// and finished rolling fetch windows that have no result yet (at most <see cref="IOptionQualitySettings.MaxRollingWindowsPerRun"/>). It reads the archive through <see cref="IOptionArchiveReader"/> in ranges bounded by a day or a window, writes only result rows
/// and stops between two units of work when <see cref="IOptionQualitySettings.MaxRunMinutes"/> have passed. It does no I/O but through its ports and makes no network call.
/// </summary>
internal sealed class OptionArchiveQualityRunner(
    IOptionArchiveReader archive,
    IOptionReference reference,
    IOptionQualityStore store,
    ITradingCalendar calendar,
    IOptionQualitySettings settings,
    TimeProvider clock,
    ILogger<OptionArchiveQualityRunner> logger)
{
    /// <summary>Rolling windows are fetched from the pending list in batches of this size until the limit, the deadline or the end of the list.</summary>
    private const int RollingBatch = 200;

    /// <summary>A run stops after this many units failed (a broken database should not be hammered).</summary>
    private const int MaxFailures = 10;

    public async ValueTask<OptionQualityRunSummary> RunAsync(string trigger, CancellationToken cancellationToken)
    {
        var started = clock.GetUtcNow();
        var deadline = started.AddMinutes(settings.MaxRunMinutes);
        var thresholds = settings.Thresholds;
        var today = IndiaTime.Today(started);
        var runId = await store.StartRunAsync(cancellationToken);
        var sessions = 0;
        var results = 0;
        var failures = 0;
        var stopped = false;
        try
        {
            var pendingChain = await archive.PendingChainSessionsAsync(today, settings.MaxChainSessionsPerRun, cancellationToken);
            OptionQualityLog.Run(logger, trigger, pendingChain.Count, settings.MaxRollingWindowsPerRun);

            foreach (var date in pendingChain)
            {
                if (clock.GetUtcNow() >= deadline || failures >= MaxFailures)
                {
                    stopped = true;
                    break;
                }

                try
                {
                    results += await CheckChainSessionAsync(runId, date, thresholds, cancellationToken);
                    sessions++;
                }
#pragma warning disable CA1031 // one session that cannot be checked must not block the others: it is logged, counted in the run note and left pending
                catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
                {
                    failures++;
                    OptionQualityLog.Failed(logger, QualityCodes.Chain, ex.GetType().Name);
                }
            }

            if (!stopped)
            {
                try
                {
                    results += await ReportNotRecordedAsync(runId, today, cancellationToken);
                }
#pragma warning disable CA1031 // see above
                catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
                {
                    failures++;
                    OptionQualityLog.Failed(logger, "not_recorded", ex.GetType().Name);
                }
            }

            if (!stopped)
            {
                var rolling = await CheckRollingAsync(runId, today, deadline, thresholds, cancellationToken);
                sessions += rolling.Sessions;
                results += rolling.Results;
                failures += rolling.Failures;
                stopped = rolling.Stopped || failures >= MaxFailures;
            }
        }
        finally
        {
            var note = failures > 0 ? $"{failures} units failed and were left for the next run" + (stopped ? "; stopped at its bounds" : string.Empty) : stopped ? "stopped at its bounds" : null;
            try
            {
                await store.FinishRunAsync(runId, trigger, started, clock.GetUtcNow(), sessions, results, note, CancellationToken.None);
            }
#pragma warning disable CA1031 // the closing row must not hide the exception that ended the run
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                OptionQualityLog.Failed(logger, "run", ex.GetType().Name);
            }
        }

        OptionQualityLog.Done(logger, sessions, results, stopped);
        return new OptionQualityRunSummary(runId, sessions, results, stopped, failures);
    }

    private async ValueTask<(int Sessions, int Results, int Failures, bool Stopped)> CheckRollingAsync(long runId, DateOnly today, DateTimeOffset deadline, OptionQualityThresholds thresholds, CancellationToken cancellationToken)
    {
        var epochs = await reference.EpochsAsync(cancellationToken);
        var cal = await calendar.GetAsync(cancellationToken);
        var symbols = new Dictionary<Guid, string>();
        var failed = new HashSet<string>(StringComparer.Ordinal);
        var done = 0;
        var sessions = 0;
        var results = 0;
        while (done + failed.Count < settings.MaxRollingWindowsPerRun)
        {
            var batch = Math.Min(RollingBatch, settings.MaxRollingWindowsPerRun - done);
            var pending = await archive.PendingRollingWindowsAsync(today, batch + failed.Count, cancellationToken);
            var fresh = pending.Where(w => !failed.Contains(Id(w))).Take(batch).ToList();
            if (fresh.Count == 0)
            {
                break;
            }

            var unknown = fresh.Select(w => w.UnderlyingId).Where(id => !symbols.ContainsKey(id)).Distinct().ToList();
            foreach (var known in await reference.SymbolsAsync(unknown, cancellationToken))
            {
                symbols[known.Key] = known.Value;
            }

            foreach (var window in fresh)
            {
                if (clock.GetUtcNow() >= deadline || failed.Count >= MaxFailures)
                {
                    OptionQualityLog.Session(logger, QualityCodes.Rolling, done, results);
                    return (sessions, results, failed.Count, true);
                }

                try
                {
                    var written = await CheckRollingWindowAsync(runId, window, symbols.GetValueOrDefault(window.UnderlyingId, string.Empty), epochs, cal, thresholds, cancellationToken);
                    sessions += written.Sessions;
                    results += written.Results;
                    done++;
                }
#pragma warning disable CA1031 // see RunAsync
                catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
                {
                    failed.Add(Id(window));
                    OptionQualityLog.Failed(logger, QualityCodes.Rolling, ex.GetType().Name);
                }
            }
        }

        OptionQualityLog.Session(logger, QualityCodes.Rolling, done, results);
        return (sessions, results, failed.Count, false);
    }

    private static string Id(RollingWindow window) => window.FetchKey + "@" + window.From.DayNumber;

    private async ValueTask<int> CheckChainSessionAsync(long runId, DateOnly date, OptionQualityThresholds thresholds, CancellationToken cancellationToken)
    {
        var summaries = await archive.ChainRequestSummariesAsync(date, cancellationToken);
        var rows = new List<OptionQualityRow>();
        foreach (var summary in summaries)
        {
            var points = await archive.ChainPointsAsync(summary.UnderlyingId, date, cancellationToken);
            var lots = await reference.LotSizesAsync(summary.UnderlyingId, cancellationToken);
            var lot = LotSizeLookup.InForce(lots, summary.UnderlyingId, date);
            QualityCheck[] checks =
            [
                ChainCoverageCheck.Evaluate(true, summary.Planned, summary.Ok, summary.Outcomes, thresholds),
                ParityCheck.Evaluate(points, date, thresholds),
                GreeksCheck.Evaluate(points, date, thresholds),
                OiVolumeCheck.Evaluate(points, lot.Available ? lot.LotSize : null, thresholds),
            ];
            rows.AddRange(checks.Select(c => new OptionQualityRow(date, summary.UnderlyingId, QualityCodes.Chain, null, null, c)));
        }

        if (rows.Count == 0)
        {
            return 0; // the pending query only offers sessions that had requests, so this is a race with the recorder: nothing to write, offered again next run
        }

        await store.AddResultsAsync(runId, rows, cancellationToken);
        OptionQualityLog.Session(logger, QualityCodes.Chain, summaries.Count, rows.Count);
        return rows.Count;
    }

    private async ValueTask<int> ReportNotRecordedAsync(long runId, DateOnly today, CancellationToken cancellationToken)
    {
        var first = await archive.FirstChainSessionAsync(cancellationToken);
        if (first is null)
        {
            return 0;
        }

        var cal = await calendar.GetAsync(cancellationToken);
        var last = cal.LastClosedSession(clock.GetUtcNow());
        var from = first.Value > today.AddDays(-settings.LookbackDays) ? first.Value : today.AddDays(-settings.LookbackDays);
        if (from > last)
        {
            return 0;
        }

        var recorded = await archive.ChainSessionDatesAsync(from, last, cancellationToken);
        var checkedDays = await archive.ChainResultDatesAsync(from, last, cancellationToken);
        var missing = new List<DateOnly>();
        for (var day = from; day <= last; day = day.AddDays(1))
        {
            if (cal.IsTradingDay(day) && !recorded.Contains(day) && !checkedDays.Contains(day))
            {
                missing.Add(day);
            }
        }

        if (missing.Count == 0)
        {
            return 0;
        }

        var underlyings = await archive.RecentChainUnderlyingsAsync(from.AddDays(-60), cancellationToken);
        var rows = missing.SelectMany(day => underlyings.Select(u => new OptionQualityRow(day, u, QualityCodes.Chain, null, null, ChainCoverageCheck.Evaluate(false, 0, 0, new Dictionary<string, int>(), settings.Thresholds)))).ToList();
        if (rows.Count > 0)
        {
            await store.AddResultsAsync(runId, rows, cancellationToken);
        }

        return rows.Count;
    }

    private async ValueTask<(int Sessions, int Results)> CheckRollingWindowAsync(
        long runId,
        RollingWindow window,
        string symbol,
        IReadOnlyList<MarketEpoch> epochs,
        TradingCalendar cal,
        OptionQualityThresholds thresholds,
        CancellationToken cancellationToken)
    {
        var points = await archive.RollingPointsAsync(window, cancellationToken);
        var step = await reference.StrikeStepAsync(window.UnderlyingId, window.From, cancellationToken);
        var byDay = points.GroupBy(p => IndiaTime.Today(p.Ts)).ToDictionary(g => g.Key, g => (IReadOnlyList<RollingPoint>)[.. g]);
        var expected = RollingCoverageCheck.ExpectedBars(window.IntervalMinutes);
        // A day is a session when the calendar says so and, if any series of the underlying has bars in the window, when one has bars that day: a past holiday the calendar does not know is not a coverage fail.
        var active = await archive.ActiveDaysAsync(window.UnderlyingId, window.From, window.To, cancellationToken);
        var rows = new List<OptionQualityRow>();
        var days = 0;
        for (var day = window.From; day < window.To; day = day.AddDays(1))
        {
            if (!cal.IsTradingDay(day) || (active.Count > 0 && !active.Contains(day)))
            {
                continue;
            }

            var bars = byDay.GetValueOrDefault(day, []);
            var outOfEra = OutOfEra.IsOutOfEra(symbol, window.ExpiryFlag, day, epochs);
            rows.Add(new OptionQualityRow(day, window.UnderlyingId, QualityCodes.Rolling, null, window.FetchKey, RollingCoverageCheck.Evaluate(bars.Count, window.IntervalMinutes, window.Outcome, outOfEra, thresholds)));
            rows.Add(new OptionQualityRow(day, window.UnderlyingId, QualityCodes.Rolling, null, window.FetchKey, StitchingCheck.Evaluate(bars, expected, step, thresholds)));
            days++;
        }

        if (rows.Count > 0)
        {
            await store.AddResultsAsync(runId, rows, cancellationToken);
        }

        return (days, rows.Count);
    }
}
