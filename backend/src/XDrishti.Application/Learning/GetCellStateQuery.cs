using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Learning;

/// <summary>The state of a cell, replayed from its rows (P6-08).</summary>
/// <param name="Cell">The cell key.</param>
public sealed record GetCellStateQuery(string Cell) : IQuery<Result<CellStateView>>;
