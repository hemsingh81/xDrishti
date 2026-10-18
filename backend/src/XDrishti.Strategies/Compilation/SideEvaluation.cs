namespace XDrishti.Strategies.Compilation;

/// <summary>
/// The three truths of the sections at one closed bar: <paramref name="Setup"/> (true when the side has no setup list), <paramref name="Trigger"/> and <paramref name="Invalidate"/> (false when the side has no invalidate
/// list). <see cref="Fires"/> is the one definition of the entry condition: Kleene <c>setup and trigger and not invalidate</c> as a decision that treats unknown as false. A simulator uses it and does not combine the
/// truths again.
/// </summary>
public readonly record struct SideEvaluation(Truth Setup, Truth Trigger, Truth Invalidate)
{
    /// <summary>An entry is possible: setup is true, trigger is true and invalidate is false. Unknown is not true, so an unknown invalidation does not fire either.</summary>
    public bool Fires => Setup == Truth.True && Trigger == Truth.True && Invalidate == Truth.False;

    /// <summary>The invalidate list is true: the setup is cancelled (the latch "no entry for the rest of the day" is the simulator's). An unknown invalidation is not an invalidation.</summary>
    public bool Invalidated => Invalidate == Truth.True;
}
