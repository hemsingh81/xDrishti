using XDrishti.Domain.Execution.Confirm;

namespace XDrishti.Execution.Domain.Checks;

/// <summary>What to do with an intent in <c>checking</c> whose newest run was judged.</summary>
public enum RejectionStep
{
    /// <summary>Nothing to move: no rejected run, or a confirmed ticket the hand-off (and the pre-send re-run) judges next.</summary>
    None,

    /// <summary>Move <c>checking&gt;rejected</c> now, with the codes in the payload. Final.</summary>
    RejectNow,

    /// <summary>A Soft-only rejection in <c>semi_auto</c> whose newest ticket is open: stay in <c>checking</c> for the owner's decision.</summary>
    AwaitTicket,
}

/// <summary>The short reasons of a decision (stored in the event payload and logged). Never a price, a name or an account.</summary>
public static class RejectionReasons
{
    public const string NoRun = "no_run";
    public const string NotRejected = "not_rejected";
    public const string HardCheck = "hard_check";
    public const string CheckRejected = "check_rejected";
    public const string NotSemiAuto = "not_semi_auto";
    public const string ModeUnavailable = "mode_unavailable";
    public const string NoTicket = "no_ticket";
    public const string TicketOpen = "ticket_open";
    public const string TicketSkipped = "ticket_skipped";
    public const string TicketExpired = "ticket_expired";
    public const string TicketRefused = "ticket_refused";
    public const string TicketClosed = "ticket_closed";
    public const string SoftSetChanged = "soft_set_changed";
    public const string Confirmed = "confirmed";

    /// <summary>The stored reason of a ticket the system closed because its intent was rejected.</summary>
    public const string IntentRejected = "intent_rejected";
}

/// <summary>The newest full or fast run of an intent: its verdict and the codes that did not pass, by class (Fail and Unavailable alike).</summary>
public sealed record RejectionRun(long Id, CheckVerdict Verdict, IReadOnlyList<string> HardNonPass, IReadOnlyList<string> SoftNonPass);

/// <summary>The newest confirm ticket of an intent: its id, its outcome (null while none is stored), its expiry and the Soft codes it was issued for.</summary>
public sealed record RejectionTicket(Guid Id, string? Outcome, DateTimeOffset ExpiresAt, IReadOnlyList<string> SoftCodes);

/// <summary>A decision and, for <see cref="RejectionStep.RejectNow"/>, the codes the move carries (Hard first in run order, then Soft sorted).</summary>
public sealed record RejectionDecision(RejectionStep Step, string Reason, IReadOnlyList<string> Codes);

/// <summary>
/// When a rejected run is final (spec P12-03 amendment "a Soft-only rejection is not final while a confirm ticket is open", P12-08 criterion 12, ADR 0166). <c>checking&gt;rejected</c> is decided at once for any Hard non-pass (Fail or
/// Unavailable, whatever the ticket says), for an account whose effective mode is read and is not <c>semi_auto</c> (a mode that cannot be read moves nothing, unless a Hard check failed), and for a Soft-only rejection whose newest ticket is missing, skipped, expired (stored, or past its expiry by the
/// database clock), refused or closed in any other way. Only a Soft-only rejection in <c>semi_auto</c> whose newest ticket is open waits. A confirmed ticket moves nothing here when it was issued for the run's exact Soft set (the hand-off
/// and the pre-send full run judge it); a changed set voids the confirmation (criterion 4) and is final. Pure: it reads nothing, sends nothing, and can only make the intent final; it never makes anything sendable.
/// </summary>
public static class RejectionFinality
{
    public static RejectionDecision Decide(RejectionRun? run, ModeAnswer? mode, RejectionTicket? ticket, DateTimeOffset dbNow)
    {
        if (run is null)
        {
            return new RejectionDecision(RejectionStep.None, RejectionReasons.NoRun, []);
        }

        if (run.Verdict != CheckVerdict.Rejected)
        {
            return new RejectionDecision(RejectionStep.None, RejectionReasons.NotRejected, []);
        }

        IReadOnlyList<string> codes = [.. run.HardNonPass, .. run.SoftNonPass.Order(StringComparer.Ordinal)];
        if (run.HardNonPass.Count > 0)
        {
            return Reject(RejectionReasons.HardCheck, codes);
        }

        if (run.SoftNonPass.Count == 0)
        {
            return Reject(RejectionReasons.CheckRejected, codes);
        }

        if (mode is null)
        {
            return new RejectionDecision(RejectionStep.None, RejectionReasons.ModeUnavailable, []); // unread is not a mode: move nothing, the next pass reads again
        }

        if (mode.EffectiveMode != ExecMode.SemiAuto)
        {
            return Reject(RejectionReasons.NotSemiAuto, codes);
        }

        return ticket switch
        {
            null => Reject(RejectionReasons.NoTicket, codes),
            { Outcome: null } when dbNow >= ticket.ExpiresAt => Reject(RejectionReasons.TicketExpired, codes),
            { Outcome: null } => new RejectionDecision(RejectionStep.AwaitTicket, RejectionReasons.TicketOpen, []),
            { Outcome: TicketOutcomes.Confirmed } when SameSet(ticket.SoftCodes, run.SoftNonPass) => new RejectionDecision(RejectionStep.None, RejectionReasons.Confirmed, []),
            { Outcome: TicketOutcomes.Confirmed } => Reject(RejectionReasons.SoftSetChanged, codes),
            { Outcome: TicketOutcomes.Skipped } => Reject(RejectionReasons.TicketSkipped, codes),
            { Outcome: TicketOutcomes.Expired } => Reject(RejectionReasons.TicketExpired, codes),
            { Outcome: TicketOutcomes.Refused } => Reject(RejectionReasons.TicketRefused, codes),
            _ => Reject(RejectionReasons.TicketClosed, codes),
        };
    }

    private static RejectionDecision Reject(string reason, IReadOnlyList<string> codes) => new(RejectionStep.RejectNow, reason, codes);

    private static bool SameSet(IReadOnlyList<string> a, IReadOnlyList<string> b) =>
        a.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).SequenceEqual(b.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal), StringComparer.Ordinal);
}
