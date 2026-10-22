using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Learning;

/// <summary>The three windows of one cell in the newest snapshot that holds it (P6-01); <c>learning.cell_not_found</c> when none does.</summary>
public sealed record GetCellQuery(string CellKey) : IQuery<Result<IReadOnlyList<CellStatsView>>>;
