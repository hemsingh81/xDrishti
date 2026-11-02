using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Filings;

/// <summary>One visible upcoming results meeting: <c>Days</c> trading sessions from the India date of the question to its <c>EventSession</c> (the first session on or after <c>MeetingDate</c>); <c>IntimatedAt</c> is when the exchange published the row; <c>Revision</c> is 1 for the first row and 2 and up for reschedules.</summary>
public sealed record ResultsMeetingView(string Symbol, Guid InstrumentId, DateOnly PeriodEnd, DateOnly MeetingDate, DateOnly EventSession, int Days, DateTimeOffset IntimatedAt, int Revision);

/// <summary>The tracked instruments that have no visible upcoming results meeting at the question (the count and the first 300 symbols). Their <c>days_to_results</c> is unavailable, never a default.</summary>
public sealed record InstrumentsWithoutEntryView(int Count, IReadOnlyList<string> Symbols);

/// <summary>The visible upcoming results meetings with an event session between <c>From</c> and <c>To</c> as known at <c>AsOf</c>, earliest first; <c>Truncated</c> when more than the cap exist. No file content and no estimate.</summary>
public sealed record ResultsCalendarView(DateOnly From, DateOnly To, DateTimeOffset AsOf, IReadOnlyList<ResultsMeetingView> Meetings, bool Truncated, InstrumentsWithoutEntryView InstrumentsWithoutEntry);

public sealed record GetResultsCalendarQuery(string? From, string? To, string? AsOf, bool UnknownParameter) : IQuery<Result<ResultsCalendarView>>;
