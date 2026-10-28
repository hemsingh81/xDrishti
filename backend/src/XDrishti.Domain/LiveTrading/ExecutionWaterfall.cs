using XDrishti.Domain.Research;

namespace XDrishti.Domain.LiveTrading;

/// <summary>
/// The exact waterfall of one graded round trip (spec P11-02 definitions), in the plan's R (rho = |plan entry - plan stop| as the fill grade): plan result (gross, on plan prices) - entry slippage (d x (entry VWAP - plan entry)) - exit difference
/// (d x (plan exit - exit VWAP)) = actual gross result (d x (exit VWAP - entry VWAP)); then the charges line (live charges minus the plan's own charges, which are zero for a gross-only plan grade) gives the actual net result. Without live charges
/// the result is labelled gross and no net is shown; without a stop no R is shown and the rupees are the result. Decimal arithmetic, so the rows add up exactly.
/// </summary>
public static class ExecutionWaterfall
{
    public const string Gross = "gross";
    public const string Net = "net";

    private const double Tolerance = 1e-9;

    /// <summary>The waterfall. <paramref name="planChargesR"/> is the plan's own charges in R (0 when the plan grade is gross-only); <paramref name="liveCharges"/> is the sum of the recorded charges of every fill of the trip, null when any is missing.</summary>
    public static WaterfallResult Build(Side side, decimal planEntry, decimal planExit, decimal? planStop, decimal entryVwap, decimal exitVwap, long qty, decimal? liveCharges, double planChargesR)
    {
        var d = side == Side.Buy ? 1m : -1m;
        var shares = (decimal)qty;
        var rho = planStop is { } stop && Math.Abs(planEntry - stop) > 0 ? Math.Abs(planEntry - stop) : (decimal?)null;
        double? R(decimal rupees) => rho is { } r ? (double)(rupees / (r * shares)) : null;
        var plan = d * (planExit - planEntry) * shares;
        var entrySlip = d * (entryVwap - planEntry) * shares;
        var exitDiff = d * (planExit - exitVwap) * shares;
        var actual = d * (exitVwap - entryVwap) * shares;
        var notes = new List<string>();
        var rows = new List<WaterfallRow>
        {
            new("Plan result, gross (on plan prices)", "start", R(plan), plan),
            new("Entry slippage", "minus", R(entrySlip), entrySlip),
            new("Exit difference", "minus", R(exitDiff), exitDiff),
            new("Actual result, gross", "total", R(actual), actual),
        };
        var balanced = Math.Abs((double)(plan - entrySlip - exitDiff - actual)) <= Tolerance;
        var basis = Gross;
        if (rho is null)
        {
            notes.Add("The plan grade has no stop, so no R is shown; the rupee result is.");
        }

        if (liveCharges is { } charges)
        {
            var planCharges = rho is { } r ? (decimal)planChargesR * r * shares : 0m;
            var difference = charges - planCharges;
            var net = actual - difference;
            rows.Add(new WaterfallRow("Charges (live minus plan)", "minus", R(difference), difference));
            rows.Add(new WaterfallRow("Actual result, net", "total", R(net), net));
            basis = Net;
        }
        else
        {
            notes.Add("Charges are not recorded for every fill of this trip, so the result is gross and no net is shown.");
        }

        return new WaterfallResult(rows, basis, balanced, notes);
    }

    /// <summary>Whether a set of rows adds up: the first row minus the minus rows up to each total equals that total. Used to prove the identity (and to show that a doctored price breaks it).</summary>
    public static bool Adds(IReadOnlyList<WaterfallRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0 || rows[0].Op != "start")
        {
            return false;
        }

        var running = rows[0].Rupees;
        foreach (var row in rows.Skip(1))
        {
            if (row.Op == "minus")
            {
                running -= row.Rupees;
            }
            else if (row.Op == "total")
            {
                if (Math.Abs((double)(running - row.Rupees)) > Tolerance)
                {
                    return false;
                }

                running = row.Rupees;
            }
        }

        return true;
    }
}
