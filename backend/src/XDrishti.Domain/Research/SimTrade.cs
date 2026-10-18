using System.Globalization;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Domain.Research;

/// <summary>
/// One simulated trade, schema version 1 (ADR 0037): where it entered, where its stop and target were, why it left, R, MAE and MFE, and what it says about itself (<see cref="Fills"/> and <see cref="Costs"/>).
/// The 24 positional fields are pinned by a test: changing any of them raises <see cref="CurrentSchemaVersion"/> and amends the ADR. Enum members are stored by name and never renamed.
/// </summary>
/// <param name="SchemaVersion">1.</param>
/// <param name="StrategyId">The strategy that decided (for example <c>ORB</c>).</param>
/// <param name="InstrumentId">The instrument.</param>
/// <param name="Symbol">Its symbol.</param>
/// <param name="Side">Buy or sell.</param>
/// <param name="TradeDate">India date of the entry.</param>
/// <param name="DecisionEpochSeconds">The instant the decision was taken: the end of the minute that closed the evaluation bar.</param>
/// <param name="TriggerTimeframe">The evaluation timeframe.</param>
/// <param name="TriggerBar">The bar that triggered; its epoch is the bucket start.</param>
/// <param name="TriggerBarMinutes">Minutes the bucket holds.</param>
/// <param name="TriggerBarComplete">Every minute of the window was there.</param>
/// <param name="EntryEpochSeconds">Start of the fill minute.</param>
/// <param name="EntryPrice">Open of the fill minute.</param>
/// <param name="StopPrice">The stop level.</param>
/// <param name="TargetPrice">The target level; null without a fixed reward-to-risk.</param>
/// <param name="ExitEpochSeconds">Start of the exit minute.</param>
/// <param name="ExitPrice">The exit price.</param>
/// <param name="ExitReason">Why the trade ended.</param>
/// <param name="R">Result in units of the initial risk.</param>
/// <param name="MaeR">Maximum adverse excursion in R, never below 0.</param>
/// <param name="MfeR">Maximum favourable excursion in R, never below 0.</param>
/// <param name="Fills">How the prices were obtained.</param>
/// <param name="Costs">Whether charges are taken off.</param>
/// <param name="EntrySnapshotJson">Reserved for the decision-time snapshot of doc 09 §3 step 3; always null in schema 1.</param>
public sealed record SimTrade(
    int SchemaVersion,
    string StrategyId,
    Guid InstrumentId,
    string Symbol,
    Side Side,
    DateOnly TradeDate,
    long DecisionEpochSeconds,
    Timeframe TriggerTimeframe,
    Bar TriggerBar,
    int TriggerBarMinutes,
    bool TriggerBarComplete,
    long EntryEpochSeconds,
    double EntryPrice,
    double StopPrice,
    double? TargetPrice,
    long ExitEpochSeconds,
    double ExitPrice,
    ExitReason ExitReason,
    double R,
    double MaeR,
    double MfeR,
    FillBasis Fills,
    CostBasis Costs,
    string? EntrySnapshotJson)
{
    /// <summary>The schema version this code writes.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary><c>StrategyId|Symbol|Side|TradeDate</c>: unique by construction for the one-trade-per-instrument-side-day rule. Computed, not stored.</summary>
    public string Key => string.Create(CultureInfo.InvariantCulture, $"{StrategyId}|{Symbol}|{Side}|{TradeDate:yyyy-MM-dd}");
}
