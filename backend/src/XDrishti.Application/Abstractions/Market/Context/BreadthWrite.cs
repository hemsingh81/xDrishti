using XDrishti.Domain.Market.Context;

namespace XDrishti.Application.Abstractions.Market.Context;

/// <summary>A breadth to store on a context day; <paramref name="ReplaceInsufficient"/> lets it replace an earlier <c>insufficient</c> (a day inside the grace).</summary>
public sealed record BreadthWrite(DateOnly Date, BreadthResult Result, bool ReplaceInsufficient);
