using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Learning;

/// <summary>The cell statistics of the newest snapshot (P6-01), optionally of one strategy and one window (<c>all</c>, <c>250d</c>, <c>60d</c>); <c>learning.invalid</c> for another window code.</summary>
public sealed record ListCellsQuery(string? Strategy, string? Window) : IQuery<Result<IReadOnlyList<CellStatsView>>>;
