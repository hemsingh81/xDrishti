namespace XDrishti.Domain.Options.Costs;

/// <summary>Where a half-spread came from. <see cref="None"/> is the first member: an uninitialised value is unavailable, never a number.</summary>
public enum HalfSpreadSource
{
    None,
    Measured,
    Assumed,
}
