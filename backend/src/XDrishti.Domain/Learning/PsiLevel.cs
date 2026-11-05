namespace XDrishti.Domain.Learning;

/// <summary>How far a feature has moved from its training distribution (CF-10 part 10b): the standard cuts of the population stability index.</summary>
public enum PsiLevel
{
    /// <summary>Below the watch cut (0.10).</summary>
    Stable = 0,

    /// <summary>From the watch cut up to and including the alarm cut (0.10 to 0.25).</summary>
    Watch = 1,

    /// <summary>Above the alarm cut (0.25).</summary>
    Alarm = 2,
}
