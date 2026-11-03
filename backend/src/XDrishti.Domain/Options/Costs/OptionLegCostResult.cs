using XDrishti.Domain.Research;

namespace XDrishti.Domain.Options.Costs;

/// <summary>
/// What one option or futures leg costs on its date (spec P14-07): the charges on the sheet when it has lines for the segment, else none and the label says the leg is gross; the half-spread cost when the spread is available, else none and the label says
/// why. <see cref="TotalRupees"/> is null unless the spread is available: a missing spread is never added as zero. Both labels always travel with the numbers.
/// </summary>
/// <param name="TurnoverRupees">Premium (or futures price) times units.</param>
/// <param name="Charges">The eight charge lines; null when the sheet has no lines for the segment on the date.</param>
/// <param name="ChargesLabel">The costs label: <c>costs: sheet ...</c> or <c>costs: none (no option lines)</c>.</param>
/// <param name="ChargesReason">When the charges are null, the code of the error (<c>costs.costs_unavailable</c> or <c>costs.no_rates_for_date</c>).</param>
/// <param name="SpreadRupees">The half-spread times the turnover, rounded to the paisa; null when the spread is unavailable.</param>
/// <param name="Spread">The half-spread and its label.</param>
/// <param name="TotalRupees">Charges (none counted as gross) plus the spread cost; null when the spread is unavailable.</param>
public sealed record OptionLegCostResult(decimal TurnoverRupees, LegCharges? Charges, string ChargesLabel, string? ChargesReason, decimal? SpreadRupees, HalfSpreadValue Spread, decimal? TotalRupees);
