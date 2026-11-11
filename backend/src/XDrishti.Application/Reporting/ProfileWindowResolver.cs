using System.Globalization;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Abstractions.Reporting;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Reporting;

namespace XDrishti.Application.Reporting;

/// <summary>The window of a profile read, with the index and the newest stored day of it.</summary>
/// <param name="From">First day.</param>
/// <param name="To">Last day.</param>
/// <param name="Newest">The newest stored daily bar of the index (or today without one): a new bar changes the cache key.</param>
/// <param name="View">The window as the answer prints it.</param>
/// <param name="Index">The index in force; null when the master list has none.</param>
/// <param name="IndexIsDefault">The owner has not chosen the index.</param>
/// <param name="IndexName">The index as the owner reads it (the default's name even while it is missing).</param>
/// <param name="IndexMissing">Why there is no index; null when there is one.</param>
internal sealed record ResolvedWindow(DateOnly From, DateOnly To, DateOnly Newest, BenchmarkWindowView View, BenchmarkInstrument? Index, bool IndexIsDefault, string IndexName, string? IndexMissing);

/// <summary>
/// Turns the raw query of a profile or ranking call into a window and an index (BK-07 criterion 1): <c>from</c> and <c>to</c> default to the last sessions ending at the newest stored daily bar of the index, said in <c>window.source</c>. One small read of the index finds that
/// bar; the bars of the window are read later, once, and not at all when the answer is cached.
/// </summary>
internal sealed class ProfileWindowResolver(IBarQueries bars, IPortfolioBenchmark benchmark, IBasketBenchmark basketBenchmark, ITradingCalendar calendars, BasketProfileSettings settings, ReportBenchmarkOptions limits, TimeProvider time)
{
    /// <summary>Sessions of the minimum window.</summary>
    public const int MinSessions = 20;

    public async ValueTask<Result<ResolvedWindow>> ResolveAsync(string? fromText, string? toText, string? indexText, CancellationToken cancellationToken)
    {
        var from = Parse(fromText, "from");
        var to = Parse(toText, "to");
        foreach (var d in new[] { from, to })
        {
            if (d.IsFailure)
            {
                return Result.Failure<ResolvedWindow>(d.Error);
            }
        }

        BenchmarkInstrument? index;
        var isDefault = true;
        string indexName;
        string? indexMissing = null;
        if (!string.IsNullOrWhiteSpace(indexText))
        {
            if (!Guid.TryParse(indexText.Trim(), out var indexId))
            {
                return Result.Failure<ResolvedWindow>(ReportErrors.Invalid("index is an instrument id."));
            }

            index = await benchmark.FindAsync(indexId, cancellationToken);
            if (index is null)
            {
                return Result.Failure<ResolvedWindow>(ReportErrors.Invalid($"index '{indexId:D}' is not an instrument of the master list."));
            }

            isDefault = false;
            indexName = index.Name;
        }
        else
        {
            // the basket setting (Nifty 500 by default), never the portfolio benchmark; a missing index stays missing, with the reason
            var configured = await basketBenchmark.GetAsync(cancellationToken);
            index = configured.Index;
            isDefault = configured.IsDefault;
            indexName = configured.Name;
            indexMissing = configured.Reason;
        }

        var today = IndiaTime.Today(time.GetUtcNow());
        var newest = today;
        var sessionsFrom = (DateOnly?)null;
        if (index is not null)
        {
            var wantSessions = from.Value is null && to.Value is null;
            var rows = await bars.ReadBarsAsync(index.Id, Timeframe.D1, wantSessions ? settings.DefaultWindowSessions : 1, cancellationToken);
            if (rows.Count > 0)
            {
                newest = IndiaTime.Today(rows[0].Ts);
                sessionsFrom = wantSessions ? IndiaTime.Today(rows[^1].Ts) : null;
            }
        }

        var end = to.Value ?? newest;
        var fromSource = from.Value is not null ? "query" : sessionsFrom is not null ? "sessions" : "calendar";
        var start = from.Value ?? sessionsFrom ?? end.AddDays(-365);
        var window = Check(start, end, await calendars.GetAsync(cancellationToken));
        if (window is not null)
        {
            return Result.Failure<ResolvedWindow>(window);
        }

        var toSource = to.Value is not null ? "query" : "newest_bar";
        var source = from.Value is not null && to.Value is not null ? "query" : "default";
        return Result.Success(new ResolvedWindow(start, end, newest, new BenchmarkWindowView(start, end, source, fromSource, toSource), index, isDefault, indexName, indexMissing));
    }

    private Error? Check(DateOnly from, DateOnly to, XDrishti.Domain.Market.Calendar.TradingCalendar calendar)
    {
        if (from >= to)
        {
            return ReportErrors.Invalid("from must be before to.");
        }

        if (to.DayNumber - from.DayNumber > limits.MaxWindowDays)
        {
            return ReportErrors.Invalid($"The window is longer than {limits.MaxWindowDays} days.");
        }

        var sessions = 0;
        for (var d = from; d <= to && sessions < MinSessions; d = d.AddDays(1))
        {
            sessions += calendar.IsTradingDay(d) ? 1 : 0;
        }

        return sessions < MinSessions ? ReportErrors.Invalid($"The window holds fewer than {MinSessions} sessions.") : null;
    }

    private static Result<DateOnly?> Parse(string? text, string name)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Result.Success<DateOnly?>(null);
        }

        return DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date.Year is >= 2000 and <= 2999
            ? Result.Success<DateOnly?>(date)
            : Result.Failure<DateOnly?>(ReportErrors.Invalid($"{name} must be a date such as 2026-10-11."));
    }
}
