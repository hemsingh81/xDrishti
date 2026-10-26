namespace XDrishti.Domain.Paper;

/// <summary>One counted paper trade as the backtest band sees it (spec P10-04): the cell it belongs to and its R, in the order of exit.</summary>
/// <param name="CellKey">The cell key <c>strategy|tf|side|style|exit|regime</c>.</param>
/// <param name="R">The trade's R after the stated basis.</param>
public sealed record PaperSlot(string CellKey, double R);
