namespace XDrishti.Domain.Research;

/// <summary>
/// What a run that names the filter <c>not_event_day</c> read (P15-06, ADR 0182): the hash of the canonical results-calendar snapshot, the rows in it, the lag of the point-in-time store and the version of the event-filter rules. Part of the run key, so the same strategy over
/// another snapshot, or with another lag, is another run. Null for a run whose strategy does not name the filter. The counters of the filter (<c>event_filter_blocked</c>, <c>event_filter_unavailable</c>) are the <c>EventBlocked</c> and <c>EventUnavailable</c> fates of the run.
/// </summary>
/// <param name="CalendarSha256">Lower-case hex SHA-256 of the canonical snapshot (<c>calendar_sha256</c>).</param>
/// <param name="CalendarRows">Rows in the snapshot (<c>calendar_rows</c>).</param>
/// <param name="LagMinutes">The lag applied to every intimation time.</param>
/// <param name="EvfVersion">The version of the event-filter rules (<c>evf_version</c>).</param>
public sealed record EventFilterRecord(string CalendarSha256, int CalendarRows, int LagMinutes, string EvfVersion);
