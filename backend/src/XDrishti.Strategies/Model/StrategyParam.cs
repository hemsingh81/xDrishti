namespace XDrishti.Strategies.Model;

/// <summary>A numeric parameter of a strategy (at most <see cref="StrategyDefinition.MaxParams"/>): a name <c>[a-z][a-z0-9_]*</c> of 1 to 32 characters and a finite value. The conditions read it by name (<c>rsi(rsi_len)</c>).</summary>
public sealed record StrategyParam(string Name, double Value);
