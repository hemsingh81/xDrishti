using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Market;

namespace XDrishti.Application.LiveTrading;

/// <summary>
/// Reads the ramp of an account: its step, the scale, the risk it gives, the evidence gates, the breach rules, today's loss, the halts and the history (spec P11-04). Read only; nothing is written and no broker is called. A gate that cannot be read is shown
/// as such and never as a pass.
/// </summary>
internal sealed class GetRampHandler(
    IAccountStore accounts,
    IRiskProfileStore profiles,
    IRampStore ramp,
    ILiveRampSettings settings,
    ILiveWritesGate gate,
    RampAssessor assessor,
    LiveLossReader loss,
    ILiveHaltStore halts,
    ILiveSettingsStore switches,
    TimeProvider time) : IQueryHandler<GetRampQuery, Result<RampView>>
{
    public const int HistoryLimit = 50;
    public const int HaltDays = 30;

    public async ValueTask<Result<RampView>> HandleAsync(GetRampQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var account = await accounts.FindReadOnlyAsync(query.AccountId, cancellationToken);
        return account is null
            ? Result.Failure<RampView>(Error.NotFound(LiveErrors.AccountNotFound, "The account does not exist."))
            : Result.Success(await ViewAsync(account, cancellationToken));
    }

    internal async ValueTask<RampView> ViewAsync(BrokerAccount account, CancellationToken cancellationToken)
    {
        var accountId = account.Id;
        var state = await ramp.CurrentAsync(accountId, cancellationToken);
        var history = await ramp.HistoryAsync(accountId, HistoryLimit, cancellationToken);
        var profile = account.Trading.RiskProfileId is { } id ? (await profiles.ListAsync(cancellationToken)).FirstOrDefault(p => p.Id == id) : null;
        double? intended = account.Trading.OverrideRiskPerTradePct is { } o ? (double)o : profile is null ? null : (double)profile.RiskPerTradePct;
        var scale = RampPolicy.Scale(state, settings.StepFactors);
        var holdReason = state is { Hold: true } ? history.FirstOrDefault(e => e.Kind == RampEventKinds.Hold)?.Reason : null;
        var assessment = state is null ? null : await assessor.AssessAsync(account, cancellationToken);
        var today = IndiaTime.Today(time.GetUtcNow());
        var haltRows = await halts.ListAsync(accountId, today.AddDays(-HaltDays), today, cancellationToken);
        var liveRisk = intended is { } i && scale is { } sc ? i * sc : (double?)null;
        return new RampView(
            accountId,
            state?.Step,
            scale,
            state?.Hold ?? false,
            holdReason,
            state is { } s && s.Step < RampSteps.Third ? s.Step + 1 : null,
            assessment is not null && RampGates.Eligible(assessment.Gates),
            GatesBuilt: true,
            intended,
            liveRisk,
            gate.Enabled,
            [.. history.Select(e => new RampEventView(e.Id, e.Kind, e.FromStep, e.ToStep, e.Actor, e.Reason, e.CreatedAt, e.GateHash))],
            LiveNotice.NoOrder,
            [.. (assessment?.Gates ?? []).Select(g => new RampGateView(g.Code, g.State, g.Number, g.Threshold, g.N, g.Reason))],
            [.. (assessment?.Breaches ?? []).Select(b => new BreachView(b.Code, b.Action, b.Reason))],
            [.. (assessment?.HoldConditions ?? []).Select(b => new BreachView(b.Code, b.Action, b.Reason))],
            state is null ? null : await LossAsync(account, profile, liveRisk, today, cancellationToken),
            [.. haltRows.Where(h => h.Event == HaltCodes.Opened).Select(HaltView)],
            assessment?.Evidence.SlippageKind,
            (await switches.CurrentAsync(cancellationToken)).AutoRampUp,
            state?.ExitConfirmed ?? false,
            assessment?.Evidence.Sessions ?? 0);
    }

    internal static LiveHaltView HaltView(StoredHalt h) =>
        new(h.Id, h.AccountId, h.Kind, h.PeriodStart, (double)h.LimitR, (double)h.LimitPct, h.MeasuredR, h.MeasuredPct, h.MeasureState, h.Action, h.CreatedAt);

    private async ValueTask<LiveLossView?> LossAsync(BrokerAccount account, RiskProfile? profile, double? liveRiskPct, DateOnly today, CancellationToken cancellationToken)
    {
        const string note = "Counts the fills of the session opened and closed (or marked) today, gross of charges; positions carried in from earlier days and discretionary-only history are not in it.";
        var oneR = HaltRules.OneR(account.Trading.CapitalAmount, liveRiskPct);
        var measure = await loss.MeasureAsync(account.Id, today, cancellationToken);
        var net = measure.Loss.Net;
        double? lossR = measure.Loss.Loss is { } l && oneR is { } r ? (double)(l / r) : null;
        return new LiveLossView(today, measure.Health, measure.Loss.State, net is { } n ? (double)n : null, lossR, profile is null ? null : (double)profile.DailyLossR, oneR is { } one ? (double)one : null, measure.Loss.CarriedQty, measure.Loss.OpenUnmarked, note);
    }
}
