
namespace XDrishti.Domain.Research;

/// <summary>
/// One real out-of-sample trade as the placebo shapes of CF-07 restate it: where and when the strategy decided, its side, the initial risk as a fraction of the trigger close, the fixed target multiple and the cost segment. Nothing of its prices is kept.
/// </summary>
/// <param name="Id">The position of the signal in the plan.</param>
/// <param name="InstrumentId">The instrument the strategy traded.</param>
/// <param name="Date">The India date of the session.</param>
/// <param name="Side">Buy or sell.</param>
/// <param name="RiskFraction"><c>|trigger close - stop| / trigger close</c>; above 0 and below 1.</param>
/// <param name="RewardRisk">The fixed target multiple; above 0.</param>
/// <param name="Segment">The cost segment of the real trade.</param>
/// <param name="DecisionEpochSeconds">The instant the strategy decided (the end of the minute that closed its evaluation bar).</param>
public sealed record PlaceboSignal(int Id, Guid InstrumentId, DateOnly Date, Side Side, double RiskFraction, double RewardRisk, CostSegment Segment, long DecisionEpochSeconds);
