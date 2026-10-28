using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.LiveTrading;

namespace XDrishti.Application.LiveTrading;

/// <summary>Reads the ramp of an account: its step, the scale, the risk it gives and the history (spec P11-04). Read only. The evidence gates are built in P11-04b: until then none is listed and the step is not eligible to rise.</summary>
internal sealed class GetRampHandler(IAccountStore accounts, IRiskProfileStore profiles, IRampStore ramp, ILiveRampSettings settings, ILiveWritesGate gate) : IQueryHandler<GetRampQuery, Result<RampView>>
{
    public const int HistoryLimit = 50;

    public async ValueTask<Result<RampView>> HandleAsync(GetRampQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var account = await accounts.FindReadOnlyAsync(query.AccountId, cancellationToken);
        if (account is null)
        {
            return Result.Failure<RampView>(Error.NotFound(LiveErrors.AccountNotFound, "The account does not exist."));
        }

        return Result.Success(await ViewAsync(account.Trading.RiskProfileId, account.Trading.OverrideRiskPerTradePct, query.AccountId, cancellationToken));
    }

    internal async ValueTask<RampView> ViewAsync(Guid? profileId, decimal? overridePct, Guid accountId, CancellationToken cancellationToken)
    {
        var state = await ramp.CurrentAsync(accountId, cancellationToken);
        var history = await ramp.HistoryAsync(accountId, HistoryLimit, cancellationToken);
        var profile = profileId is { } id ? (await profiles.ListAsync(cancellationToken)).FirstOrDefault(p => p.Id == id) : null;
        double? intended = overridePct is { } o ? (double)o : profile is null ? null : (double)profile.RiskPerTradePct;
        var scale = RampPolicy.Scale(state, settings.StepFactors);
        var holdReason = state is { Hold: true } ? history.FirstOrDefault(e => e.Kind == RampEventKinds.Hold)?.Reason : null;
        return new RampView(
            accountId,
            state?.Step,
            scale,
            state?.Hold ?? false,
            holdReason,
            state is { } s && s.Step < RampSteps.Third ? s.Step + 1 : null,
            Eligible: false,
            GatesBuilt: false,
            intended,
            intended is { } i && scale is { } sc ? i * sc : null,
            gate.Enabled,
            [.. history.Select(e => new RampEventView(e.Id, e.Kind, e.FromStep, e.ToStep, e.Actor, e.Reason, e.CreatedAt, e.GateHash))],
            LiveNotice.NoOrder);
    }
}
