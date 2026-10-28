using XDrishti.Domain.LiveTrading;

namespace XDrishti.Application.Abstractions.LiveTrading;

/// <summary>The facts of the live-ready gate that live outside the account: the step, the two services, the paper phase check and the charge sheet. Reads only.</summary>
/// <param name="StepSet">The account has a ramp step.</param>
/// <param name="Capture">The <c>live-capture</c> service.</param>
/// <param name="Settle">The <c>live-settle</c> service.</param>
/// <param name="PaperPhaseMet">The P10 paper phase check is MET; null when it could not be read.</param>
/// <param name="ChargeSheetConfirmed">The charge sheet is confirmed; null when it could not be read.</param>
public sealed record LiveSourceFacts(bool StepSet, LiveServiceState Capture, LiveServiceState Settle, bool? PaperPhaseMet, bool? ChargeSheetConfirmed);

/// <summary>Where the live-ready gate reads what is not on the account.</summary>
public interface ILiveReadinessSource
{
    ValueTask<LiveSourceFacts> ReadAsync(Guid accountId, CancellationToken cancellationToken);
}
