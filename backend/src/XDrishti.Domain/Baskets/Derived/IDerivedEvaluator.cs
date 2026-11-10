using XDrishti.Domain.Common;

namespace XDrishti.Domain.Baskets.Derived;

/// <summary>
/// Computes one derived basket as of a date, from values only. Reads daily bars dated at or before <c>asOf</c> and intraday bars before <c>formationTime</c>.
/// Fails (no result) when the basket needs intraday bars and no formation time is given.
/// </summary>
public interface IDerivedEvaluator
{
    DerivedDefinition Definition { get; }

    Result<DerivedResult> Evaluate(IReadOnlyList<InstrumentBars> universe, DateOnly asOf, DerivedParams parameters, DerivedResult? previous = null, DateTimeOffset? formationTime = null);
}
