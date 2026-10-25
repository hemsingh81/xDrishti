namespace XDrishti.Domain.Learning;

/// <summary>Tag counts of one cell (P6-15). A loss can carry several tags, so the counts add to more than <paramref name="Losses"/>.</summary>
/// <param name="CellKey">The cell.</param>
/// <param name="Trades">Closed trades of the cell.</param>
/// <param name="Losses">Of them, losses (R below 0).</param>
/// <param name="Counts">Losses with each tag, for every kind in the order of <see cref="LossTagKind"/> (zeros included).</param>
/// <param name="Untagged">Losses with no tag.</param>
/// <param name="Growing">Growing tags of the cell.</param>
/// <param name="LostR">Per tag, parallel to <paramref name="Counts"/>: the sum of the (negative) R of the losses carrying the tag. Tags overlap, so the sums add to more than the cell's total; null when the producer did not compute it.</param>
public sealed record CellLossTags(string CellKey, int Trades, int Losses, IReadOnlyList<int> Counts, int Untagged, IReadOnlyList<GrowingTag> Growing, IReadOnlyList<double>? LostR = null);
