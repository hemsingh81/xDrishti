namespace XDrishti.Application.Learning;

/// <summary>What an extraction did.</summary>
/// <param name="Trades">The closed trades of the run.</param>
/// <param name="Stored">Rows newly written.</param>
/// <param name="NoBars">Trades left without features because their instrument has no stored bars.</param>
public sealed record TradeFeaturesOutcome(int Trades, int Stored, int NoBars);
