namespace XDrishti.Application.Market.Bars.Csv;

/// <summary>
/// What reading one CSV file found. <see cref="Rows"/> are the data rows (blank lines excluded), <see cref="Accepted"/> the ones handed to the sink
/// (<see cref="Repaired"/> among them), <see cref="Rejected"/> the others, counted by reason in <see cref="Rejections"/> with the first few as <see cref="Examples"/>.
/// <see cref="FileError"/> is set when the file as a whole is unusable (no usable header, a line that is not CSV): nothing of it may be stored.
/// </summary>
internal sealed record CsvReadSummary(
    long Rows,
    long Accepted,
    long Repaired,
    long Rejected,
    IReadOnlyDictionary<string, long> Rejections,
    IReadOnlyList<string> Examples,
    string? FileError);
