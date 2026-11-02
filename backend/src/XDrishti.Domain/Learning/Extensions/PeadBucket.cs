namespace XDrishti.Domain.Learning.Extensions;

/// <summary>Where the surprise of an event falls in its season's cross-section (P15-10, protocol P): the top quintile is compared with the middle three on the same dates.</summary>
public enum PeadBucket
{
    /// <summary>The top quintile.</summary>
    Top,

    /// <summary>The middle three quintiles.</summary>
    Middle,
}
