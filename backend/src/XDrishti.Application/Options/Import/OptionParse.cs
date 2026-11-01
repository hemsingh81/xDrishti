namespace XDrishti.Application.Options.Import;

/// <summary>
/// What the master parse found for the option archive: the rows of listed underlyings, the option rows of other underlyings (<see cref="SkippedNotListed"/>),
/// the rows that could not be read (<see cref="Invalid"/>, never stored with a zero), and <see cref="Unavailable"/> when the file lacks a column options need
/// (then nothing is read and the master list is imported as before).
/// </summary>
internal sealed record OptionParse(IReadOnlyList<OptionContractRow> Rows, long SkippedNotListed, long Invalid, string? Unavailable);
