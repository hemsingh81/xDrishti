using XDrishti.Application.Abstractions.Lab;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>Where a model stands with the locked hold-out (CF-10 part 10c-2): <c>not_kept</c> (trained without one), <c>kept</c> (nothing looked yet at the current block), <c>used</c> (this model took the family's look at the current block) or <c>reused</c> (the look cannot count for it).</summary>
/// <param name="State">The state word.</param>
/// <param name="Facts">The facts for the predicate; null while nothing was scored.</param>
/// <param name="BlockStart">The start of the block the look was taken on (the model's newest fresh look across blocks when <c>used</c>, HO-01 D8), or null before a look.</param>
/// <param name="Look">The model's newest fresh look when <c>used</c>, with the stored result; null otherwise.</param>
internal sealed record HoldOutReading(string State, MlHoldOutFacts? Facts, DateOnly? BlockStart, StoredUnlock? Look = null)
{
    /// <summary>The model took the family's fresh look at the current block.</summary>
    public const string Used = "used";
}
