namespace XDrishti.Domain.Learning;

/// <summary>
/// The one-variable rule (doc 09 section 5, doc 16 <c>one_variable_only</c>, spec P6-10, ADR 0076): a candidate run may differ from its baseline in exactly the named knob and nothing else, checked against what the two stored runs say about their
/// strategy and fill rules. A parameter knob: that one effective parameter differs, from and to the stated values; the exits, the rest of the definition and the fill rules are equal. An exit knob: only the exits differ, and each stated value is named in the stored exits of its run. A slippage knob: only the
/// <c>slippage.*</c> fill fields differ (judged by <see cref="FillCostComparison"/>; its stated values are labels only, as in P6-11, because no single stored number is "the slippage"). The data, window, universe and cost sheet are compared by the experiment (P6-11) that judges the pair. Pure.
/// </summary>
public static class OneVariableRule
{
    /// <summary>Why the baseline cannot carry this knob; empty when it can. A parameter knob must name a parameter of the run and its stated baseline value must be the stored one.</summary>
    public static IReadOnlyList<string> BaselineProblems(ExperimentKnob knob, RunDefinition baseline)
    {
        ArgumentNullException.ThrowIfNull(knob);
        ArgumentNullException.ThrowIfNull(baseline);
        if (knob.Kind == ExperimentKnobKind.Exit)
        {
            return NamesIn(baseline.Exits, knob.BaselineValue) ? [] : [$"the exits of the baseline run do not name '{knob.BaselineValue}'"];
        }

        if (knob.Kind != ExperimentKnobKind.Parameter)
        {
            return [];
        }

        if (!baseline.Parameters.TryGetValue(knob.Name, out var stored))
        {
            return [$"the baseline run has no parameter '{knob.Name}'"];
        }

        return ExperimentKnob.Number(knob.BaselineValue) == Normal(stored)
            ? []
            : [$"the baseline run used {stored.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} for '{knob.Name}', not {knob.BaselineValue}"];
    }

    /// <summary>Why the candidate is not the baseline with this one knob changed; empty when it is.</summary>
    public static IReadOnlyList<string> Problems(ExperimentKnob knob, RunDefinition baseline, RunDefinition candidate)
    {
        ArgumentNullException.ThrowIfNull(knob);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(candidate);
        var problems = new List<string>();
        if (!string.Equals(baseline.StrategyId, candidate.StrategyId, StringComparison.Ordinal))
        {
            problems.Add("the strategy differs");
        }

        if (!string.Equals(baseline.Rest, candidate.Rest, StringComparison.Ordinal))
        {
            problems.Add("the strategy definition differs outside its parameters and exits");
        }

        var changed = ChangedParameters(baseline, candidate);
        var exitsDiffer = !string.Equals(baseline.Exits, candidate.Exits, StringComparison.Ordinal);
        switch (knob.Kind)
        {
            case ExperimentKnobKind.Parameter:
                Parameter(knob, baseline, candidate, changed, problems);
                if (exitsDiffer)
                {
                    problems.Add("the exits differ as well as the parameter");
                }

                if (!string.Equals(baseline.Fills, candidate.Fills, StringComparison.Ordinal))
                {
                    problems.Add("the fill rules differ as well as the parameter");
                }

                break;
            case ExperimentKnobKind.Exit:
                if (changed.Count > 0)
                {
                    problems.Add($"parameters differ ({string.Join(", ", changed)}) as well as the exits");
                }

                if (!exitsDiffer)
                {
                    problems.Add("the exits are the same: the knob did not change");
                }

                if (!NamesIn(baseline.Exits, knob.BaselineValue))
                {
                    problems.Add($"the exits of the baseline run do not name '{knob.BaselineValue}'");
                }

                if (!NamesIn(candidate.Exits, knob.CandidateValue))
                {
                    problems.Add($"the exits of the candidate run do not name '{knob.CandidateValue}'");
                }

                if (!string.Equals(baseline.Fills, candidate.Fills, StringComparison.Ordinal))
                {
                    problems.Add("the fill rules differ as well as the exits");
                }

                break;
            case ExperimentKnobKind.Slippage:
                if (changed.Count > 0)
                {
                    problems.Add($"parameters differ ({string.Join(", ", changed)}) as well as the slippage");
                }

                if (exitsDiffer)
                {
                    problems.Add("the exits differ as well as the slippage");
                }

                var fills = FillCostComparison.Compare(baseline.Fills, candidate.Fills);
                if (fills.IsFailure)
                {
                    problems.Add(fills.Error.Message);
                }

                break;
            default:
                problems.Add("the kind of knob is unknown");
                break;
        }

        return problems;
    }

    private static void Parameter(ExperimentKnob knob, RunDefinition baseline, RunDefinition candidate, List<string> changed, List<string> problems)
    {
        if (changed.Count == 0)
        {
            problems.Add("no parameter differs: the knob did not change");
        }
        else if (changed.Count > 1 || !string.Equals(changed[0], knob.Name, StringComparison.Ordinal))
        {
            problems.Add($"{changed.Count} parameters differ ({string.Join(", ", changed)}); a hypothesis changes only '{knob.Name}'");
        }
        else
        {
            if (!baseline.Parameters.TryGetValue(knob.Name, out var baselineValue))
            {
                problems.Add($"the baseline run has no parameter '{knob.Name}'");
            }
            else if (ExperimentKnob.Number(knob.BaselineValue) != Normal(baselineValue))
            {
                problems.Add($"the baseline run's '{knob.Name}' is not {knob.BaselineValue}");
            }

            if (!candidate.Parameters.TryGetValue(knob.Name, out var candidateValue))
            {
                problems.Add($"the candidate run has no parameter '{knob.Name}'");
            }
            else if (ExperimentKnob.Number(knob.CandidateValue) != Normal(candidateValue))
            {
                problems.Add($"the candidate run's '{knob.Name}' is not {knob.CandidateValue}");
            }
        }
    }

    // A parameter present in one run only counts as changed.
    private static List<string> ChangedParameters(RunDefinition a, RunDefinition b) =>
        [.. a.Parameters.Keys.Concat(b.Parameters.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Where(k => !(a.Parameters.TryGetValue(k, out var x) && b.Parameters.TryGetValue(k, out var y) && x == y))];

    // An exit value is the policy's name as the stored exits spell it (compared without case).
    private static bool NamesIn(string exits, string value) => exits.Contains(value, StringComparison.OrdinalIgnoreCase);

    private static double Normal(double v) => v == 0 ? 0 : v;
}
