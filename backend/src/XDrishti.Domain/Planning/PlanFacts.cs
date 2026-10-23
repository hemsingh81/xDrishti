namespace XDrishti.Domain.Planning;

/// <summary>What the tickets of a plan say, counted from their current states (spec P7-03). Only Primary and Reserve tickets count.</summary>
/// <param name="Open">Tickets nobody has decided on (Proposed or Modified).</param>
/// <param name="Approved">Tickets in Approved.</param>
/// <param name="Armed">Tickets in Armed.</param>
/// <param name="Rejected">Tickets the owner rejected.</param>
public sealed record PlanFacts(int Open, int Approved, int Armed, int Rejected)
{
    /// <summary>Counts the states of the actionable tickets.</summary>
    public static PlanFacts Of(IEnumerable<TicketState> states)
    {
        ArgumentNullException.ThrowIfNull(states);
        var list = states.ToList();
        return new PlanFacts(
            list.Count(s => s is TicketState.Proposed or TicketState.Modified),
            list.Count(s => s == TicketState.Approved),
            list.Count(s => s == TicketState.Armed),
            list.Count(s => s == TicketState.Rejected));
    }
}
