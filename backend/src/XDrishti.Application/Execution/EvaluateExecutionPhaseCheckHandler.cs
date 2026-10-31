using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Common;
using XDrishti.Domain.Execution.PhaseCheck;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Execution;

/// <summary><c>through</c> is the last session to judge (null: the newest whose final reconciliation is due); <c>AccountId</c> narrows the report to one account.</summary>
public sealed record EvaluateExecutionPhaseCheckQuery(DateOnly? Through, Guid? AccountId) : IQuery<Result<ExecutionPhaseCheckView>>;

public sealed record ExecutionPhaseCheckView(ExecutionPhaseReport Report, string Text);

/// <summary>
/// The P12 phase check (spec P12-12, ADR 0169): reads the stored records of each Trading account through <see cref="IExecutionRecordsReader"/> and hands plain facts to the pure <see cref="ExecutionPhaseCheck"/>. Nothing is written, no test is run, no setting is
/// changed and no broker is called; a read that fails is an error of the call, never a quiet MET. Time comes from the <see cref="TimeProvider"/>.
/// </summary>
internal sealed class EvaluateExecutionPhaseCheckHandler(
    IExecutionRecordsReader reader,
    ITradingCalendar tradingCalendar,
    IBrokerAllowList allowList,
    TimeProvider clock,
    ILogger<EvaluateExecutionPhaseCheckHandler> logger) : IQueryHandler<EvaluateExecutionPhaseCheckQuery, Result<ExecutionPhaseCheckView>>
{
    internal const int MaxSessions = 200;

    /// <summary>The final reconciliation runs at 16:30 India time; a session is judged from ten minutes after.</summary>
    internal static readonly TimeSpan FinalDue = new(16, 40, 0);

    private static readonly TimeSpan OpenAt = new(9, 0, 0);
    private static readonly TimeSpan CloseAt = new(16, 30, 0);

    public async ValueTask<Result<ExecutionPhaseCheckView>> HandleAsync(EvaluateExecutionPhaseCheckQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var now = clock.GetUtcNow();
        var calendar = await tradingCalendar.GetAsync(cancellationToken);
        var lastClosed = calendar.LastClosedSession(now);
        var newest = await reader.NewestSessionAsync(cancellationToken);
        var latest = newest is { } n && n > lastClosed ? n : lastClosed;

        DateOnly through;
        if (query.Through is { } asked)
        {
            if (asked > latest)
            {
                return Refuse($"The newest recorded session is {latest:yyyy-MM-dd}; a later date has nothing to read.");
            }

            if (OnOrBefore(calendar, asked) is not { } found)
            {
                return Refuse("No trading session lies on or before this date.");
            }

            through = found;
        }
        else
        {
            through = DefaultThrough(calendar, lastClosed, now);
        }

        var accounts = await reader.AccountsAsync(query.AccountId, cancellationToken);
        if (query.AccountId is not null && accounts.Count == 0)
        {
            return Refuse("No Trading account has this id.");
        }

        var settings = await reader.SettingsAsync(cancellationToken);
        var built = new List<AccountPhaseFacts>();
        foreach (var account in accounts)
        {
            built.Add(await AccountAsync(account, calendar, settings, through, cancellationToken));
        }

        var boundary = new BoundaryFacts(allowList.Entries(), PlanPhaseCheck.PinnedAllowList, [.. PlanPhaseCheck.RefusedProbes.Where(p => allowList.Allows(p.Method, p.Url)).Select(p => $"{p.Method} {p.Url}")]);
        var report = ExecutionPhaseCheck.Evaluate(new ExecutionPhaseFacts(through, built, boundary));
        var met = report.Accounts.Count(a => a.Verdict == PhaseVerdicts.Met);
        var notMet = report.Accounts.Count(a => a.Verdict == PhaseVerdicts.NotMet);
        ExecutionPhaseCheckLog.Evaluated(logger, through, report.ExitVerdict, report.Accounts.Count, met, notMet);
        return Result.Success(new ExecutionPhaseCheckView(report, ExecutionPhaseText.Render(report)));
    }

    private Result<ExecutionPhaseCheckView> Refuse(string message)
    {
        ExecutionPhaseCheckLog.Refused(logger, ExecutionScreenErrors.Invalid);
        return Result.Failure<ExecutionPhaseCheckView>(ExecutionScreens.Invalid(message));
    }

    private static DateOnly DefaultThrough(TradingCalendar calendar, DateOnly lastClosed, DateTimeOffset now)
    {
        var ist = IndiaTime.ToIst(now);
        if (IndiaTime.Today(now) == lastClosed && ist.TimeOfDay < FinalDue)
        {
            return Before(calendar, lastClosed) ?? lastClosed;
        }

        return lastClosed;
    }

    private static DateOnly? Before(TradingCalendar calendar, DateOnly date) => OnOrBefore(calendar, date.AddDays(-1));

    private static DateOnly? OnOrBefore(TradingCalendar calendar, DateOnly date)
    {
        for (var i = 0; i < 30; i++)
        {
            var d = date.AddDays(-i);
            if (calendar.IsTradingDay(d))
            {
                return d;
            }
        }

        return null;
    }

    private static DateTimeOffset At(DateOnly date, TimeSpan time) => new(date.ToDateTime(TimeOnly.FromTimeSpan(time)), IndiaTime.Offset);

    private static string ModeAt(IReadOnlyList<PhaseTrailRow> trail, DateTimeOffset instant)
    {
        if (trail.Count == 0)
        {
            return "unknown";
        }

        var row = trail.LastOrDefault(t => t.EffectiveFrom <= instant);
        return row?.ToMode ?? trail[0].FromMode;
    }

    private async ValueTask<AccountPhaseFacts> AccountAsync(PhaseAccount account, TradingCalendar calendar, IReadOnlyList<PhaseSettingsRow> settings, DateOnly through, CancellationToken cancellationToken)
    {
        var trail = await reader.TrailAsync(account.Id, cancellationToken);
        var mode = ModeAt(trail, At(through, CloseAt));
        var promotion = trail.LastOrDefault(t => t.ToMode == ExecutionPhaseCheck.SemiAuto);
        if (promotion is null)
        {
            return new AccountPhaseFacts(account.Id, account.Name, mode, null, [], false, [], AccountExtras.Unread);
        }

        var start = DateOnly.FromDateTime(IndiaTime.ToIst(promotion.EffectiveFrom).DateTime);
        var days = new List<DateOnly>();
        for (var d = start; d <= through; d = d.AddDays(1))
        {
            if (calendar.IsTradingDay(d))
            {
                days.Add(d);
            }
        }

        var truncated = days.Count > MaxSessions;
        if (truncated)
        {
            days = days[^MaxSessions..];
        }

        if (days.Count == 0)
        {
            return new AccountPhaseFacts(account.Id, account.Name, mode, start, [], false, [], AccountExtras.Unread);
        }

        var records = await reader.RecordsAsync(account.Id, days[0], through, cancellationToken);
        var sessions = new List<SessionFacts>(days.Count);
        foreach (var day in days)
        {
            sessions.Add(Session(day, trail, settings, records));
        }

        var demotions = trail.Where(t => t.Kind is "demotion" or "owner_demotion").Select(t => DateOnly.FromDateTime(IndiaTime.ToIst(t.EffectiveFrom).DateTime)).Where(d => d >= days[0] && d <= through).ToList();
        var lastDrill = records.Drills.OrderBy(d => d.Date).Cast<PhaseDrillRow?>().LastOrDefault();
        var extras = new AccountExtras(
            records.LiveLegs, records.LiveLegsWithAlgoId, records.LiveEntryFills,
            records.Breaks.Count(b => b.Kind is "unknown_xd_order" or "foreign_conflict"),
            records.Drills.Count, lastDrill?.Date, lastDrill?.Passed,
            sessions.Count(s => s.DayState == "not_ready"), null, null);
        return new AccountPhaseFacts(account.Id, account.Name, mode, start, sessions, truncated, demotions, extras);
    }

    private static SessionFacts Session(DateOnly day, IReadOnlyList<PhaseTrailRow> trail, IReadOnlyList<PhaseSettingsRow> settings, PhaseAccountRecords r)
    {
        var open = At(day, OpenAt);
        var close = At(day, CloseAt);
        var changed = trail.Where(t => t.EffectiveFrom > open && t.EffectiveFrom <= close).ToList();
        var canary = settings.LastOrDefault(s => s.EffectiveFrom <= open)?.CanaryPending ?? true;
        var entries = r.Entries.Where(e => e.SessionDate == day).ToList();
        var live = entries.Where(e => e.Environment == "live").ToList();
        var tickets = r.Tickets.Where(t => t.SessionDate == day).ToList();
        var runs = r.Runs.Where(x => x.SessionDate == day).ToList();
        return new SessionFacts(
            day,
            ModeAt(trail, open) == ExecutionPhaseCheck.SemiAuto,
            changed.Count > 0,
            changed.Count(t => t.Kind is "demotion" or "owner_demotion"),
            canary,
            live.Count,
            live.Count(e => !e.Confirmed),
            r.SandboxDates.Count(d => d == day),
            r.Carried.Any(c => c.SessionDate < day && (c.ClosedOn is null || c.ClosedOn >= day)),
            [.. r.Breaks.Where(b => b.SessionDate == day).Select(b => b.Kind)],
            Run(runs, "eod"),
            Run(runs, "final"),
            [.. r.Alarms.Where(a => a.SessionDate == day).Select(a => a.Code)],
            [.. r.Halts.Where(h => h.SessionDate == day).Select(h => h.Code)],
            r.DayStates.FirstOrDefault(s => s.SessionDate == day)?.State,
            Tally(tickets));
    }

    private static RunFact? Run(List<PhaseRunRow> runs, string kind)
    {
        var rows = runs.Where(x => x.Kind == kind).ToList();
        return rows.Count == 0 ? null : new RunFact(rows.All(x => x.Readable));
    }

    private static TicketTally Tally(List<PhaseTicketRow> tickets)
    {
        if (tickets.Count == 0)
        {
            return TicketTally.None;
        }

        var taps = tickets.Where(t => t.Outcome == "confirmed" && t.DecidedAt is not null).Select(t => (t.DecidedAt.GetValueOrDefault() - t.IssuedAt).TotalMinutes).Order().ToList();
        double? median = tickets.Count < TicketTally.MinForMedian || taps.Count == 0 ? null : taps.Count % 2 == 1 ? taps[taps.Count / 2] : (taps[(taps.Count / 2) - 1] + taps[taps.Count / 2]) / 2;
        return new TicketTally(
            tickets.Count, tickets.Count(t => t.Outcome == "confirmed"), tickets.Count(t => t.Outcome == "skipped"), tickets.Count(t => t.Outcome == "expired"), tickets.Count(t => t.Outcome == "refused"), median);
    }
}
