using System.Globalization;
using XDrishti.Domain.Confidence;

namespace XDrishti.Domain.Learning;

/// <summary>
/// The complexity ladder (CF-10 part 10b, ADR 0215, decision D2): base rate, shrunk cell rate, L2 logistic with at most 8 features, gradient-boosted trees. A rung above the base rate is earned only if it beats the rung below with probability at least
/// <see cref="LadderOptions.MinProbability"/> on the paired day-cluster bootstrap (part 10c); that probability is an input here, and a missing one refuses the rung. A logistic model is also capped by the events-per-variable rule (<see cref="EventsPerVariable"/>),
/// trees need at least <see cref="LadderOptions.MinTreeEvents"/> events. Pure; it reports and decides nothing.
/// </summary>
public static class ComplexityLadder
{
    /// <summary>Assesses one scorer.</summary>
    /// <param name="scorer">The name in the report.</param>
    /// <param name="rung">The rung it sits on.</param>
    /// <param name="events">The smaller class of its training rows.</param>
    /// <param name="featureCount">The features it reads.</param>
    /// <param name="probabilityBeatsBelow">The probability that it beats the rung below (paired bootstrap), or null when not available.</param>
    /// <param name="options">The settings.</param>
    /// <param name="structuralReason">A fact about the model that keeps it off its rung whatever the numbers (null: none), listed after the numeric reasons.</param>
    public static LadderAssessment Assess(string scorer, LadderRung rung, int events, int featureCount, double? probabilityBeatsBelow, LadderOptions options, string? structuralReason = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scorer);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegative(events);
        ArgumentOutOfRangeException.ThrowIfNegative(featureCount);
        var c = CultureInfo.InvariantCulture;
        var reasons = new List<string>();
        if (rung != LadderRung.BaseRate)
        {
            var below = Name(rung - 1);
            if (probabilityBeatsBelow is not { } p || !double.IsFinite(p))
            {
                reasons.Add(string.Create(c, $"the probability of beating the {below} is not available (a missing figure is not a pass)"));
            }
            else if (p < options.MinProbability)
            {
                reasons.Add(string.Create(c, $"the probability of beating the {below} is {p:F4}, below {options.MinProbability:F2}"));
            }
        }

        if (rung == LadderRung.Logistic)
        {
            var cap = EventsPerVariable.Cap(events, options.Epv, options.MaxLogisticFeatures);
            if (featureCount > cap)
            {
                reasons.Add(string.Create(c, $"{featureCount} features exceed the cap of {cap} ({events} events at {options.Epv} per variable, at most {options.MaxLogisticFeatures})"));
            }
        }

        if (rung == LadderRung.Trees && events < options.MinTreeEvents)
        {
            reasons.Add(string.Create(c, $"{events} events, {options.MinTreeEvents} needed"));
        }

        if (structuralReason is not null)
        {
            reasons.Add(structuralReason);
        }

        var valid = probabilityBeatsBelow is { } v && double.IsFinite(v) ? probabilityBeatsBelow : null;
        return new LadderAssessment(scorer, rung, events, featureCount, valid, reasons.Count == 0, reasons.AsReadOnly());
    }

    private static string Name(LadderRung rung) => rung switch
    {
        LadderRung.BaseRate => "base rate",
        LadderRung.CellRate => "cell rate",
        LadderRung.Logistic => "logistic model",
        _ => "trees",
    };
}
