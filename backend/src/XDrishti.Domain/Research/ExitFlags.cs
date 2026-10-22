using System.Globalization;

namespace XDrishti.Domain.Research;

/// <summary>
/// Reads the flags <c>--exit POLICY[:name=value[,name=value...]]</c> (repeatable; <c>a|b|c</c> lists candidate values) and <c>--time-stop name=value,...</c> (P4-05, D1). Pure text: it knows the policies and the names of their parameters of doc 08 and
/// which of them are lists of candidate values; the ranges are judged by the compile of the strategy. Every refusal names the flag and the value.
/// </summary>
public static class ExitFlags
{
    /// <summary>The most candidates a choice may expand to.</summary>
    public const int MaxCandidates = 16;

    private static readonly Dictionary<string, Policy> Policies = new(StringComparer.Ordinal)
    {
        ["FIXED_RR"] = new(["rr"], ["rr"], []),
        ["PARTIAL_BE"] = new(["book_pct", "t1_rr", "t2_rr"], ["t2_rr"], []),
        ["PARTIAL_TRAIL"] = new(["book_pct", "t1_rr", "trail_atr_mult", "atr_period"], [], []),
        ["ATR_TRAIL"] = new(["mult", "activate_at_rr", "atr_period"], ["mult"], []),
        ["STRUCTURE_TRAIL"] = new(["lookback_bars"], [], []),
        ["EMA_TRAIL"] = new(["length"], [], []),
        ["PYRAMID"] = new(["initial_pct", "add_at_rr", "add_pct", "max_adds", "trail_atr_mult", "atr_period"], [], ["add_at_rr", "add_pct"]),
    };

    private static readonly string[] TimeStopNames = ["min_progress_rr", "intraday_bars", "swing_sessions"];

    /// <summary>The policies the flags accept and the parameters of each, for <c>--list-strategies</c>: <c>NAME: p1, p2 (candidate lists with '|': ...)</c>.</summary>
    public static IReadOnlyList<string> Catalog { get; } = [.. Policies.Select(p =>
        p.Key + ": " + string.Join(", ", p.Value.Names)
        + (p.Value.Lists.Length + p.Value.Ladders.Length > 0
            ? " (" + (p.Value.Lists.Length > 0 ? "candidate lists with '|': " + string.Join(", ", p.Value.Lists) : string.Empty) + (p.Value.Ladders.Length > 0 ? "ladders with '|', not expanded: " + string.Join(", ", p.Value.Ladders) : string.Empty) + ")"
            : string.Empty))];

    /// <summary>Reads the flags. Errors are lines for the usage error; with errors the choice is null.</summary>
    /// <param name="exits">The values of <c>--exit</c>, in order.</param>
    /// <param name="timeStop">The value of <c>--time-stop</c>, or null.</param>
    /// <param name="errors">Where the refusals go.</param>
    public static ExitChoice? Parse(IReadOnlyList<string> exits, string? timeStop, List<string> errors)
    {
        ArgumentNullException.ThrowIfNull(exits);
        ArgumentNullException.ThrowIfNull(errors);
        var before = errors.Count;
        var candidates = new List<ExitCandidate>();
        foreach (var spec in exits)
        {
            candidates.AddRange(Expand(spec, errors));
        }

        var stop = timeStop is null ? null : ParseTimeStop(timeStop, errors);
        if (timeStop is not null && exits.Count == 0)
        {
            errors.Add("'--time-stop' needs at least one '--exit' (it is an overlay on the exits you name)");
        }

        if (errors.Count > before)
        {
            return null;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            if (!ids.Add(candidate.Id))
            {
                errors.Add("'--exit' names the candidate " + candidate.Id + " twice; two candidates with identical values cannot be compared");
            }
        }

        if (candidates.Count > MaxCandidates)
        {
            errors.Add("'--exit' expands to " + candidates.Count.ToString(CultureInfo.InvariantCulture) + " candidates; the most is " + MaxCandidates.ToString(CultureInfo.InvariantCulture));
        }

        return errors.Count > before || candidates.Count == 0 ? null : new ExitChoice(candidates.AsReadOnly(), stop);
    }

    private static List<ExitCandidate> Expand(string spec, List<string> errors)
    {
        var colon = spec.IndexOf(':', StringComparison.Ordinal);
        var name = colon < 0 ? spec : spec[..colon];
        var flag = "'--exit " + Show(spec) + "'";
        if (name is "EVENT_EXIT" or "TIME_STOP")
        {
            errors.Add(flag + ": " + name + " is an overlay, not a candidate" + (name == "TIME_STOP" ? "; use --time-stop" : " and is not run yet (P4-07)"));
            return [];
        }

        if (!Policies.TryGetValue(name, out var policy))
        {
            errors.Add(flag + ": '" + Show(name) + "' is not an exit policy; the policies are " + string.Join(", ", Policies.Keys));
            return [];
        }

        var parameters = new List<(string Name, double[] Values)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ok = true;
        var body = colon < 0 ? string.Empty : spec[(colon + 1)..];
        foreach (var pair in body.Length == 0 ? [] : body.Split(','))
        {
            var equals = pair.IndexOf('=', StringComparison.Ordinal);
            var key = equals < 0 ? pair : pair[..equals];
            if (equals < 0 || !policy.Names.Contains(key, StringComparer.Ordinal))
            {
                errors.Add(flag + ": '" + Show(key) + "' is not a parameter of " + name + "; its parameters are " + string.Join(", ", policy.Names));
                ok = false;
            }
            else if (!seen.Add(key))
            {
                errors.Add(flag + ": '" + key + "' is given more than once");
                ok = false;
            }
            else
            {
                var values = Numbers(pair[(equals + 1)..], out var bad);
                if (bad is not null)
                {
                    errors.Add(flag + ": '" + key + "' must be finite numbers such as 2 or 1.5|2, '" + Show(bad) + "' is not");
                    ok = false;
                }
                else if (values.Length > 1 && !policy.Lists.Contains(key, StringComparer.Ordinal) && !policy.Ladders.Contains(key, StringComparer.Ordinal))
                {
                    errors.Add(flag + ": '" + key + "' takes one value; only " + (policy.Lists.Length == 0 ? "a ladder of PYRAMID" : string.Join(", ", policy.Lists)) + " takes a list with '|'");
                    ok = false;
                }
                else
                {
                    parameters.Add((key, values));
                }
            }
        }

        if (!ok)
        {
            return [];
        }

        // one candidate for each combination of the candidate lists; ladders stay whole
        var combos = new List<List<ExitParameterValue>> { new() };
        foreach (var (key, values) in parameters.OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            var expanded = policy.Lists.Contains(key, StringComparer.Ordinal) ? values.Select(v => new[] { v }).ToList() : [values];
            combos = [.. combos.SelectMany(c => expanded.Select(v => new List<ExitParameterValue>([.. c, new ExitParameterValue(key, v)])))];
        }

        return [.. combos.Select(c => new ExitCandidate(name, c.AsReadOnly()))];
    }

    private static TimeStopChoice? ParseTimeStop(string text, List<string> errors)
    {
        var flag = "'--time-stop " + Show(text) + "'";
        double? progress = null;
        int bars = 0, sessions = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var before = errors.Count;
        foreach (var pair in text.Split(','))
        {
            var equals = pair.IndexOf('=', StringComparison.Ordinal);
            var key = equals < 0 ? pair : pair[..equals];
            if (equals < 0 || !TimeStopNames.Contains(key, StringComparer.Ordinal))
            {
                errors.Add(flag + ": '" + Show(key) + "' is not a parameter of the time stop; they are " + string.Join(", ", TimeStopNames) + " (event_exit is not run yet)");
                continue;
            }

            if (!seen.Add(key))
            {
                errors.Add(flag + ": '" + key + "' is given more than once");
                continue;
            }

            var values = Numbers(pair[(equals + 1)..], out var bad);
            if (bad is not null || values.Length != 1)
            {
                errors.Add(flag + ": '" + key + "' must be one finite number, '" + Show(pair[(equals + 1)..]) + "' is not");
            }
            else if (key == "min_progress_rr")
            {
                progress = values[0];
            }
            else if (values[0] != Math.Floor(values[0]) || values[0] < 1 || values[0] > 1000)
            {
                errors.Add(flag + ": '" + key + "' must be a whole number from 1");
            }
            else if (key == "intraday_bars")
            {
                bars = (int)values[0];
            }
            else
            {
                sessions = (int)values[0];
            }
        }

        if (errors.Count > before)
        {
            return null;
        }

        if (progress is null)
        {
            errors.Add(flag + ": 'min_progress_rr' is required");
            return null;
        }

        if ((bars == 0) == (sessions == 0))
        {
            errors.Add(flag + ": give 'intraday_bars' (an intraday strategy) or 'swing_sessions' (a swing strategy), one of them");
            return null;
        }

        return new TimeStopChoice(progress.Value, bars, sessions);
    }

    private static double[] Numbers(string text, out string? bad)
    {
        var parts = text.Split('|');
        var values = new double[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0 || parts[i].Any(char.IsWhiteSpace) || !double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) || !double.IsFinite(values[i]))
            {
                bad = parts[i];
                return [];
            }
        }

        bad = null;
        return values;
    }

    private static string Show(string text)
    {
        var cut = text.Length > 60 ? text[..60] + "..." : text;
        return string.Concat(cut.Select(c => char.IsControl(c) ? '?' : c));
    }

    private sealed record Policy(string[] Names, string[] Lists, string[] Ladders);
}
