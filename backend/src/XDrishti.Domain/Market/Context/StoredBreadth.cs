namespace XDrishti.Domain.Market.Context;

/// <summary>The breadth fields of one stored context day; <paramref name="State"/> is null while the day has none.</summary>
public sealed record StoredBreadth(DateOnly Date, double? Value, int? Voters, int? Universe, int? Excluded, string? State);
