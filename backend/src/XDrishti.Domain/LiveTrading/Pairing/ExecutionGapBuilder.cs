namespace XDrishti.Domain.LiveTrading;

/// <summary>What kind of ticket a gap input is: a closed round trip with a waterfall, a missed ticket with a known cause, or a missed ticket whose cause is unknown (no order evidence).</summary>
public enum GapKind
{
    Trip,
    Missed,
    MissedUnknown,
}

/// <summary>One ticket for the execution gap, in R on the plan's rho. A trip carries its waterfall figures; a missed ticket carries only the plan R. <see cref="ChargesR"/> and <see cref="ActualNetR"/> are null when the trip's charges are not all recorded.</summary>
public sealed record GapTicket(string Group, GapKind Kind, double PlanR, double EntryR, double ExitR, double? ChargesR, double ActualGrossR, double? ActualNetR);

/// <summary>The sums of one group: plan - missed - missed unknown - entry - exit - charges = result. When any trip lacks recorded charges, <see cref="Basis"/> is gross, the charges sum is null and the result is the gross one; gross and net are never mixed.</summary>
public sealed record GapSums(string Group, int N, int Trips, double Plan, double Missed, double MissedUnknown, double Entry, double Exit, double? Charges, double Result, string Basis)
{
    /// <summary>The identity of the group, to a tolerance of rounding.</summary>
    public bool Balanced => Math.Abs(Plan - Missed - MissedUnknown - Entry - Exit - (Charges ?? 0) - Result) < 1e-9;
}

/// <summary>Builds the group sums of the execution gap (spec P11-03 criterion 8).</summary>
public static class ExecutionGapBuilder
{
    public static GapSums Sum(string group, IReadOnlyList<GapTicket> tickets)
    {
        ArgumentNullException.ThrowIfNull(tickets);
        var trips = tickets.Where(t => t.Kind == GapKind.Trip).ToList();
        var net = trips.Count > 0 && trips.All(t => t.ChargesR is not null && t.ActualNetR is not null);
        return new GapSums(
            group,
            tickets.Count,
            trips.Count,
            tickets.Sum(t => t.PlanR),
            tickets.Where(t => t.Kind == GapKind.Missed).Sum(t => t.PlanR),
            tickets.Where(t => t.Kind == GapKind.MissedUnknown).Sum(t => t.PlanR),
            trips.Sum(t => t.EntryR),
            trips.Sum(t => t.ExitR),
            net ? trips.Sum(t => t.ChargesR!.Value) : null,
            net ? trips.Sum(t => t.ActualNetR!.Value) : trips.Sum(t => t.ActualGrossR),
            net ? "net" : "gross");
    }
}
