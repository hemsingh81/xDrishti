using System.Collections.Immutable;

namespace XDrishti.Domain.Learning;

/// <summary>What a label does not carry: the date of the entry, the cell and the features of the decision.</summary>
/// <param name="TradeDate">India date of the entry.</param>
/// <param name="CellKey">The cell as text.</param>
/// <param name="Features">The features at the decision; empty when there are none.</param>
public sealed record ValidationContext(DateOnly TradeDate, string CellKey, ImmutableArray<double> Features);
