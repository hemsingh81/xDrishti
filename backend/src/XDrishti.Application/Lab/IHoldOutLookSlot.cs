using XDrishti.Domain.Lab;

namespace XDrishti.Application.Lab;

/// <summary>
/// One kind of look at the block in force (strategy, model, system), as the seal step reads it for the auto release (HO-01 D4, D10): whether the kind's fresh slot is used, or free with or without an eligible subject. HO-01b registers the strategy slot, CF-10e part 3 the
/// model slot and SR-03 the system slot; until a kind is registered it holds no block. A read that cannot be answered returns <see cref="LookAvailability.Unavailable"/>, which keeps the block.
/// </summary>
internal interface IHoldOutLookSlot
{
    /// <summary>The kind: <c>strategy</c>, <c>model</c> or <c>system</c>.</summary>
    string Kind { get; }

    /// <summary>What the kind can still do at the block.</summary>
    ValueTask<LookAvailability> ReadAsync(DateOnly blockStart, CancellationToken cancellationToken);

    /// <summary>A sentence for the stall notice when this kind's look is held up by something other than a missing look (for example a claimed look that keeps failing), else null. Words only, no figure.</summary>
    ValueTask<string?> StallNoteAsync(DateOnly blockStart, CancellationToken cancellationToken) => ValueTask.FromResult<string?>(null);
}
