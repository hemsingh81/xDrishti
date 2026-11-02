namespace XDrishti.Domain.Research;

/// <summary>
/// What a run that names <c>days_to_results</c> or <c>days_since_results</c> read (P15-05, ADR 0185): the hash and rows of the results-calendar snapshot (<c>-</c> and 0 when it had none), the hash and count of the results filing times, the lag and the version of the series rules.
/// Part of the run key, so the same strategy over other rows or filings is another run. Null for a run whose strategy names neither feature.
/// </summary>
/// <param name="CalendarSha256">Lower-case hex SHA-256 of the canonical snapshot, or <c>-</c> when the run had no snapshot.</param>
/// <param name="CalendarRows">Rows in the snapshot.</param>
/// <param name="FilingsSha256">Lower-case hex SHA-256 of the canonical filing times.</param>
/// <param name="FilingRows">Filing times in it.</param>
/// <param name="LagMinutes">The lag applied to every intimation and broadcast time.</param>
/// <param name="RdsVersion">The version of the series rules (<c>rds_version</c>).</param>
public sealed record ResultsDaysRecord(string CalendarSha256, int CalendarRows, string FilingsSha256, int FilingRows, int LagMinutes, string RdsVersion);
