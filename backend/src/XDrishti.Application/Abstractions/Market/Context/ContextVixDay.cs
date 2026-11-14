namespace XDrishti.Application.Abstractions.Market.Context;

/// <summary>One INDIA VIX close read from a dropped CSV.</summary>
public sealed record ContextVixDay(DateOnly Date, double Close);
