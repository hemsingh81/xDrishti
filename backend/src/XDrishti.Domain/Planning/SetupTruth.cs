namespace XDrishti.Domain.Planning;

/// <summary>The three values of a part of a setup (Kleene, ADR 0034, used by the ticket builder, ADR 0088). Unknown is never True.</summary>
public enum SetupTruth
{
    /// <summary>The part does not hold.</summary>
    False = 0,

    /// <summary>The part holds.</summary>
    True = 1,

    /// <summary>The part cannot be decided: it is knowable only tomorrow, or its data is not ready.</summary>
    Unknown = 2,
}
