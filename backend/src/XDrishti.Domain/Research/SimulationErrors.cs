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

    /// <summary>The charge sheet a request names is not stored, or its id is a <c>test-</c> one (P3-03f F2).</summary>
    public const string CostSheetNotFoundCode = "simulation.cost_sheet_not_found";

    /// <summary>The first schedule of the charge sheet starts after the first day of the window (P3-03f F2).</summary>
    public const string CostSheetNotEffectiveCode = "simulation.cost_sheet_not_effective";

    /// <summary>The strategy id is not a built-in one (a located problem of the strategy factory, path <c>strategy_id</c>).</summary>
    public const string UnknownStrategyCode = "simulation.unknown_strategy";

    /// <summary>A parameter name the built-in strategy does not have (a located problem of the strategy factory, path <c>parameters.&lt;name&gt;</c>).</summary>
    public const string UnknownParameterCode = "simulation.unknown_parameter";

    /// <summary>A finite parameter value outside the range the built-in strategy declares for it (a located problem of the strategy factory, path <c>parameters.&lt;name&gt;</c>; ADR 0041 Amendment 2).</summary>
    public const string ParameterOutOfRangeCode = "simulation.parameter_out_of_range";

    /// <summary>The strategy exists but not in the version a run names (a located problem at path <c>strategy_version</c>; spec P3-02d, D8).</summary>
    public const string UnknownStrategyVersionCode = "simulation.unknown_strategy_version";

    /// <summary>No stored version has the hash a run names (a located problem at path <c>strategy_hash</c>; spec P3-02d, D8).</summary>
    public const string UnknownStrategyHashCode = "simulation.unknown_strategy_hash";

    /// <summary>The most problems a <see cref="StrategyInvalid"/> error lists; the rest are counted in its message.</summary>
    public const int MaxStrategyProblems = 20;

    /// <summary>Two equal epochs or a descending pair among the minutes of a walk or a run.</summary>
    public const string BarsNotAscendingCode = "simulation.bars_not_ascending";

    /// <summary>A stop level, trigger close, reward-to-risk or square-off minute the walker cannot use.</summary>
    public const string RulesInvalidCode = "simulation.rules_invalid";

    /// <summary>A candidate whose minute is not at its index in the list of bars, or does not end at its decision instant.</summary>
    public const string CandidateNotInBarsCode = "simulation.candidate_not_in_bars";

    /// <summary>The stored data of an instrument changed between the run and the second read for the trade cards (P3-04c, C2); the export is refused and nothing is written. The field of the error names the instrument and the first difference.</summary>
    public const string CardsStaleCode = "simulation.cards_stale";

    /// <summary>The instrument row of a member of the run is gone (a first difference named by <see cref="CardsStale"/>).</summary>
    public const string StaleInstrumentMissing = "instrument_missing";

    /// <summary>The hash of the stored bars differs from the one the run recorded.</summary>
    public const string StaleBars = "bars_changed";

    /// <summary>The applied corporate actions that change the bars differ from the ones the run recorded.</summary>
    public const string StaleAdjustments = "adjustments_changed";

    /// <summary>The tick of the instrument master differs from the one the run used.</summary>
    public const string StaleTick = "tick_changed";

    /// <summary>The recomputation for the trade cards did not give the recorded trade, or the recorded decision does not hold its conditions (P3-04d); the export is refused and nothing is written.</summary>
    public const string CardsMismatchCode = "simulation.cards_mismatch";

    /// <summary>The replay of <paramref name="symbol"/> does not reproduce a recorded trade; <paramref name="field"/> names the first field that differs (or the condition list that does not hold) and <paramref name="detail"/> says how. A conflict, like stale.</summary>
    public static Error CardsMismatch(string symbol, string field, string detail)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(detail);
        return Error.Conflict(CardsMismatchCode, "The recomputation of the trade for its card does not give the recorded trade, so the export is refused; the engine or the data changed since the run.") with
        {
            Fields = [new FieldError(symbol, detail, null, field)],
        };
    }

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

    /// <summary>The data of <paramref name="symbol"/> is no longer what the run read; <paramref name="difference"/> is one of the <c>Stale…</c> names and <paramref name="detail"/> a sentence for the console. A conflict: the request is fine, the data moved.</summary>
    public static Error CardsStale(string symbol, string difference, string detail)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        ArgumentNullException.ThrowIfNull(difference);
        ArgumentNullException.ThrowIfNull(detail);
        return Error.Conflict(CardsStaleCode, "The stored data changed since the run, so the trade cards would not match it; run the simulation again.") with
        {
            Fields = [new FieldError(symbol, detail, null, difference)],
        };
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

    /// <summary>The sheet is not stored (or is a <c>test-</c> id).</summary>
    public static Error CostSheetNotFound(string sheetId) =>
        Error.NotFound(CostSheetNotFoundCode, $"No charge sheet '{sheetId}' is stored; load one with scripts/cost-sheet.sh, or run gross.");

    /// <summary>The window begins before the first date the sheet vouches for: refused, never extrapolated.</summary>
    public static Error CostSheetNotEffective(string sheetId, DateOnly firstEffective, DateOnly from) =>
        Error.Validation(CostSheetNotEffectiveCode, $"The charge sheet '{sheetId}' starts on {firstEffective:yyyy-MM-dd}, after the first day of the window ({from:yyyy-MM-dd}).") with
        {
            Fields = [new FieldError("costs.sheet_id", $"has no rates before {firstEffective:yyyy-MM-dd}.", null, CostSheetNotEffectiveCode)],
        };

    public static readonly Error BasketNotFound = Error.NotFound(BasketNotFoundCode, "The basket does not exist, is a portfolio basket or is archived; a simulation runs over a manual basket that is not archived.");
}
