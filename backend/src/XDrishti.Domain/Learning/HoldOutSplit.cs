namespace XDrishti.Domain.Learning;

/// <summary>The development rows and the reserved hold-out of <see cref="HoldOut.Reserve"/> (P6-05).</summary>
/// <param name="Development">Rows for every tuning and model choice.</param>
/// <param name="Reserved">Rows of the last months: looked at once, at the phase check (P6-18).</param>
/// <param name="Start">The first date of the hold-out; pass it as <see cref="ValidationOptions.HoldOutStart"/> so that development refuses a row from it.</param>
/// <param name="Purged">Development rows dropped because their label horizon reaches into the hold-out.</param>
public sealed record HoldOutSplit(IReadOnlyList<ValidationRow> Development, IReadOnlyList<ValidationRow> Reserved, DateOnly Start, int Purged);
