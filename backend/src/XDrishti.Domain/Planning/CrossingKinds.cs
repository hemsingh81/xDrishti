namespace XDrishti.Domain.Planning;

/// <summary>The two kinds of price crossing of a watched ticket (spec P7-20, ADR 0115): the indicative stop level and the target level. Stored codes; there is no third kind, and no entry alert.</summary>
public static class CrossingKinds
{
    /// <summary>The live price reached or passed the stop level.</summary>
    public const string Stop = "stop_crossed";

    /// <summary>The live price reached or passed the target level.</summary>
    public const string Target = "target_crossed";

    /// <summary>True for a stored kind.</summary>
    public static bool IsKnown(string? kind) => kind is Stop or Target;
}
