using System.Globalization;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Domain.Research;

/// <summary>
/// One simulated trade, schema version 4 (ADR 0037, ADR 0038, ADR 0040, ADR 0042): where it entered, where its stop and target were, why it left, R from the exact and from the filled prices, MAE and MFE, and what it says about itself
/// (<see cref="Fills"/>, <see cref="Costs"/>, <see cref="EntryFill"/> and <see cref="TickFallback"/>). The 33 positional fields (the 30 of schema 2 in their order, the two of schema 3, then <see cref="Costing"/> of schema 4) are pinned by a test: changing any of them raises <see cref="CurrentSchemaVersion"/> and
/// needs an ADR. Enum members are stored by name and never renamed.
/// </summary>
/// <param name="SchemaVersion">4.</param>
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
/// <param name="EntryPrice">The exact entry: the open of the fill minute, or the limit for an entry at the limit.</param>
/// <param name="StopPrice">The stop level as the candidate gave it (to 8 decimals); never rounded to the tick.</param>
/// <param name="TargetPrice">The target level, exact in decimal and never rounded to the tick; null without a fixed reward-to-risk.</param>
/// <param name="ExitEpochSeconds">Start of the exit minute.</param>
/// <param name="ExitPrice">The exact exit of the rule table: the stop, or the open of a minute that gapped through it; the target; the open at the square-off; the last close.</param>
/// <param name="ExitReason">Why the trade ended.</param>
/// <param name="RExact">Result from the exact prices in units of the initial risk; does not depend on the slippage or the tick.</param>
/// <param name="MaeR">Maximum adverse excursion in R, never below 0.</param>
/// <param name="MfeR">Maximum favourable excursion in R, never below 0.</param>
/// <param name="Fills">How the filled prices were obtained.</param>
/// <param name="Costs">Whether charges are taken off: <see cref="CostBasis.Charged"/> if and only if <paramref name="Costing"/> is not null.</param>
/// <param name="EntrySnapshotJson">Reserved for the decision-time snapshot of doc 09 §3 step 3; always null so far.</param>
/// <param name="RFilled">Result from the filled prices in units of the same risk as <paramref name="RExact"/>; never above it.</param>
/// <param name="SlippageR"><c>RFilled - RExact</c>; never positive.</param>
/// <param name="EntryFilledPrice">The entry after slippage and the price grid; equal to <paramref name="EntryPrice"/> when the fills are exact.</param>
/// <param name="ExitFilledPrice">The exit after slippage and the price grid; equal to <paramref name="ExitPrice"/> when the fills are exact.</param>
/// <param name="EntryFill">How the entry limit order filled.</param>
/// <param name="TickFallback">The tick was not in the instrument master and the NSE default was used.</param>
/// <param name="SessionsHeld">The sessions of the trading calendar from the entry session to the session of the exit, both included: 1 for an intraday trade and for a swing trade that leaves on its entry date (ADR 0040, D8).</param>
/// <param name="SessionsUnseen">The sessions among those on which the walker read no minute that holds a trade (a halt, or a session the replay left out); 0 for an intraday trade (ADR 0040, D7).</param>
/// <param name="Costing">The quantity, the turnover, the 16 charge lines and net R of the trade (ADR 0042); null for a gross trade. The walker never sets it: the replay does, after the window cut.</param>
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
    double RExact,
    double MaeR,
    double MfeR,
    FillBasis Fills,
    CostBasis Costs,
    string? EntrySnapshotJson,
    double RFilled,
    double SlippageR,
    double EntryFilledPrice,
    double ExitFilledPrice,
    EntryFill EntryFill,
    bool TickFallback,
    int SessionsHeld,
    int SessionsUnseen,
    TradeCosting? Costing = null)
{
    /// <summary>The schema version this code writes.</summary>
    public const int CurrentSchemaVersion = 4;

    /// <summary><c>StrategyId|Symbol|Side|TradeDate</c>: unique by construction for the one-trade-per-instrument-side-day rule. Computed, not stored.</summary>
    public string Key => string.Create(CultureInfo.InvariantCulture, $"{StrategyId}|{Symbol}|{Side}|{TradeDate:yyyy-MM-dd}");
}
