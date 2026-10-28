namespace XDrishti.Domain.LiveTrading;

/// <summary>
/// One stored snapshot of the live switches (spec P11-04, ADR 0148): <c>live.auto_approve_tickets</c> and <c>live.auto_ramp_up</c>. The newest row is current; with no row both are OFF and <see cref="Id"/> is null, so the defaults can never be the basis of
/// an automatic approval. ON means no manual approval is needed for what the rule allows; no setting ever allows an order to be placed.
/// </summary>
/// <param name="Id">The row id, or null for the defaults.</param>
/// <param name="Hash">SHA-256 (hex) of the canonical text, stamped by the database; null for the defaults.</param>
/// <param name="AutoApproveTickets">An automatic approval of a ticket with a live allocation is allowed (as well as <c>autonomy.auto_approve_tickets</c>).</param>
/// <param name="AutoRampUp">The evaluator of P11-04b may write an <c>auto_step_up</c> (up to S2 only).</param>
public sealed record LiveSettings(long? Id, string? Hash, bool AutoApproveTickets, bool AutoRampUp)
{
    public const string AutoApproveKey = "live.auto_approve_tickets";
    public const string AutoRampKey = "live.auto_ramp_up";

    /// <summary>Nothing stored: both OFF (manual).</summary>
    public static LiveSettings Defaults { get; } = new(null, null, false, false);

    /// <summary>Whether an automatic approval can rest on this snapshot: switch ON and a stored row.</summary>
    public bool CanAutoApprove => AutoApproveTickets && Id is not null && !string.IsNullOrEmpty(Hash);
}
