using System.Globalization;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Quality;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Market.Bars.Eod;
using XDrishti.Application.Market.Quality;
using XDrishti.Application.Portfolio.BrokerHistory;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Planning;

/// <summary>What every gate is told (spec P7-08): the session that closed, the session planned for, the calendar and the instant of the run.</summary>
/// <param name="Session">The session whose close the plan rests on.</param>
/// <param name="ForDate">The session the plan is for.</param>
/// <param name="Calendar">The trading calendar, read once for the whole run.</param>
/// <param name="Now">The instant of the run.</param>
internal sealed record GateContext(DateOnly Session, DateOnly ForDate, TradingCalendar Calendar, DateTimeOffset Now);

/// <summary>One gate of the nightly pipeline (spec P7-08). It reads and decides; it writes nothing. A gate that cannot read its input says <c>unavailable</c>, never pass.</summary>
internal interface INightlyGate
{
    string Id { get; }

    ValueTask<GateResult> CheckAsync(GateContext context, CancellationToken cancellationToken);
}

/// <summary>The pure decisions of the gates (spec P7-08): inputs in, a reading out. No clock, no I/O.</summary>
internal static class NightlyChecks
{
    public static GateResult Calendar(GateContext c)
    {
        const string gate = NightlyGateIds.Calendar;
        if (c.Calendar.SessionOf(c.Session) is not { } session)
        {
            return GateResult.Failed(gate, Say($"{c.Session:yyyy-MM-dd} is not a trading day in the calendar."));
        }

        if (c.Now < NightlyRules.SessionEnd(session))
        {
            return GateResult.Failed(gate, Say($"The session of {c.Session:yyyy-MM-dd} has not closed yet."));
        }

        if (NightlyRules.NextSession(c.Calendar, c.Session) is not { } next)
        {
            return GateResult.NotAvailable(gate, Say($"The calendar shows no trading day within {NightlyRules.NextSessionSearchDays} days after {c.Session:yyyy-MM-dd}."));
        }

        if (next != c.ForDate)
        {
            return GateResult.Failed(gate, Say($"The next session after {c.Session:yyyy-MM-dd} is {next:yyyy-MM-dd}, not {c.ForDate:yyyy-MM-dd}."));
        }

        return NightlyRules.ReviewCutoff(next) <= c.Now
            ? GateResult.Failed(gate, Say($"The review cutoff of {next:yyyy-MM-dd} has passed; a plan for it can no longer be reviewed."))
            : GateResult.Passed(gate, Say($"{c.Session:yyyy-MM-dd} closed; the next session is {next:yyyy-MM-dd}."));
    }

    /// <summary>A succeeded run (or, where given, a skipped one with exactly that message) that began after the session closed and before the day of the planned session.</summary>
    public static GateResult ServiceRan(string gate, string label, IReadOnlyList<ServiceRun> runs, GateContext c, string? acceptedSkip)
    {
        if (c.Calendar.SessionOf(c.Session) is not { } session)
        {
            return GateResult.NotAvailable(gate, "The session is not in the calendar.");
        }

        var from = NightlyRules.SessionEnd(session);
        var before = new DateTimeOffset(c.ForDate.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset);
        var ok = runs.Any(r => r.StartedAt >= from && r.StartedAt < before
            && (r.Status == ServiceRunStatus.Succeeded || (acceptedSkip is not null && r.Status == ServiceRunStatus.Skipped && r.Message == acceptedSkip)));
        return ok
            ? GateResult.Passed(gate, Say($"{label} ran after the close of {c.Session:yyyy-MM-dd}."))
            : GateResult.Failed(gate, Say($"{label} has no successful run after the close of {c.Session:yyyy-MM-dd}."));
    }

    public static GateResult Fresh(BarTotals totals, GateContext c)
    {
        const string gate = NightlyGateIds.FreshData;
        var bad = (long)totals.InstrumentsBehind + totals.InstrumentsEmpty;
        var total = Math.Max(totals.Instruments, bad);
        if (totals.LastBar is not { } last || IndiaTime.Today(last) < c.Session)
        {
            return GateResult.Failed(gate, Say($"The newest stored bar is {(totals.LastBar is { } l ? IndiaTime.Today(l).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "none")}, before {c.Session:yyyy-MM-dd}."));
        }

        return NightlyRules.TooManyBad(bad, total)
            ? GateResult.Failed(gate, Say($"{bad} of {total} instruments have no bars for {c.Session:yyyy-MM-dd} (the limit is {NightlyRules.MaxBadShare:P0})."))
            : GateResult.Passed(gate, Say($"{total - bad} of {total} instruments have bars for {c.Session:yyyy-MM-dd}."));
    }

    public static GateResult Quality(QualityRunView? run, GateContext c)
    {
        const string gate = NightlyGateIds.Quality;
        if (run is null)
        {
            return GateResult.NotAvailable(gate, "No finished full data-quality run is stored.");
        }

        if (c.Calendar.SessionOf(c.Session) is not { } session || run.FinishedAt is not { } finished || finished < NightlyRules.SessionEnd(session))
        {
            return GateResult.Failed(gate, Say($"The newest full data-quality run did not finish after the close of {c.Session:yyyy-MM-dd}."));
        }

        if (run.Status != "succeeded")
        {
            return GateResult.Failed(gate, Say($"The newest full data-quality run ended {run.Status}."));
        }

        return NightlyRules.TooManyBad(run.Failed, run.Instruments)
            ? GateResult.Failed(gate, Say($"{run.Failed} of {run.Instruments} instruments failed the data-quality checks (the limit is {NightlyRules.MaxBadShare:P0})."))
            : GateResult.Passed(gate, Say($"{run.Failed} of {run.Instruments} instruments failed the data-quality checks."));
    }

    public static GateResult Selection(DateOnly? newestAsOf, GateContext c)
    {
        const string gate = NightlyGateIds.SelectionInputs;
        if (newestAsOf is not { } asOf)
        {
            return GateResult.NotAvailable(gate, "No cell statistics snapshot is stored.");
        }

        if (asOf > c.Session)
        {
            return GateResult.Failed(gate, Say($"The newest cell statistics are as of {asOf:yyyy-MM-dd}, after the session {c.Session:yyyy-MM-dd} (look-ahead)."));
        }

        var age = c.Calendar.TradingDaysBetween(asOf, c.Session);
        return age > NightlyRules.MaxCellStatsAgeSessions
            ? GateResult.Failed(gate, Say($"The newest cell statistics are as of {asOf:yyyy-MM-dd}, {age} sessions old (the limit is {NightlyRules.MaxCellStatsAgeSessions})."))
            : GateResult.Passed(gate, Say($"Cell statistics as of {asOf:yyyy-MM-dd}."));
    }

    private static string Say(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}

/// <summary>The trading calendar says the session closed and the next session exists (spec P7-08).</summary>
internal sealed class CalendarGate : INightlyGate
{
    public string Id => NightlyGateIds.Calendar;

    public ValueTask<GateResult> CheckAsync(GateContext context, CancellationToken cancellationToken) => ValueTask.FromResult(NightlyChecks.Calendar(context));
}

/// <summary>The <c>eod</c> service succeeded for the session (spec P7-08).</summary>
internal sealed class EodGate(IServiceRunStore runs) : INightlyGate
{
    public string Id => NightlyGateIds.Eod;

    public async ValueTask<GateResult> CheckAsync(GateContext context, CancellationToken cancellationToken) =>
        NightlyChecks.ServiceRan(Id, "The end-of-day data run", await runs.ListAsync(EodService.Id, 0, 20, cancellationToken), context, null);
}

/// <summary>The Portfolio trades and ledger sync succeeded for the session; with no Portfolio account it has nothing to do and says so (spec P7-08).</summary>
internal sealed class PortfolioSyncGate(IServiceRunStore runs) : INightlyGate
{
    public string Id => NightlyGateIds.PortfolioSync;

    public async ValueTask<GateResult> CheckAsync(GateContext context, CancellationToken cancellationToken) =>
        NightlyChecks.ServiceRan(Id, "The portfolio sync", await runs.ListAsync(PortfolioBrokerHistoryService.Id, 0, 20, cancellationToken), context, PortfolioBrokerHistoryService.NoAccountsMessage);
}

/// <summary>The stored bars reach the session for all but a small share of the instruments (spec P7-08).</summary>
internal sealed class FreshDataGate(IBarQueries bars) : INightlyGate
{
    public string Id => NightlyGateIds.FreshData;

    public async ValueTask<GateResult> CheckAsync(GateContext context, CancellationToken cancellationToken) =>
        NightlyChecks.Fresh(await bars.GetBarTotalsAsync(new DateTimeOffset(context.Session.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset), cancellationToken), context);
}

/// <summary>The newest full data-quality run finished after the close and few instruments failed it (spec P7-08).</summary>
internal sealed class QualityGate(IQualityQueries quality) : INightlyGate
{
    public string Id => NightlyGateIds.Quality;

    public async ValueTask<GateResult> CheckAsync(GateContext context, CancellationToken cancellationToken) =>
        NightlyChecks.Quality(await quality.GetLastFinishedFullRunAsync(cancellationToken), context);
}

/// <summary>A cell statistics snapshot exists, is not from after the session and is not too old (spec P7-08).</summary>
internal sealed class SelectionInputsGate(ICellStatsStore cells) : INightlyGate
{
    public string Id => NightlyGateIds.SelectionInputs;

    public async ValueTask<GateResult> CheckAsync(GateContext context, CancellationToken cancellationToken)
    {
        var rows = await cells.ListLatestAsync(null, null, cancellationToken);
        return NightlyChecks.Selection(rows.Count == 0 ? null : rows.Max(r => r.Row.AsOf), context);
    }
}
