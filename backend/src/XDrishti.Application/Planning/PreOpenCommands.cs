using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Planning;

/// <summary>
/// Runs the pre-open check of today's plan (spec P7-11, doc 10 section 7): each ticket the owner (or the owner's setting, P7-12) approved is armed or invalidated from the pre-open price, the previous close and the average true range.
/// Arming is a state in our own database plus a place in the live feed's set; nothing is ordered. It approves nothing and promotes nothing from the reserve.
/// </summary>
public sealed record RunPreOpenCheckCommand : ICommand;

/// <summary>What a pre-open pass did.</summary>
/// <param name="Plans">Plans of today with an approved ticket to check.</param>
/// <param name="Armed">Tickets armed.</param>
/// <param name="Invalidated">Tickets invalidated (a gap, a missing input or the arming cap).</param>
/// <param name="Summary">In words, including what could not be checked.</param>
public sealed record PreOpenOutcome(int Plans, int Armed, int Invalidated, string Summary);

/// <summary>Ends the session for the tickets of the day (spec P7-11): armed tickets, and approved ones that were never armed, expire after the close; so does their plan. Triggered tickets are left alone.</summary>
public sealed record ExpireTicketsCommand : ICommand;

/// <summary>What an expiry pass did.</summary>
/// <param name="Plans">Plans closed.</param>
/// <param name="Tickets">Tickets expired.</param>
/// <param name="Summary">In words.</param>
public sealed record ExpiryOutcome(int Plans, int Tickets, string Summary);
