namespace XDrishti.Domain.Learning.Extensions;

/// <summary>Everything the P15 phase check reads (P15-12), as plain facts. A value that could not be read is null (or an unreadable flag), never zero.</summary>
/// <param name="Families">The five families' records; a family that is missing here reads as unreadable.</param>
/// <param name="Filter">The event-risk switch.</param>
/// <param name="SeasonsHeld">Seasons (calendar quarters) the stored bars span, or null.</param>
/// <param name="Filings">Filings coverage per kind, or null when it could not be read.</param>
/// <param name="TrackedInstruments">Tracked instruments, or null.</param>
/// <param name="InstrumentsWithEntry">Instruments with a results-calendar entry at the check time, or null.</param>
/// <param name="NoUpcomingShare">The share of entries lost to <c>no_upcoming_entry</c> in the newest protocol F record, or null.</param>
/// <param name="NoUpcomingRecord">The id of that record, or null.</param>
public sealed record EventsPhaseFacts(
    IReadOnlyList<EventsFamilyFacts> Families,
    EventsFilterFact Filter,
    int? SeasonsHeld,
    IReadOnlyList<EventsKindFact>? Filings,
    int? TrackedInstruments,
    int? InstrumentsWithEntry,
    double? NoUpcomingShare,
    string? NoUpcomingRecord);
