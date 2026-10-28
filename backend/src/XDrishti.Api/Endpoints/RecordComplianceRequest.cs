namespace XDrishti.Api.Endpoints;

/// <summary>The body of a compliance record (spec P11-05). The owner is never part of it: the actor comes from the signed-in session. The lock end is not part of it either: the server derives it.</summary>
/// <param name="Kind">static_ip, sebi_recheck or dhan_ip_read_policy.</param>
/// <param name="Outcome">One of the outcomes of the kind (static_ip: not_needed_for_manual, requested, obtained, whitelisted, declined, blocked; sebi_recheck: rechecked_no_change, rechecked_changed, blocked; dhan_ip_read_policy: reads_not_ip_restricted, reads_ip_restricted, unanswered).</param>
/// <param name="DecidedOn">The India date of the decision; not in the future.</param>
/// <param name="Summary">1 to 1000 characters of what you were told or read. No IP address, client id or token: they are refused.</param>
/// <param name="Sources">At most 10 sources, each an https:// URL or a circular id such as NSE/INVG/73992.</param>
/// <param name="StaticIpState">static_ip only: none, requested, active or whitelisted.</param>
/// <param name="Isp">static_ip only: the provider's name, up to 80 characters, no address.</param>
/// <param name="MonthlyCost">static_ip only: rupees a month.</param>
/// <param name="IpVersion">static_ip only: v4 or v6.</param>
/// <param name="WhitelistedAt">static_ip only: the date set at Dhan; required when the state is whitelisted, refused otherwise.</param>
/// <param name="NextReviewOn">After decidedOn and at most 400 days ahead.</param>
/// <param name="Password">The owner's password, re-entered for THIS save. It is checked on every request (a step-up window opened earlier does not count and is not used up or extended); wrong ones count toward the sign-in lockout.</param>
internal sealed record RecordComplianceRequest(
    string? Kind,
    string? Outcome,
    DateOnly DecidedOn,
    string? Summary,
    string[]? Sources,
    string? StaticIpState,
    string? Isp,
    decimal? MonthlyCost,
    string? IpVersion,
    DateOnly? WhitelistedAt,
    DateOnly NextReviewOn,
    string? Password);
