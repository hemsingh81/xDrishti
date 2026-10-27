namespace XDrishti.Domain.Assistant.Drafting;

/// <summary>Something in the idea the strategy language cannot say today, with the reason; it keeps a number or time of the idea from being dropped silently.</summary>
public sealed record DraftNotExpressed(string What, string Why);
