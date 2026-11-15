using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Paper;
using XDrishti.Application.Platform.Services;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.LiveTrading;

namespace XDrishti.Application.LiveTrading;

/// <summary>
/// The facts of the live-ready gate from outside the account (spec P11-04). Reads only. A service that is not registered (live-capture and live-settle until P11-01) is NotRegistered, which is not a pass; a phase check or cost buffer setting that cannot be read
/// is null, which is not a pass either.
/// </summary>
internal sealed class LiveReadinessSource(
    IRampStore ramp,
    ServiceSettingsReader services,
    IQueryHandler<EvaluatePaperPhaseCheckQuery, Result<PaperPhaseCheckView>> phase,
    CostBufferOptions costBuffer) : ILiveReadinessSource
{
    public const string CaptureId = "live-capture";
    public const string SettleId = "live-settle";

    public async ValueTask<LiveSourceFacts> ReadAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var step = await ramp.CurrentAsync(accountId, cancellationToken);
        var all = await services.ReadAllAsync(cancellationToken);
        return new LiveSourceFacts(step is not null, State(all, CaptureId), State(all, SettleId), await PhaseMetAsync(cancellationToken), CostBasisStated());
    }

    private static LiveServiceState State(IReadOnlyList<EffectiveService> all, string id) =>
        all.FirstOrDefault(s => s.Id == id) is { } s ? (s.Settings.Enabled ? LiveServiceState.Enabled : LiveServiceState.Paused) : LiveServiceState.NotRegistered;

    private async ValueTask<bool?> PhaseMetAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await phase.HandleAsync(new EvaluatePaperPhaseCheckQuery(null, null), cancellationToken);
            return result.IsSuccess ? string.Equals(result.Value.Report.ExitVerdict, "MET", StringComparison.Ordinal) : null;
        }
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
        {
            return null; // could not be read: never a pass
        }
    }

    /// <summary>GROSS-01: the cost basis is stated when the cost buffer setting is valid; an invalid one is not a pass.</summary>
    private bool? CostBasisStated() => costBuffer.IsValid;
}
