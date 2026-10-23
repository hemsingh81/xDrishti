using XDrishti.Domain.Common;

namespace XDrishti.Application.Learning;

/// <summary>The expected failures of the hypothesis loop (P6-10).</summary>
public static class HypothesisErrors
{
    /// <summary>No hypothesis with this id.</summary>
    public const string NotFoundCode = "hypotheses.not_found";

    /// <summary>The move is not legal or lacks a fact the table requires.</summary>
    public const string MoveRefusedCode = "hypotheses.move_refused";

    /// <summary>The candidate run is not the baseline with the one knob changed.</summary>
    public const string NotOneVariableCode = "hypotheses.not_one_variable";

    /// <summary>The hypothesis moved while the request was handled.</summary>
    public const string StaleCode = "hypotheses.stale";

    /// <summary>The open limit is reached.</summary>
    public const string OpenLimitCode = "hypotheses.open_limit";

    /// <summary>No hypothesis has this id.</summary>
    public static Error NotFound(long id) => Error.NotFound(NotFoundCode, $"No hypothesis {id} is recorded.");

    /// <summary>A move the table refuses.</summary>
    public static Error MoveRefused(IReadOnlyList<string> reasons) => Error.Conflict(MoveRefusedCode, string.Join(" ", reasons));

    /// <summary>The pair of runs differs in more than the knob, or not in the knob.</summary>
    public static Error NotOneVariable(IReadOnlyList<string> problems) =>
        Error.Conflict(NotOneVariableCode, "The candidate run is not the baseline with exactly the one knob changed: " + string.Join("; ", problems) + ".");

    /// <summary>Another move was written first.</summary>
    public static Error Stale() => Error.Conflict(StaleCode, "The hypothesis moved while this was handled; read it again.");

    /// <summary>The open limit.</summary>
    public static Error OpenLimit(IReadOnlyList<string> reasons) => Error.Conflict(OpenLimitCode, string.Join(" ", reasons));
}
