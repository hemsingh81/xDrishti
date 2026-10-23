namespace XDrishti.Domain.Planning;

/// <summary>The stable kinds of the suggestions the rules produce (spec P7-09, ADR 0092). Stored as they are; never rename one.</summary>
public static class SuggestionKinds
{
    /// <summary>The market gate had no data: the plan was not treated as a normal day. Subject: <c>plan</c>.</summary>
    public const string MarketGateUnavailable = "market_gate_unavailable";

    /// <summary>The plan has no primary ticket. Subject: <c>plan</c>.</summary>
    public const string NoPrimaryTickets = "no_primary_tickets";

    /// <summary>Tickets were held because one input was unavailable (for example cost). Subject: the input's code.</summary>
    public const string TicketsHeldUnavailableInput = "tickets_held_unavailable_input";

    /// <summary>The cell statistics the plan used are old. Subject: <c>plan</c>.</summary>
    public const string CellStatsStale = "cell_stats_stale";
}
