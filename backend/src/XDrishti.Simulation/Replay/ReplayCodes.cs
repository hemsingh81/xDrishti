namespace XDrishti.Simulation.Replay;

/// <summary>The stable codes of what the strategy factory of the replay refuses by name (spec P3-04, Scope 2 and Hand-over). A program switches on the code and the path; the messages are English and may change.</summary>
public static class ReplayCodes
{
    /// <summary>The strategy id is not a built-in one. Path <c>strategy_id</c>.</summary>
    public const string UnknownStrategy = "simulation.unknown_strategy";

    /// <summary>A parameter name the built-in strategy does not have. Path <c>parameters.&lt;name&gt;</c>.</summary>
    public const string UnknownParameter = "simulation.unknown_parameter";
}
