namespace XDrishti.Domain.Learning.Extensions;

/// <summary>One calendar test window of the comparison (P13-05) with its train rows. A fold with a <see cref="SkipReason"/> is not used.</summary>
/// <param name="Start">The first day of the window.</param>
/// <param name="End">The last day of the window.</param>
/// <param name="Train">The train rows: before the window, outside the embargo, not purged. In the order of the input.</param>
/// <param name="Test">The rows dated inside the window.</param>
/// <param name="SkipReason">Why the fold is not used, or null.</param>
public sealed record CalendarFold(DateOnly Start, DateOnly End, IReadOnlyList<ValidationRow> Train, IReadOnlyList<ValidationRow> Test, string? SkipReason)
{
    /// <summary>Whether the fold is used.</summary>
    public bool Usable => SkipReason is null;
}
