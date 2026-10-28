using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.LiveTrading;

/// <summary>The day's capture of one live account (spec P11-01). Every order-dependent field is <c>unavailable</c> (not a number) when the session has no order evidence. Nothing here is an instruction.</summary>
public sealed record LiveCaptureQuery(Guid AccountId, DateOnly? Date) : IQuery<Result<LiveCaptureView>>;

/// <summary>The capture of one session: its health, the runs, one row per approved ticket, the fills no ticket owns and the owner's own trades, and what the evidence is.</summary>
public sealed record LiveCaptureView(
    DateOnly Date,
    string Health,
    IReadOnlyList<LiveCaptureRunView> Runs,
    IReadOnlyList<LiveCaptureTicketView> Tickets,
    IReadOnlyList<LiveCaptureFillView> Unmatched,
    IReadOnlyList<LiveCaptureFillView> Discretionary,
    LiveCaptureEvidenceView Evidence);

/// <summary>One run of a poll or the settle.</summary>
public sealed record LiveCaptureRunView(string Kind, DateTimeOffset StartedAt, string Outcome, int FillsNew, int OrdersNew);

/// <summary>
/// One approved ticket. <see cref="Outcome"/> is null while the settle has not written it (the figures beside it are what is captured so far); <see cref="OrderedQty"/> and <see cref="Protection"/> are the words <c>unavailable</c> / null when there is no order
/// evidence; <see cref="MissedR"/> only for the two missed outcomes; <see cref="RejectText"/> is the broker's own text, data to be shown as text.
/// </summary>
public sealed record LiveCaptureTicketView(
    long ItemId,
    string Symbol,
    string Side,
    long AllocatedQty,
    long? OrderedQty,
    long FilledQty,
    decimal? AvgEntry,
    string? Outcome,
    IReadOnlyList<string> Flags,
    double? MissedR,
    string OrderEvidence,
    string Protection,
    string? RejectText);

/// <summary>A fill no ticket owns (unmatched, with the reason) or the owner's own trade.</summary>
public sealed record LiveCaptureFillView(string Symbol, string Side, string Product, long Qty, decimal Price, string? Time, string Reason);

/// <summary>What the capture rests on, with its stated limits.</summary>
public sealed record LiveCaptureEvidenceView(string OrderEvidence, int PollRuns, int Fills, int Orders, bool OrderBookEnabled, IReadOnlyList<string> Limits);

/// <summary>A page of the outcomes of live tickets in a range of sessions (size 1 to 200, range at most 92 days).</summary>
public sealed record LiveOutcomesQuery(Guid? AccountId, DateOnly? From, DateOnly? To, int Page, int Size) : IQuery<Result<LiveOutcomesPage>>;

/// <summary>A page of outcomes.</summary>
public sealed record LiveOutcomesPage(IReadOnlyList<LiveOutcomeView> Items, int Page, int Size, int Total);

/// <summary>One stored outcome (final).</summary>
public sealed record LiveOutcomeView(
    long ItemId,
    Guid AccountId,
    DateOnly Date,
    string Outcome,
    long AllocatedQty,
    long? OrderedQty,
    long EntryQty,
    DateTimeOffset? FirstEntryAt,
    IReadOnlyList<string> Flags,
    double? MissedR,
    string PlanGradeStatus,
    string OrderEvidence,
    string Protection,
    string? RejectText,
    DateTimeOffset WrittenAt);
