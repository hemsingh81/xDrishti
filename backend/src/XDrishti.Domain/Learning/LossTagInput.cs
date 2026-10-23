using XDrishti.Domain.Research;

namespace XDrishti.Domain.Learning;

/// <summary>One closed trade given to the loss tagger with its cell and its result in the cell's unit (net of charges when the cell is costed, else from the filled prices).</summary>
/// <param name="CellKey">The key of the cell.</param>
/// <param name="Trade">The closed trade.</param>
/// <param name="R">Its result in R in the cell's unit; a loss is below 0.</param>
public sealed record LossTagInput(string CellKey, SimTrade Trade, double R);
