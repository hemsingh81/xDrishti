using XDrishti.Domain.Common;

namespace XDrishti.Domain.Research;

/// <summary>Stable error codes of the simulator (P3-03b, P3-04, P3-04a). Every <c>simulation.*</c> code is declared here once; other simulator slices add their own codes to this class.</summary>
public static class SimulationErrors
{
    public const string SlippageInvalidCode = "simulation.slippage_invalid";
    public const string FillRulesInvalidCode = "simulation.fill_rules_invalid";
    public const string TickSizeInvalidCode = "simulation.tick_size_invalid";

    /// <summary>The request of a run is not valid (P3-04a D1); the fields name what is wrong.</summary>
    public const string InvalidCode = "simulation.invalid";

    /// <summary>The strategy of a run is refused before any bar is read (P3-04a D4); the fields carry the path and the code of each problem.</summary>
    public const string StrategyInvalidCode = "simulation.strategy_invalid";

    /// <summary>The basket of a run does not exist, is a portfolio basket or is archived (P3-04a D2).</summary>
    public const string BasketNotFoundCode = "simulation.basket_not_found";

    /// <summary>The strategy id is not a built-in one (a located problem of the strategy factory, path <c>strategy_id</c>).</summary>
    public const string UnknownStrategyCode = "simulation.unknown_strategy";

    /// <summary>A parameter name the built-in strategy does not have (a located problem of the strategy factory, path <c>parameters.&lt;name&gt;</c>).</summary>
    public const string UnknownParameterCode = "simulation.unknown_parameter";

    /// <summary>The most problems a <see cref="StrategyInvalid"/> error lists; the rest are counted in its message.</summary>
    public const int MaxStrategyProblems = 20;

    /// <summary>Two equal epochs or a descending pair among the minutes of a walk or a run.</summary>
    public const string BarsNotAscendingCode = "simulation.bars_not_ascending";

    /// <summary>A stop level, trigger close, reward-to-risk or square-off minute the walker cannot use.</summary>
    public const string RulesInvalidCode = "simulation.rules_invalid";

    /// <summary>A candidate whose minute is not at its index in the list of bars, or does not end at its decision instant.</summary>
    public const string CandidateNotInBarsCode = "simulation.candidate_not_in_bars";

    public static Error SlippageInvalid(string field, string reason) =>
        Error.Validation(SlippageInvalidCode, $"The slippage setting '{field}' is invalid: {reason}.");

    public static Error FillRulesInvalid(string field, string reason) =>
        Error.Validation(FillRulesInvalidCode, $"The fill rule '{field}' is invalid: {reason}.");

    public static Error TickSizeInvalid(string reason) =>
        Error.Validation(TickSizeInvalidCode, $"The tick size is invalid: {reason}.");

    /// <summary>The request is not valid: one entry per field.</summary>
    public static Error Invalid(IReadOnlyList<FieldError> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return Error.Validation(InvalidCode, "Some values of the simulation request are not valid.") with { Fields = fields };
    }

    /// <summary>The strategy is refused: the first <see cref="MaxStrategyProblems"/> problems with their paths and codes, the number of the others in the message.</summary>
    public static Error StrategyInvalid(IReadOnlyList<FieldError> problems)
    {
        ArgumentNullException.ThrowIfNull(problems);
        var more = problems.Count - MaxStrategyProblems;
        var message = more > 0
            ? $"The strategy cannot be run: {problems.Count} problems, the first {MaxStrategyProblems} are listed."
            : "The strategy cannot be run.";
        return Error.Validation(StrategyInvalidCode, message) with { Fields = [.. problems.Take(MaxStrategyProblems)] };
    }

    public static readonly Error BasketNotFound = Error.NotFound(BasketNotFoundCode, "The basket does not exist, is a portfolio basket or is archived; a simulation runs over a manual basket that is not archived.");
}
