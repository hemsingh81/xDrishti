namespace XDrishti.Application.Abstractions.LiveTrading;

/// <summary>Whether the small-live exit report (P11-06) is met right now. The report is not built yet, so the answer is "not checked", which is never a pass (an input that cannot be read is not met).</summary>
/// <param name="Met">True only when the report was read and says MET.</param>
/// <param name="Reason">In words.</param>
public sealed record LiveExitCheck(bool Met, string Reason);

/// <summary>The owner reads the P11-06 report; the system does not decide. Used only by <c>exit_confirmed</c>, which is the key to S3.</summary>
public interface ILiveExitReport
{
    ValueTask<LiveExitCheck> CheckAsync(CancellationToken cancellationToken);
}
