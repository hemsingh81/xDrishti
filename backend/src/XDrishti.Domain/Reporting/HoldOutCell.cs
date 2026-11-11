namespace XDrishti.Domain.Reporting;

/// <summary>The stability of one strategy's expectancy in a basket across the two halves of its trades (BK-07 m23).</summary>
/// <param name="Strategy">The strategy id.</param>
/// <param name="SplitDate">The median exit date: trades that exited before it are the first half, the others the second; null when there are no trades.</param>
/// <param name="FirstN">Trades in the first half.</param>
/// <param name="SecondN">Trades in the second half.</param>
/// <param name="FirstExpectancy">Mean R of the first half, or why there is none.</param>
/// <param name="SecondExpectancy">Mean R of the second half, or why there is none.</param>
/// <param name="Verdict"><see cref="HoldOutStability.Agrees"/>, <see cref="HoldOutStability.Disagrees"/>, <see cref="HoldOutStability.Insufficient"/> or <see cref="HoldOutStability.NotEvaluable"/>.</param>
public sealed record HoldOutCell(string Strategy, DateOnly? SplitDate, int FirstN, int SecondN, Metric FirstExpectancy, Metric SecondExpectancy, string Verdict);
