namespace XDrishti.Application.Market.Tracking;

/// <summary>What <see cref="InstrumentTracking.TrackAsync"/> changed in the caller's unit of work (distinct instruments).</summary>
public sealed record TrackingOutcome(int Requested, int Added, int Reactivated, int AlreadyTracked)
{
    public int Changed => Added + Reactivated;
}
