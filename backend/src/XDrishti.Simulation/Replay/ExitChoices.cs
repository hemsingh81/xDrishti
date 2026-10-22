using XDrishti.Domain.Research;
using XDrishti.Strategies.Model;

namespace XDrishti.Simulation.Replay;

/// <summary>
/// Turns the <see cref="ExitChoice"/> of a verb into the exits and overlays of a definition (P4-05, D1): the exits of the built-in are replaced, its entry, stop and time rules are not, so every candidate sees the same entries.
/// </summary>
internal static class ExitChoices
{
    /// <summary>The definition with its exits replaced by <paramref name="candidates"/> and its overlays by the time stop of <paramref name="choice"/> (or none), on both sides.</summary>
    public static StrategyDefinition With(StrategyDefinition definition, IReadOnlyList<ExitCandidate> candidates, ExitChoice choice)
    {
        var exits = candidates.Select(Spec).ToList();
        var overlays = choice.TimeStop is { } stop ? new List<OverlaySpec> { Overlay(stop) } : null;
        return definition with
        {
            Buy = definition.Buy is null ? null : definition.Buy with { Exits = exits, Overlays = overlays },
            Sell = definition.Sell is null ? null : definition.Sell with { Exits = exits, Overlays = overlays },
        };
    }

    private static ExitSpec Spec(ExitCandidate candidate) =>
        new(PolicyOf(candidate.Policy), [.. candidate.Parameters.Select(p => new ExitParameter(p.Name, [.. p.Values.Select(Quantity.Of)]))]);

    private static OverlaySpec Overlay(TimeStopChoice stop)
    {
        var parameters = new List<ExitParameter> { new("min_progress_rr", [Quantity.Of(stop.MinProgressRr)]) };
        if (stop.IntradayBars > 0)
        {
            parameters.Add(new ExitParameter("intraday_bars", [Quantity.Of(stop.IntradayBars)]));
        }

        if (stop.SwingSessions > 0)
        {
            parameters.Add(new ExitParameter("swing_sessions", [Quantity.Of(stop.SwingSessions)]));
        }

        return new OverlaySpec(OverlayKind.TimeStop, parameters);
    }

    private static ExitPolicy PolicyOf(string policy) => policy switch
    {
        "FIXED_RR" => ExitPolicy.FixedRr,
        "PARTIAL_BE" => ExitPolicy.PartialBe,
        "PARTIAL_TRAIL" => ExitPolicy.PartialTrail,
        "ATR_TRAIL" => ExitPolicy.AtrTrail,
        "STRUCTURE_TRAIL" => ExitPolicy.StructureTrail,
        "EMA_TRAIL" => ExitPolicy.EmaTrail,
        "PYRAMID" => ExitPolicy.Pyramid,
        _ => throw new ArgumentOutOfRangeException(nameof(policy), policy, "Not an exit policy of ExitFlags."),
    };
}
