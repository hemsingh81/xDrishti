namespace XDrishti.Domain.Market.Context;

/// <summary>The two numbers of the derivation (<c>Data:Context</c>): how many sessions a day waits for its VIX leg before it goes without (D10), and the share of the calendar's expected minutes a leg needs to count (D9).</summary>
public sealed record ContextDerivationOptions(int VixGraceSessions = 5, double MinDayFraction = 0.96);
