namespace XDrishti.Domain.Research;

/// <summary>How each charge line of a leg is rounded to the paisa (ADR 0042, D6). The one rule of the sheet that the owner's bills can settle without a code change.</summary>
public enum ChargeRounding : byte
{
    /// <summary>An exact half goes away from zero (1.275 is 1.28).</summary>
    HalfAwayFromZero = 0,

    /// <summary>An exact half goes to the even paisa (1.275 is 1.28, 5.005 is 5.00).</summary>
    HalfEven = 1,

    /// <summary>The amount is cut at the paisa (1.275 is 1.27).</summary>
    Down = 2,
}
