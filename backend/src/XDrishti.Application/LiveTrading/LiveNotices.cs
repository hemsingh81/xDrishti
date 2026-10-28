using System.Globalization;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Market;

namespace XDrishti.Application.LiveTrading;

/// <summary>
/// The notices of the live record, derived on read (spec P11-04): a halt in force, a hold, a capture gap, a step the evidence has earned, and the compliance re-check that is overdue. Words and a route only: nothing to run, nothing stored. Reads stored data and
/// calls no broker. A halt is advice: the owner places every order.
/// </summary>
internal sealed class LiveNotices(IAccountStore accounts, IRampStore ramp, ILiveHaltStore halts, RampAssessor assessor, IComplianceStore compliance, TimeProvider time) : IQueryHandler<GetLiveNoticesQuery, IReadOnlyList<LiveNoticeView>>
{
    public const string RampRoute = "/live?tab=ramp";

    public async ValueTask<IReadOnlyList<LiveNoticeView>> HandleAsync(GetLiveNoticesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var today = IndiaTime.Today(time.GetUtcNow());
        var notices = new List<LiveNoticeView>();
        var live = (await accounts.ListActiveAsync(cancellationToken)).Where(a => a.Trading is { Enabled: true, Mode: TradingMode.ManualLive }).ToList();
        var states = live.Count == 0 ? new Dictionary<Guid, RampState>() : (await ramp.CurrentAllAsync(cancellationToken)).ToDictionary(p => p.Key, p => p.Value);
        foreach (var account in live.Where(a => states.ContainsKey(a.Id)))
        {
            var state = states[account.Id];
            foreach (var h in (await halts.ListAsync(account.Id, RampAssessor.WeekStart(today), today, cancellationToken)).Where(h => h.Event == HaltCodes.Opened && (h.Kind == HaltCodes.Weekly || h.PeriodStart == today)))
            {
                var what = h.Action switch { HaltCodes.StopNew => "no new entries", HaltCodes.Halve => "size halves next week", _ => "alert only" };
                notices.Add(new($"live-halt:{h.Id}", "live_halt", "error", string.Create(CultureInfo.InvariantCulture, $"{account.Name}: {h.Kind} loss limit reached ({h.MeasuredR:0.00} R of {h.LimitR:0.##} R, {h.MeasureState.Replace('_', ' ')}): {what}. You place every order; this is advice."), RampRoute));
            }

            if (state.Hold)
            {
                notices.Add(new($"live-hold:{state.EventId}", "live_hold", "warning", $"{account.Name}: the ramp is on hold, raising is blocked until you clear it.", RampRoute));
            }

            var assessment = await assessor.AssessAsync(account, cancellationToken);
            if (assessment is null)
            {
                continue;
            }

            if (assessment.Evidence.GapLast5 > 0)
            {
                notices.Add(new($"live-gap:{account.Id}", "live_capture_gap", "warning", $"{account.Name}: the live capture had a gap in the last 5 sessions.", "/live?tab=capture"));
            }

            if (!state.Hold && state.Step < 2 && RampGates.Eligible(assessment.Gates))
            {
                notices.Add(new($"live-eligible:{state.EventId}", "live_step_eligible", "info", $"{account.Name}: every evidence gate passes, S{state.Step + 1} can be set.", RampRoute));
            }
        }

        notices.AddRange((await compliance.LatestPerKindAsync(cancellationToken)).Where(s => ComplianceRules.Overdue(s.Record, today)).Select(s => new LiveNoticeView($"compliance:overdue:{s.Record.Kind}", ComplianceRules.OverdueKind, "warning", $"Compliance re-check overdue: {ComplianceKinds.Label(s.Record.Kind)}.", ComplianceRules.Route)));
        return notices;
    }
}
