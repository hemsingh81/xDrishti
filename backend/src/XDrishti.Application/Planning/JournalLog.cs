using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Planning;

/// <summary>Structured log events of the auto-journal (P7-14; ids 4750 to 4759). Counts and codes only: no price, no account number.</summary>
internal static partial class JournalLog
{
    [LoggerMessage(EventId = 4750, Level = LogLevel.Information, Message = "journal.done: {Plans} plan(s), {Tickets} ticket(s), {Entries} entry fill(s), {Exits} exit fill(s), {Discretionary} discretionary, {Unmatched} unmatched, {Written} row(s) written, {Graded} fill grade(s)")]
    public static partial void Done(ILogger logger, int plans, int tickets, int entries, int exits, int discretionary, int unmatched, int written, int graded);

    [LoggerMessage(EventId = 4751, Level = LogLevel.Warning, Message = "journal.unmatched plan {PlanId}: {Count} fill(s) fit more than one ticket and were not matched")]
    public static partial void Unmatched(ILogger logger, long planId, int count);

    [LoggerMessage(EventId = 4752, Level = LogLevel.Information, Message = "journal.grade_pending entry {EntryId}: {Reason}")]
    public static partial void GradePending(ILogger logger, long entryId, string reason);
}
