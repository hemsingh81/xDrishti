using XDrishti.Domain.Market.Actions;

namespace XDrishti.Application.Market.Actions;

/// <summary>
/// The price adjustment of an instrument for its applied corporate actions (ADR 0017 §5): the one place other slices (research reads, charts) get it from. Bars stay raw in the database;
/// callers ask for the factors and adjust on read (<see cref="AdjustmentFactors.FactorAt"/>, <see cref="AdjustmentFactors.Price"/>, <see cref="AdjustmentFactors.Volume"/>). Plans use raw prices.
/// </summary>
public interface IAdjustmentFactors
{
    /// <summary>The factors of the instrument's applied actions (<see cref="AdjustmentFactors.None"/> when it has none).</summary>
    ValueTask<AdjustmentFactors> GetAsync(Guid instrumentId, CancellationToken cancellationToken);
}
