using System.Globalization;
using XDrishti.Domain.Confidence;

namespace XDrishti.Domain.Lab;

/// <summary>
/// The plateau gate on parameters (spec CF-07, D4), read from the stored points of the in-sample sweep. For one parameter: the two points next to the declared value (by value order) each keep at least <see cref="PlateauOptions.Retention"/> of the base
/// net expectancy, the base is above 0, and the median over all valid points (the declared value included) is above 0. A parameter with fewer valid points than <see cref="PlateauOptions.MinPoints"/>, no base result, no point on one side of the declared value
/// or an unreadable point next to it is <c>inconclusive</c>, which is <see cref="NullStatus.Unavailable"/> and never a pass. The plateau centre (the value maximizing the mean of itself and its two neighbours) is reported, not applied. The gate passes when every swept
/// parameter passes. Pure.
/// </summary>
public static class PlateauGate
{
    public const string Plateau = "plateau";
    public const string Spike = "spike";
    public const string NoBaseEdge = "no_base_edge";
    public const string Inconclusive = "inconclusive";

    /// <summary>Slack for a ratio that is exactly the retention in exact arithmetic (0.14 over 0.20 is 0.70) but a hair under it in floating point.</summary>
    private const double Tolerance = 1e-9;

    /// <param name="parameter">The parameter name.</param>
    /// <param name="declared">The value the strategy declares.</param>
    /// <param name="declaredPoint">The base run (at the declared value).</param>
    /// <param name="swept">The sweep points of this parameter, in any order.</param>
    /// <param name="options">Retention and the fewest valid points.</param>
    public static PlateauResult Evaluate(string parameter, double declared, PlateauPoint declaredPoint, IReadOnlyList<PlateauPoint> swept, PlateauOptions options)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(declaredPoint);
        ArgumentNullException.ThrowIfNull(swept);
        ArgumentNullException.ThrowIfNull(options);
        var c = CultureInfo.InvariantCulture;
        var atDeclared = declaredPoint with { Value = declared };
        List<PlateauPoint> all = [.. swept.Append(atDeclared).OrderBy(p => p.Value)];
        var at = all.IndexOf(atDeclared);
        var valid = all.Where(p => p.IsValid).ToList();
        var values = valid.Select(p => p.MeanR!.Value).Order().ToList();
        double? median = values.Count == 0 ? null : values.Count % 2 == 1 ? values[values.Count / 2] : (values[(values.Count / 2) - 1] + values[values.Count / 2]) / 2;
        var (centre, smoothed) = Centre(all);
        var lower = at > 0 ? all[at - 1] : null;
        var upper = at < all.Count - 1 ? all[at + 1] : null;
        var baseMean = declaredPoint.IsValid ? declaredPoint.MeanR : null;

        PlateauResult Make(string reading, NullStatus status, string reason, double? lowerRatio = null, double? upperRatio = null) =>
            new(parameter, declared, all, valid.Count, baseMean, lower?.Value, lowerRatio, upper?.Value, upperRatio, median, centre, smoothed, reading, status, reason);

        if (baseMean is not { } b)
        {
            return Make(Inconclusive, NullStatus.Unavailable, "unavailable: the base run gave no result" + (declaredPoint.Error is { } e ? ": " + e : string.Empty));
        }

        if (valid.Count < options.MinPoints)
        {
            return Make(Inconclusive, NullStatus.Unavailable, string.Create(c, $"unavailable: {valid.Count} valid point(s) including the declared value; {options.MinPoints} are needed"));
        }

        if (lower is null || upper is null)
        {
            return Make(Inconclusive, NullStatus.Unavailable, "unavailable: the declared value has no swept point on " + (lower is null ? "its lower" : "its upper") + " side");
        }

        if (new[] { lower, upper }.FirstOrDefault(p => !p.IsValid) is { } side)
        {
            return Make(Inconclusive, NullStatus.Unavailable, string.Create(c, $"unavailable: the point next to the declared value ({side.Value:0.######}) gave no result") + (side.Error is { } err ? ": " + err : " (no trades)") + "; a plateau cannot be claimed");
        }

        if (b <= 0)
        {
            return Make(NoBaseEdge, NullStatus.Fail, string.Create(c, $"no base edge: the net expectancy at the declared value is {b:0.####} R, not above 0, so there is no edge for the parameter to hold"));
        }

        var lowerRatio = lower.MeanR!.Value / b;
        var upperRatio = upper.MeanR!.Value / b;
        var misses = new List<string>();
        if (lowerRatio < options.Retention - Tolerance)
        {
            misses.Add(string.Create(c, $"the point below ({lower.Value:0.######}) keeps {lowerRatio * 100:0.#} % of the base"));
        }

        if (upperRatio < options.Retention - Tolerance)
        {
            misses.Add(string.Create(c, $"the point above ({upper.Value:0.######}) keeps {upperRatio * 100:0.#} % of the base"));
        }

        if (!(median > 0))
        {
            misses.Add(string.Create(c, $"the median net expectancy over the valid points is {median:0.####} R, not above 0"));
        }

        return misses.Count == 0
            ? Make(Plateau, NullStatus.Pass, string.Create(c, $"plateau: the neighbours keep {lowerRatio * 100:0.#} % and {upperRatio * 100:0.#} % of the base (needs {options.Retention * 100:0.#} %) and the median is {median:0.####} R"), lowerRatio, upperRatio)
            : Make(Spike, NullStatus.Fail, "spike: " + string.Join("; ", misses) + string.Create(c, $" (needs {options.Retention * 100:0.#} % and a median above 0)"), lowerRatio, upperRatio);
    }

    /// <summary>The gate over all swept parameters: a fail if any parameter fails, otherwise unavailable if any is (or there is none), otherwise a pass.</summary>
    public static (NullStatus Status, string Reason) Overall(IReadOnlyList<PlateauResult> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (parameters.Count == 0)
        {
            return (NullStatus.Unavailable, "unavailable: the strategy declares no parameter to sweep");
        }

        var failed = parameters.Where(p => p.Status == NullStatus.Fail).Select(p => p.Parameter).ToList();
        if (failed.Count > 0)
        {
            return (NullStatus.Fail, "not a plateau: " + string.Join(", ", failed));
        }

        var open = parameters.Where(p => p.Status == NullStatus.Unavailable).Select(p => p.Parameter).ToList();
        return open.Count > 0
            ? (NullStatus.Unavailable, "unavailable: no reading for " + string.Join(", ", open))
            : (NullStatus.Pass, "every swept parameter sits on a plateau");
    }

    /// <summary>The value whose own mean and the means of the points next to it (by value order) average highest; only a valid point with two valid neighbours is a candidate, and a tie goes to the lower value.</summary>
    private static (double? Value, double? Mean) Centre(List<PlateauPoint> all)
    {
        double? bestValue = null;
        double? best = null;
        for (var i = 1; i < all.Count - 1; i++)
        {
            if (!all[i - 1].IsValid || !all[i].IsValid || !all[i + 1].IsValid)
            {
                continue;
            }

            var mean = (all[i - 1].MeanR!.Value + all[i].MeanR!.Value + all[i + 1].MeanR!.Value) / 3;
            if (best is null || mean > best)
            {
                best = mean;
                bestValue = all[i].Value;
            }
        }

        return (bestValue, best);
    }
}
