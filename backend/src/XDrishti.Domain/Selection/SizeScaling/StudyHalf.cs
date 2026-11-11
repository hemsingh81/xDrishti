namespace XDrishti.Domain.Selection.SizeScaling;

/// <summary>One half of the sizing study (a median split of the trades in decision order).</summary>
/// <param name="Trades">Trades in the half.</param>
/// <param name="Sufficient">True with at least 30 trades; otherwise the figures below are null.</param>
/// <param name="DrawdownSign">-1, 0 or 1: the sign of the change of the maximum drawdown, scaled against unscaled (negative is a smaller drawdown).</param>
/// <param name="TotalSign">-1, 0 or 1: the sign of the change of the total R, scaled against unscaled.</param>
public sealed record StudyHalf(int Trades, bool Sufficient, int? DrawdownSign, int? TotalSign);
