namespace XDrishti.Domain.Research;

/// <summary>
/// One real trade as the random-entry null (CF-07, D1) restates it: the instrument, the session and the side it traded, the initial risk as a fraction of its trigger close (the price a stop is placed from), the target multiple and the cost segment. A random trade of the null has exactly
/// these and enters at another minute of the same session. Nothing of the real trade's prices is kept.
/// </summary>
/// <param name="Id">The position of the trade in the plan (the seed of its draws is derived from it, so it must not depend on the order instruments are walked).</param>
/// <param name="InstrumentId">The instrument.</param>
/// <param name="Date">The India date of the session.</param>
/// <param name="Side">Buy or sell.</param>
/// <param name="RiskFraction"><c>|trigger close - stop| / trigger close</c> of the real trade (the random trade places its stop the same way from its own trigger close); above 0 and below 1.</param>
/// <param name="RewardRisk">The fixed target multiple of the real trade; above 0.</param>
/// <param name="Segment">The cost segment of the real trade.</param>
public sealed record NullTradeSpec(int Id, Guid InstrumentId, DateOnly Date, Side Side, double RiskFraction, double RewardRisk, CostSegment Segment);
