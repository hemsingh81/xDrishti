namespace XDrishti.Domain.Planning;

/// <summary>What is stated about a ticket move (spec P7-03).</summary>
/// <param name="Actor">Who makes the move.</param>
/// <param name="Group">The list the ticket is on.</param>
public sealed record TicketFacts(MoveActor Actor, TicketGroup Group);
