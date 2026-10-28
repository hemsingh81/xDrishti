using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.LiveTrading;

/// <summary>Reads the ramp of a live account (spec P11-04): the step, the scale it gives and the history. Read only.</summary>
public sealed record GetRampQuery(Guid AccountId) : IQuery<Result<RampView>>;

/// <summary>
/// The owner sets the ramp step of an account (spec P11-04): the first step (S1) or a lower one. Raising needs the evidence gates (P11-04b) and is refused until they exist. The actor is <c>owner:...</c> from the signed-in identity after a fresh password
/// (the endpoint), never from a body.
/// </summary>
public sealed record SetRampStepCommand(string Actor, Guid AccountId, int ToStep, string? Reason) : ICommand;

/// <summary>One event of the ramp history.</summary>
/// <param name="Id">The event id (allocations carry it).</param>
/// <param name="Kind">set_step, auto_step_up, hold, clear_hold, step_down or exit_confirmed.</param>
/// <param name="FromStep">The step before; null for the first.</param>
/// <param name="ToStep">The step after.</param>
/// <param name="Actor">owner:... or rule:ramp.</param>
/// <param name="Reason">Why.</param>
/// <param name="CreatedAt">When.</param>
/// <param name="GateHash">The hash of the frozen gate snapshot (P11-04b); null before.</param>
public sealed record RampEventView(long Id, string Kind, int? FromStep, int ToStep, string Actor, string Reason, DateTimeOffset CreatedAt, string? GateHash);

/// <summary>
/// An account on the ramp. <c>step</c> is null while no step is set (no live allocation). <c>scaleEffective</c> is the factor of the step; the live risk is the intended risk times it. <c>gatesBuilt</c> is false until P11-04b: <c>gates</c> is then empty and
/// <c>eligible</c> is false, so the step cannot be raised.
/// </summary>
/// <param name="AccountId">The account.</param>
/// <param name="Step">1 to 3, or null.</param>
/// <param name="ScaleEffective">The factor in force; null with no step.</param>
/// <param name="Hold">Raising is blocked.</param>
/// <param name="HoldReason">Why, or null.</param>
/// <param name="NextStep">The step a raise would reach; null at S3 or with no step.</param>
/// <param name="Eligible">All evidence gates pass; false while the gates are not built.</param>
/// <param name="GatesBuilt">Whether the evidence gates exist.</param>
/// <param name="IntendedRiskPct">The risk per trade of the profile (with the account's override), percent of capital; null when not known.</param>
/// <param name="LiveRiskPct">The intended risk times the scale.</param>
/// <param name="WritesEnabled">Whether the web may write the ramp now (<c>Live:Writes:Enabled</c>).</param>
/// <param name="History">Newest first.</param>
/// <param name="Notice">No setting lets this system place an order.</param>
public sealed record RampView(Guid AccountId, int? Step, double? ScaleEffective, bool Hold, string? HoldReason, int? NextStep, bool Eligible, bool GatesBuilt, double? IntendedRiskPct, double? LiveRiskPct, bool WritesEnabled, IReadOnlyList<RampEventView> History, string Notice);

/// <summary>Reads the live switches and their history. Read only.</summary>
public sealed record GetLiveSettingsQuery : IQuery<LiveSettingsView>;

/// <summary>The owner changes the live switches (spec P11-04); both default OFF. Appends one audited row. The actor comes from the signed-in identity after a fresh password (the endpoint).</summary>
/// <param name="Actor">owner:...</param>
/// <param name="Reason">Why, 1 to 500 characters.</param>
/// <param name="AutoApproveTickets">The new value of <c>live.auto_approve_tickets</c>.</param>
/// <param name="AutoRampUp">The new value of <c>live.auto_ramp_up</c>.</param>
/// <param name="ExpectedSettingsId">The row the form was loaded at (0 for none); a different row now is a 409.</param>
public sealed record ChangeLiveSettingsCommand(string Actor, string? Reason, bool AutoApproveTickets, bool AutoRampUp, long? ExpectedSettingsId) : ICommand;

/// <summary>One change of the switches.</summary>
public sealed record LiveSettingsChangeView(long Id, bool AutoApproveTickets, bool AutoRampUp, string Actor, string Reason, DateTimeOffset ChangedAt);

/// <summary>The live switches in force. <c>id</c> is null while nothing is stored (both OFF).</summary>
/// <param name="Id">The row in force.</param>
/// <param name="AutoApproveTickets"><c>live.auto_approve_tickets</c>: a ticket with a live allocation may be approved by rule (also needs <c>autonomy.auto_approve_tickets</c>).</param>
/// <param name="AutoRampUp"><c>live.auto_ramp_up</c>: the evaluator may raise S1 to S2 by rule (P11-04b; no effect before).</param>
/// <param name="WritesEnabled">Whether the web may write now.</param>
/// <param name="Notice">No setting lets this system place an order.</param>
/// <param name="History">Newest first.</param>
public sealed record LiveSettingsView(long? Id, bool AutoApproveTickets, bool AutoRampUp, bool WritesEnabled, string Notice, IReadOnlyList<LiveSettingsChangeView> History);

/// <summary>The sentence on every live write screen and response.</summary>
internal static class LiveNotice
{
    public const string NoOrder = "No setting lets this system place, change or cancel an order: you place every order yourself.";
}
