using XDrishti.Domain.Common;

namespace XDrishti.Domain.Reporting;

/// <summary>The typed refusals of the report engine, with the problem-details codes the endpoints will use (P8-00 contract).</summary>
public static class ReportErrors
{
    /// <summary>The input is not usable.</summary>
    public static Error Invalid(string message) => Error.Validation("reports.invalid", message);

    /// <summary>A trade names a run that is not in the set.</summary>
    public static Error UnknownRun(string runKey) => Error.NotFound("reports.not_found", $"Run '{runKey}' is not in the set of runs.");

    /// <summary>Two runs of one strategy, universe, overlapping window and the same labels: the same population twice.</summary>
    public static Error RunOverlap(string first, string second, string strategy) =>
        Error.Conflict("reports.run_overlap", $"Runs '{first}' and '{second}' both hold strategy '{strategy}' for the same universe, an overlapping window and the same style and exit; they would count the same trades twice. Keep one.");

    /// <summary>One signal in two runs under the same labels.</summary>
    public static Error TradeOverlap(string tradeKey, string first, string second) =>
        Error.Conflict("reports.run_overlap", $"Trade '{tradeKey}' is in both run '{first}' and run '{second}' with the same style and exit; it would be counted twice. Keep one run.");

    /// <summary>Runs with charges and runs without in one set.</summary>
    public static Error MixedBasis(IReadOnlyList<string> costed, IReadOnlyList<string> gross) =>
        Error.Conflict("reports.mixed_basis", $"The runs mix net ({string.Join(", ", costed)}) and gross ({string.Join(", ", gross)}) results; a report is one or the other. Keep one kind.");

    /// <summary>A net report was asked for before the owner confirmed the charge sheet.</summary>
    public static Error NetNotConfirmed() =>
        Error.Validation("reports.net_not_confirmed", "A net report is not allowed until the owner has confirmed the stored charge sheet as their own; the report is gross.");

    /// <summary>A net report was asked for, but the runs carry no net R.</summary>
    public static Error NetUnavailable() => Error.Validation("reports.net_unavailable", "The runs carry no net R, so no net report can be made.");

    /// <summary>A dimension for which no data exists.</summary>
    public static Error DimensionUnavailable(ReportDimension dimension) =>
        Error.Validation("reports.dimension_unavailable", dimension switch
        {
            ReportDimension.Regime => "Regime is unavailable: no market context is stored, so no trade has a regime.",
            ReportDimension.Account => "Account is unavailable: a simulated trade has no account.",
            _ => $"{dimension} is not unavailable.",
        });

    /// <summary>A grouping that would add one signal twice because it does not split by a label that differs between the runs.</summary>
    public static Error DoubleCount(IReadOnlyList<ReportDimension> required) =>
        Error.Validation("reports.double_count", $"The runs hold the same signals under different {string.Join(" and ", required.Select(d => d.ToString().ToLowerInvariant()))} labels; group by {string.Join(" and ", required)} as well, or the same trade is counted once per label.");
}
