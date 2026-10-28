using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Market;

namespace XDrishti.Application.LiveTrading;

/// <summary>
/// The evaluator of the ramp and the halts (spec P11-04, ADR 0151). Per manual-live account with a step: (1) the daily halt (the loss of today against 2 R or the percentage limit) and the weekly halt (the week's loss against the weekly limit, the profile's
/// action) are opened once per period; (2) unless <c>HaltsOnly</c>, the breach rules write a <c>hold</c> or a <c>step_down</c> by <c>rule:ramp</c>, and, with <c>live.auto_ramp_up</c> ON, no hold, every gate passing and a step below 2, an <c>auto_step_up</c> with
/// the settings snapshot. A row is written only when the state changes, so a second run writes nothing. A measure that is not a value never opens a halt (it fails the live-ready gate instead). It reads stored data and writes insert-only rows; it calls no broker and
/// places, changes or cancels no order, and it does not move any ticket.
/// </summary>
internal sealed class EvaluateRampHandler(
    IAccountStore accounts,
    IRiskProfileStore profiles,
    IRampStore ramp,
    RampAssessor assessor,
    LiveLossReader loss,
    ILiveHaltStore halts,
    ILiveSettingsStore switches,
    ILiveRampSettings settings,
    ITradingCalendar calendars,
    TimeProvider time,
    ILogger<EvaluateRampHandler> logger) : ICommandHandler<EvaluateRampCommand, RampEvaluationOutcome>
{
    public async ValueTask<Result<RampEvaluationOutcome>> HandleAsync(EvaluateRampCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        int checkedCount = 0, holds = 0, downs = 0, ups = 0, opened = 0, failed = 0;
        var now = time.GetUtcNow();
        var today = IndiaTime.Today(now);
        var calendar = await calendars.GetAsync(cancellationToken);
        var live = (await accounts.ListActiveAsync(cancellationToken)).Where(a => a.Trading is { Enabled: true, Mode: TradingMode.ManualLive } && (command.AccountId is null || a.Id == command.AccountId)).ToList();
        var allProfiles = live.Count > 0 ? await profiles.ListAsync(cancellationToken) : [];
        foreach (var account in live)
        {
            try
            {
                var state = await ramp.CurrentAsync(account.Id, cancellationToken);
                if (state is null)
                {
                    continue;
                }

                checkedCount++;
                if (calendar.IsTradingDay(today))
                {
                    opened += await HaltsAsync(account, state, allProfiles.FirstOrDefault(p => p.Id == account.Trading.RiskProfileId), today, calendar, cancellationToken);
                }

                if (command.HaltsOnly)
                {
                    continue;
                }

                var (h, d, u) = await RampAsync(account, state, cancellationToken);
                holds += h;
                downs += d;
                ups += u;
            }
            catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
            {
                failed++;
                RampLog.EvaluationFailed(logger, failure.GetType().Name);
            }
        }

        var summary = string.Create(CultureInfo.InvariantCulture, $"{checkedCount} live accounts checked: {opened} halts opened, {holds} holds, {downs} step-downs, {ups} automatic step-ups");
        return Result.Success(new RampEvaluationOutcome(checkedCount, holds, downs, ups, opened, failed, summary));
    }

    private async ValueTask<(int Holds, int Downs, int Ups)> RampAsync(BrokerAccount account, RampState state, CancellationToken cancellationToken)
    {
        var assessment = await assessor.AssessAsync(account, cancellationToken);
        if (assessment is null)
        {
            return (0, 0, 0);
        }

        var down = assessment.Breaches.Where(b => b.Action == Breach.StepDown).ToList();
        if (down.Count > 0 && state.Step > RampSteps.First)
        {
            var stored = await ramp.AppendAsync(RampEvent.RuleStepDown(account.Id, state, Reason(down)), cancellationToken);
            if (stored is not null)
            {
                RampLog.StepDownWritten(logger, stored.Id, stored.FromStep ?? 0, stored.ToStep);
                return (0, 1, 0);
            }

            return (0, 0, 0);
        }

        if (assessment.Breaches.Count > 0)
        {
            // A step-down rule at S1 has nowhere lower to go: it holds instead.
            if (!state.Hold && await ramp.AppendAsync(RampEvent.RuleHold(account.Id, state, Reason(assessment.Breaches)), cancellationToken) is { } held)
            {
                RampLog.HoldWritten(logger, held.Id, held.ToStep);
                return (1, 0, 0);
            }

            return (0, 0, 0);
        }

        var current = await switches.CurrentAsync(cancellationToken);
        var up = RampEvent.RuleAutoStepUp(account.Id, state, assessment.Snapshot, current);
        if (up is not null && await ramp.AppendAsync(up, cancellationToken) is { } raised)
        {
            RampLog.AutoStepUpWritten(logger, raised.Id, raised.FromStep ?? 0, raised.ToStep);
            return (0, 0, 1);
        }

        return (0, 0, 0);
    }

    private async ValueTask<int> HaltsAsync(BrokerAccount account, RampState state, RiskProfile? profile, DateOnly today, XDrishti.Domain.Market.Calendar.TradingCalendar calendar, CancellationToken cancellationToken)
    {
        if (profile is null || RampPolicy.Scale(state, settings.StepFactors) is not { } scale)
        {
            return 0;
        }

        var intended = account.Trading.OverrideRiskPerTradePct ?? profile.RiskPerTradePct;
        var capital = account.Trading.CapitalAmount;
        var oneR = HaltRules.OneR(capital, (double)intended * scale);
        if (oneR is null)
        {
            return 0;
        }

        var opened = 0;
        var day = await loss.MeasureAsync(account.Id, today, cancellationToken);
        var dailyPct = account.Trading.OverrideDailyLossPct ?? profile.DailyLossPct;
        if (HaltRules.Daily(day.Loss, oneR, capital, profile.DailyLossR, dailyPct) is { } daily && await OpenAsync(account.Id, daily, today, profile.DailyLossR, dailyPct, cancellationToken))
        {
            opened++;
        }

        // The week: every completed session of it measured once more, unavailable days left out (the figure then reads realised_only).
        var monday = RampAssessor.WeekStart(today);
        decimal net = 0m;
        var state2 = MeasureStates.Value;
        for (var d = monday; d <= today; d = d.AddDays(1))
        {
            if (!calendar.IsTradingDay(d))
            {
                continue;
            }

            var m = d == today ? day : await loss.MeasureAsync(account.Id, d, cancellationToken);
            if (m.Loss.Net is { } n)
            {
                net += n;
                state2 = m.Loss.State == MeasureStates.RealisedOnly ? MeasureStates.RealisedOnly : state2;
            }
            else
            {
                state2 = MeasureStates.RealisedOnly;
            }
        }

        if (HaltRules.Weekly(state2, Math.Max(0m, -net), oneR, capital, profile.WeeklyLossR, profile.WeeklyLossAction) is { } weekly && await OpenAsync(account.Id, weekly, monday, profile.WeeklyLossR, 0m, cancellationToken))
        {
            opened++;
        }

        return opened;
    }

    private async ValueTask<bool> OpenAsync(Guid accountId, HaltDecision d, DateOnly period, decimal limitR, decimal limitPct, CancellationToken cancellationToken)
    {
        var stored = await halts.OpenAsync(new NewHalt(accountId, d.Kind, period, limitR, limitPct, d.LossR, d.LossPct, d.MeasureState, d.Action), cancellationToken);
        if (stored is null)
        {
            return false;
        }

        RampLog.HaltOpened(logger, stored.Id, stored.Kind, stored.Action, stored.MeasureState);
        return true;
    }

    private static string Reason(IReadOnlyList<Breach> breaches)
    {
        var text = "Breach rule: " + string.Join("; ", breaches.Select(b => $"{b.Code} {b.Reason}"));
        return text.Length <= RampEvent.MaxReason ? text : text[..RampEvent.MaxReason];
    }
}
