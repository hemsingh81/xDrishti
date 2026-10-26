namespace XDrishti.Domain.Market.Context;

/// <summary>The answer to "what was the market context on this date": the <see cref="Figures"/>, or <see cref="Unavailable"/> with the reason (<see cref="NoContext"/>, <see cref="BeforeHistory"/>, <see cref="Stale"/>). Never both.</summary>
public sealed record ContextRead(ContextFigures? Figures, string? Unavailable)
{
    /// <summary>No day is stored.</summary>
    public const string NoContext = "no_context";

    /// <summary>The date is on or before the first stored day.</summary>
    public const string BeforeHistory = "before_history";

    /// <summary>The latest stored day before the date is older than the age bound.</summary>
    public const string Stale = "stale";

    /// <summary>The stored days could not be built into a context (a row the library refuses).</summary>
    public const string Invalid = "context_invalid";

    public static ContextRead Of(ContextFigures figures) => new(figures, null);

    public static ContextRead Not(string reason) => new(null, reason);
}
