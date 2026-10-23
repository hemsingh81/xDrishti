namespace XDrishti.Domain.Learning;

/// <summary>A loss with its tags; no tag is the honest outcome <c>untagged</c>.</summary>
/// <param name="Key">The trade key.</param>
/// <param name="CellKey">The cell.</param>
/// <param name="R">The result in R in the cell's unit.</param>
/// <param name="ExitEpochSeconds">The exit minute, used to order the losses.</param>
/// <param name="Tags">The tags in the fixed order of <see cref="LossTagKind"/>; empty when untagged.</param>
public sealed record TaggedLoss(string Key, string CellKey, double R, long ExitEpochSeconds, IReadOnlyList<LossTag> Tags);
