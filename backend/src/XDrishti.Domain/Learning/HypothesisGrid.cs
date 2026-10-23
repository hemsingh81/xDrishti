using System.Globalization;

namespace XDrishti.Domain.Learning;

/// <summary>
/// The deterministic grid source of the hypothesis loop (spec P6-10, ADR 0076): values around the current value of one parameter, <c>steps</c> each side at <c>step_pct</c> percent of the current value, so at most 2 x steps values, none equal to the
/// current one, none that changes its sign. A coarse grid on purpose (doc 09 section 6 rule 3). No random number, no model, no LLM. Pure.
/// </summary>
/// <param name="StepPct">Percent of the current value per step, 1 to 50.</param>
/// <param name="Steps">Steps each side, 1 to 4; <c>Steps x StepPct</c> must stay under 100 so that no value changes sign.</param>
public sealed record HypothesisGrid(double StepPct = 10, int Steps = 2)
{
    /// <summary>The problems of the grid options, empty when usable.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (!double.IsFinite(StepPct) || StepPct is < 1 or > 50)
        {
            problems.Add("the grid step is 1 to 50 percent");
        }

        if (Steps is < 1 or > 4)
        {
            problems.Add("the grid steps are 1 to 4 each side");
        }
        else if (double.IsFinite(StepPct) && StepPct * Steps >= 100)
        {
            problems.Add("steps x step percent must be under 100");
        }

        return problems;
    }

    /// <summary>The grid values around <paramref name="current"/> in ascending order, as text with six significant digits; empty when the current value is zero or not finite, or the options are unusable.</summary>
    public IReadOnlyList<string> Around(double current)
    {
        if (Problems().Count > 0 || !double.IsFinite(current) || current == 0)
        {
            return [];
        }

        var values = new SortedSet<double>();
        for (var k = 1; k <= Steps; k++)
        {
            foreach (var factor in new[] { 1 - (k * StepPct / 100), 1 + (k * StepPct / 100) })
            {
                var v = double.Parse((current * factor).ToString("G6", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                if (v != current && v != 0)
                {
                    values.Add(v);
                }
            }
        }

        return [.. values.Select(v => v.ToString("R", CultureInfo.InvariantCulture))];
    }

    /// <summary>Whether <paramref name="value"/> is one of the grid values around <paramref name="current"/> (compared as numbers).</summary>
    public bool Contains(double current, string value) =>
        ExperimentKnob.Number(value) is { } n && Around(current).Any(g => ExperimentKnob.Number(g) == n);
}
