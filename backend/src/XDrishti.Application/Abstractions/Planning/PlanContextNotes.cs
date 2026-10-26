using XDrishti.Domain.Market.Context;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Abstractions.Planning;

/// <summary>The notes a plan context carries into the plan's basis (P7-09, ADR 0092). Kept short: the basis column holds 500 characters and the open-book flag and the gate flag must both survive the cut.</summary>
public static class PlanContextNotes
{
    /// <summary>The owner set the open book to assumed empty.</summary>
    public const string NoBookNote = PlanWording.OpenBookAssumedEmpty;

    /// <summary>The default: the open book is not known.</summary>
    public const string BookUnavailableNote = PlanWording.OpenBookUnavailableMarker + ": no primary tickets (set Planning:OpenBook:AssumedEmpty to allow).";

    /// <summary>There is no Trading account to size for.</summary>
    public const string NoAccountNote = "No Trading account to size for.";

    /// <summary>What the breadth in the market gate is (P3-01e): the owner's own tracked set, not the market. Appended after the flag lines, so a cut of the basis never takes a flag.</summary>
    public static string MarketBreadth(int? universe) => BreadthWording.Note(universe);
}
