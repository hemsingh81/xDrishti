using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Paper;

namespace XDrishti.Application.Paper;

/// <summary>
/// Captures the paper trades of armed tickets (spec P10-01, ADR 0116): each armed allocation of a closed session is replayed by the grading path over the stored bars and stored as an entry (and an exit when closed) or a skip. Insert-only and idempotent.
/// It places no order, calls no broker and changes no ticket.
/// </summary>
/// <param name="Date">One closed session, or null for every session of the epoch without a finished capture (newest 30 with work).</param>
/// <param name="Manual">Whether a person started it.</param>
public sealed record CapturePaperFillsCommand(DateOnly? Date, bool Manual) : ICommand;

/// <summary>What a capture run did.</summary>
/// <param name="Sessions">Sessions processed.</param>
/// <param name="Seen">Armed allocations looked at.</param>
/// <param name="EntriesWritten">Entries written.</param>
/// <param name="ExitsWritten">Exits written.</param>
/// <param name="SkipsWritten">Skips written.</param>
/// <param name="Pending">Pairs left for a later run.</param>
/// <param name="Integrity">Stored entries a later replay disagreed with.</param>
/// <param name="FailedSessions">Sessions that failed and were recorded as failed.</param>
public sealed record PaperCaptureOutcome(int Sessions, int Seen, int EntriesWritten, int ExitsWritten, int SkipsWritten, int Pending, int Integrity, int FailedSessions)
{
    /// <summary>One line for the Services screen and the verb.</summary>
    public string Summary =>
        $"{Sessions} session(s), {Seen} armed allocation(s): {EntriesWritten} entries, {ExitsWritten} exits, {SkipsWritten} skips, {Pending} pending, {Integrity} integrity, {FailedSessions} failed. Basis: {PaperWording.Basis}";
}

/// <summary>The paper entries (spec P10-01); dates are India dates of the entry instant.</summary>
/// <param name="From">First day, or null.</param>
/// <param name="To">Last day, or null.</param>
/// <param name="AccountId">One account, or null.</param>
/// <param name="EpochId">One epoch, or null.</param>
/// <param name="Limit">1 to 200.</param>
public sealed record ListPaperEntriesQuery(DateOnly? From, DateOnly? To, Guid? AccountId, long? EpochId, int Limit) : IQuery<Result<PaperEntryList>>;

/// <summary>The capture rows (spec P10-01).</summary>
/// <param name="Limit">1 to 200.</param>
public sealed record ListCaptureRunsQuery(int Limit) : IQuery<Result<CaptureRunList>>;

/// <summary>The epoch in force, or none before the first capture (spec P10-01).</summary>
public sealed record GetPaperEpochQuery : IQuery<Result<PaperEpochView>>;

/// <summary>The epoch shown by <c>xd paper epoch show</c>.</summary>
/// <param name="Id">The id; null before the first capture.</param>
/// <param name="StartedOn">The first session.</param>
/// <param name="Kind">first, detected_change or owner_restart.</param>
/// <param name="Reason">Why.</param>
/// <param name="Basis">The label.</param>
public sealed record PaperEpochView(long? Id, DateOnly? StartedOn, string? Kind, string? Reason, string Basis);
