namespace XDrishti.Domain.Learning;

/// <summary>How one term of the influence predicate reads (CF-10 part 10c-2). Only <see cref="Holds"/> counts as true: an unavailable figure is not a pass.</summary>
public enum MlTermState
{
    /// <summary>A figure the term needs could not be had (never counts as a pass). The default value, so an unset state fails closed.</summary>
    Unavailable = 0,

    /// <summary>Every figure the term needs is there and meets its bar.</summary>
    Holds = 1,

    /// <summary>A figure is there and misses its bar, or the hold-out was spent.</summary>
    Fails = 2,
}
