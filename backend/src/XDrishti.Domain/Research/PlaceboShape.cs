namespace XDrishti.Domain.Research;

/// <summary>One of the 20 fixed placebo shapes of CF-07 (D2): a name, what it does and by how much. There are no free parameters.</summary>
/// <param name="Name">The stable name (<c>mirror</c>, <c>shift_+15</c>, <c>date_shift_-3</c>, <c>instrument_roll_2</c>, ...).</param>
/// <param name="Kind">What the shape changes.</param>
/// <param name="Amount">Minutes (<see cref="PlaceboKind.ShiftMinutes"/>), sessions (<see cref="PlaceboKind.ShiftSessions"/>, negative is earlier) or places (<see cref="PlaceboKind.RollInstrument"/>); 0 for the mirror.</param>
public sealed record PlaceboShape(string Name, PlaceboKind Kind, int Amount);
