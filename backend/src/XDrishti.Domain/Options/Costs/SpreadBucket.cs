namespace XDrishti.Domain.Options.Costs;

/// <summary>The three keys of a half-spread bucket (P14-07): moneyness in strike steps from the ATM, days to expiry and time of day, each as its stored word.</summary>
public sealed record SpreadBucket(string Moneyness, string Dte, string Tod);
