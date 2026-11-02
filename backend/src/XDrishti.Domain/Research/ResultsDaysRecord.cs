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
/// <param name="SurpriseSha256">Lower-case hex SHA-256 of the canonical surprise observations a run that names a quintile feature read (P15-09); null for any other run, whose key is then unchanged.</param>
/// <param name="SurpriseRows">Observations in it.</param>
/// <param name="SurpriseUnverified">Observations whose <c>sue_yoy</c> rests on the synthetic parser.</param>
/// <param name="SurpriseSeasons">Seasons (calendar quarters) with at least one surprise observation.</param>
/// <param name="SurpriseThinSeasons">Of them, the seasons with fewer than 100 finite values of either measure (NaN breakpoints).</param>
/// <param name="MissedReactionFilings">Filings whose reaction session lies in the window and which were broadcast before its open, so <c>days_since_results == 0</c> never fires for them.</param>
/// <param name="ReactionFilings">Filings whose reaction session lies in the window.</param>
/// <param name="SbpVersion">The version of the breakpoint rules (<c>sbp-1</c>); null with <paramref name="SurpriseSha256"/>.</param>
public sealed record ResultsDaysRecord(string CalendarSha256, int CalendarRows, string FilingsSha256, int FilingRows, int LagMinutes, string RdsVersion, string? SurpriseSha256 = null, int SurpriseRows = 0, string? SbpVersion = null, int SurpriseUnverified = 0, int SurpriseSeasons = 0, int SurpriseThinSeasons = 0, int MissedReactionFilings = 0, int ReactionFilings = 0);
