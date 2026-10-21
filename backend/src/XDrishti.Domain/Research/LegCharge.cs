namespace XDrishti.Domain.Research;

/// <summary>The charge of one leg as one order (P4-01, D11): its quantity, its turnover on the filled price and its eight lines.</summary>
/// <param name="EpochSeconds">The leg's instant, the one of the <see cref="Leg"/> it charges.</param>
/// <param name="Quantity">Whole shares of this order, at least 1 (a leg of no shares is merged and not charged).</param>
/// <param name="Turnover"><c>Quantity ×</c> the filled price of the leg.</param>
/// <param name="Charges">The eight lines of the order on the schedule in force on the leg's date.</param>
public sealed record LegCharge(long EpochSeconds, long Quantity, decimal Turnover, LegCharges Charges);
