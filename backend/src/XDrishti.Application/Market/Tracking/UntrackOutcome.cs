namespace XDrishti.Application.Market.Tracking;

/// <summary>What <see cref="InstrumentTracking.UntrackAsync"/> changed in the caller's unit of work (distinct instruments).</summary>
public sealed record UntrackOutcome(int Requested, int Untracked, int NotTracked);
