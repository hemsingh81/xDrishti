using XDrishti.Domain.Common;

namespace XDrishti.Domain.Research;

/// <summary>Stable error codes of the simulator (P3-03b). Other simulator slices add their own codes to this class.</summary>
public static class SimulationErrors
{
    public const string SlippageInvalidCode = "simulation.slippage_invalid";
    public const string FillRulesInvalidCode = "simulation.fill_rules_invalid";
    public const string TickSizeInvalidCode = "simulation.tick_size_invalid";

    public static Error SlippageInvalid(string field, string reason) =>
        Error.Validation(SlippageInvalidCode, $"The slippage setting '{field}' is invalid: {reason}.");

    public static Error FillRulesInvalid(string field, string reason) =>
        Error.Validation(FillRulesInvalidCode, $"The fill rule '{field}' is invalid: {reason}.");

    public static Error TickSizeInvalid(string reason) =>
        Error.Validation(TickSizeInvalidCode, $"The tick size is invalid: {reason}.");
}
