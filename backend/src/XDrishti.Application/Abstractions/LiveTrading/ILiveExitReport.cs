namespace XDrishti.Application.Abstractions.LiveTrading;

/// <summary>Whether the small-live exit report (the P11-06 phase check) is met right now, read from the stored records. An input that cannot be read or judged is not met, never a pass.</summary>
/// <param name="Met">True only when the report was read and says MET.</param>
/// <param name="Reason">In words.</param>
public sealed record LiveExitCheck(bool Met, string Reason);

/// <summary>The owner reads the P11-06 report; the system does not decide. Used only by <c>exit_confirmed</c>, which is the key to S3.</summary>
public interface ILiveExitReport
{
    ValueTask<LiveExitCheck> CheckAsync(CancellationToken cancellationToken);
}
