namespace XDrishti.Domain.Learning;

/// <summary>
/// The stored decision-time features of one trade of one run (P6-04, ADR 0066): the first <see cref="TradeFeatureCatalog.StoredCount"/> values of catalog <see cref="TradeFeatureCatalog.Version"/>, <see cref="double.NaN"/> where a value was unknown.
/// </summary>
/// <param name="RunKey">The 64-hex key of the stored run.</param>
/// <param name="TradeKey">The trade's key within the run.</param>
/// <param name="Strategy">The strategy id.</param>
/// <param name="Timeframe">The evaluation timeframe code.</param>
/// <param name="Side">BUY or SELL.</param>
/// <param name="TradeDate">India date of the entry.</param>
/// <param name="DecisionEpochSeconds">The decision instant.</param>
/// <param name="Values">The stored values, in catalog order.</param>
public sealed record TradeFeatureRow(string RunKey, string TradeKey, string Strategy, string Timeframe, string Side, DateOnly TradeDate, long DecisionEpochSeconds, IReadOnlyList<double> Values);
