using XDrishti.Domain.Research;

namespace XDrishti.Simulation.Walking;

/// <summary>What the loops need to know about the entered position; a stack value, nothing per minute.</summary>
internal readonly record struct Position(
    EntryCandidate Candidate,
    long FillEpoch,
    bool IsBuy,
    decimal Entry,
    decimal EntryFilled,
    EntryFill EntryFill,
    decimal Stop,
    decimal? Target,
    decimal Risk,
    Slip Slip,
    bool AfterMissingMinutes,
    bool RiskUnderFourTicks,
    FillBasis Basis,
    bool TickFallback,
    bool OvernightEntry = false);
