namespace XDrishti.Domain.Learning;

/// <summary>One example of a training set (P6-04): the features known at the decision, and the label with what only the finished trade can tell. <see cref="Values"/> is the whole catalog in order, features only; nothing after the decision is in it.</summary>
/// <param name="TradeKey">The trade's key.</param>
/// <param name="Strategy">The strategy id.</param>
/// <param name="Timeframe">The evaluation timeframe code.</param>
/// <param name="Side">BUY or SELL.</param>
/// <param name="TradeDate">India date of the entry.</param>
/// <param name="DecisionEpochSeconds">The decision instant.</param>
/// <param name="Values">The catalog values in order (NaN where unknown and not required).</param>
/// <param name="Label">The triple-barrier label.</param>
/// <param name="R">R at resolution on the set's basis (a second target, never a feature).</param>
/// <param name="SessionsHeld">Sessions held; preferred over minutes to resolution for swing (ADR 0065).</param>
/// <param name="ResolvedEpochSeconds">The end of the label horizon, for purging.</param>
public sealed record TrainingRow(string TradeKey, string Strategy, string Timeframe, string Side, DateOnly TradeDate, long DecisionEpochSeconds, IReadOnlyList<double> Values, int Label, double R, int SessionsHeld, long ResolvedEpochSeconds);
