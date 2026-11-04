namespace XDrishti.Domain.Learning.Extensions;

/// <summary>The archive watermark a record rests on (the highest finished fetch-log id its re-mark read): <see cref="Id"/> 0 means the run read no fetch; <see cref="EndedAt"/> null with an id above 0 means the fetch row could not be found.</summary>
public sealed record OptionsWatermarkFact(long Id, DateTimeOffset? EndedAt);

/// <summary>
/// One stored P14 evaluation as the phase check reads it (spec P14-17). <see cref="Recorded"/> are the versions the record rests on (re-mark version, engine, spread model ids, cost sheet hash, profile, catalog, version key) and <see cref="Current"/> those in force now for the same inputs;
/// a key in <see cref="Current"/> that the record did not record, or recorded differently, makes the record stale. <see cref="DevelopmentRecord"/> is, for a hold-out record, the development record it was read for. <see cref="Basis"/> is <c>net</c>, <c>gross</c>, <c>mixed</c>, <c>none</c> or <c>n/a</c> (the protocol has no charge basis).
/// </summary>
public sealed record OptionsRecordFact(
    string Id,
    string Protocol,
    string Verdict,
    bool HoldOutRead,
    int Trials,
    int Rows,
    IReadOnlyList<string> Missing,
    string Basis,
    IReadOnlyDictionary<string, string> Recorded,
    IReadOnlyDictionary<string, string> Current,
    OptionsWatermarkFact? Watermark,
    string? DevelopmentRecord,
    double? DerivedExpiryShare,
    double? FallbackShare);

/// <summary>The stored records of one family, newest first, already limited to those that can decide (for the expression families: those of the pre-registered primary profile). <see cref="Readable"/> is false when the store could not be read: the family is then open.</summary>
public sealed record OptionsFamilyFacts(string Family, bool Readable, IReadOnlyList<OptionsRecordFact> Records);

/// <summary>A stored plan item or paper entry whose cell key has an eighth part (a non-cash expression); <see cref="Source"/> is the table.</summary>
public sealed record OptionsCellRow(string Source, long Id);

/// <summary>The cell rows with a non-cash eighth part, at most the bound the reader was asked for; <see cref="Readable"/> is false when they could not be read.</summary>
public sealed record OptionsCellFacts(bool Readable, IReadOnlyList<OptionsCellRow> Rows, bool Truncated);

/// <summary>One option underlying of the archive: usable chain sessions and the backfill keys finished or given up; null when there is no source for the figure.</summary>
public sealed record OptionsUnderlyingFact(string Underlying, int? UsableSessions, int? BackfillDone, int? BackfillGivenUp);

/// <summary>The archive figures printed with every report; null where there is no source.</summary>
/// <param name="YearsHeld">Years between the first and the last stored bar, or null.</param>
/// <param name="Underlyings">The option underlyings, or null when the summary could not be read.</param>
/// <param name="RecorderOn">The newest chain-recorder switch row (no row is OFF), or null when unreadable.</param>
/// <param name="BackfillOn">The newest backfill switch row (no row is OFF), or null when unreadable.</param>
public sealed record OptionsArchiveFacts(double? YearsHeld, IReadOnlyList<OptionsUnderlyingFact>? Underlyings, bool? RecorderOn, bool? BackfillOn);

/// <summary>Everything the P14 phase check reads, as plain facts, with the check time, the staleness bound the handler took from the setting and the end of the newest finished archive fetch (null when none): a record is stale when the archive has moved on from its watermark by more than the bound.</summary>
public sealed record OptionsPhaseFacts(IReadOnlyList<OptionsFamilyFacts> Families, OptionsCellFacts Cells, OptionsArchiveFacts Archive, DateTimeOffset Now, int StaleAfterDays, DateTimeOffset? LatestFetchEndedAt);
