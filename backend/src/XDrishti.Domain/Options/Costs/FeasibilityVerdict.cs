namespace XDrishti.Domain.Options.Costs;

/// <summary>The answers of the lot-feasibility count. <see cref="Unknown"/> is the first member: an uninitialised verdict is never sizable.</summary>
public enum FeasibilityVerdict
{
    Unknown,
    Sizable,
    NotSizable,
    LotUnknown,
    DeltaUnavailable,
    RiskInvalid,
}
