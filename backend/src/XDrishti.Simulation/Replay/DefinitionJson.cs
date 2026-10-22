using XDrishti.Strategies.Canonical;
using XDrishti.Strategies.Model;

namespace XDrishti.Simulation.Replay;

/// <summary>The canonical JSON of a strategy definition, as the run record keeps it: a call of <see cref="CanonicalStrategy"/> (P3-02c D8, ADR 0055), kept so the replay code names one place.</summary>
internal static class DefinitionJson
{
    /// <summary>The canonical text of the definition.</summary>
    public static string Write(StrategyDefinition definition) => CanonicalStrategy.Json(definition);
}
