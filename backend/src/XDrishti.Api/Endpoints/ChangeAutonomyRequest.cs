namespace XDrishti.Api.Endpoints;

/// <summary>The body of the autonomy change. The owner is never part of it: the actor comes from the signed-in session.</summary>
/// <param name="AutoApproveTickets">The new value of <c>autonomy.auto_approve_tickets</c>: ON means tickets the rule allows need no manual approval, OFF means manual approval.</param>
/// <param name="PromoteFromReserve">The new value of <c>autonomy.promote_from_reserve</c>.</param>
/// <param name="MinPWin">The least lower-bound P(win), 0.5 to 0.99 (design default 0.58).</param>
/// <param name="MaxAutoPerDay">The most automatic approvals per planned session, 1 to 20.</param>
/// <param name="Reason">Why, 1 to 500 characters.</param>
internal sealed record ChangeAutonomyRequest(bool AutoApproveTickets, bool PromoteFromReserve, double MinPWin, int MaxAutoPerDay, string Reason);
