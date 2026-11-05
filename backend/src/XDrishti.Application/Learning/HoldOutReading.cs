using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>Where a model stands with the locked hold-out (CF-10 part 10c-2): <c>not_kept</c> (trained without one), <c>kept</c> (nothing looked yet at the current block), <c>used</c> (this model took the family's look at the current block) or <c>reused</c> (the look cannot count for it).</summary>
/// <param name="State">The state word.</param>
/// <param name="Facts">The facts for the predicate; null while nothing was scored.</param>
/// <param name="BlockStart">The start of the current locked block the look was taken on, or null before a look.</param>
internal sealed record HoldOutReading(string State, MlHoldOutFacts? Facts, DateOnly? BlockStart)
{
    /// <summary>The model took the family's fresh look at the current block.</summary>
    public const string Used = "used";
}
