using System.Globalization;

namespace XDrishti.Domain.Paper;

/// <summary>An accepted hypothesis as the explainer sees it (the newest row is <c>accepted</c>).</summary>
/// <param name="Id">The hypothesis id.</param>
/// <param name="CellKey">The cell it was about, or null.</param>
/// <param name="KnobKind">parameter, exit or slippage.</param>
/// <param name="KnobName">The knob.</param>
/// <param name="CandidateValue">The value it proposed.</param>
/// <param name="AcceptedAt">When it was accepted.</param>
public sealed record AcceptedHypothesis(long Id, string? CellKey, string KnobKind, string KnobName, string CandidateValue, DateTimeOffset AcceptedAt);

/// <summary>A learning-changelog row about a cell.</summary>
/// <param name="Id">The row id.</param>
/// <param name="Subject">The cell key.</param>
/// <param name="At">When it was written.</param>
public sealed record ChangelogFact(long Id, string Subject, DateTimeOffset At);

/// <summary>
/// Matches a changed <c>cells.eligible</c> entry to an accepted hypothesis or a changelog row (spec P10-05 criterion 8). By cell and value, never proof of intent. Settings, accounts and autonomy are never hypotheses: they stay unexplained.
/// </summary>
public static class ChangeExplainer
{
    /// <summary>The explanation of each change by key (<c>hypothesis:&lt;id&gt;</c>, <c>changelog:&lt;id&gt;</c>, or null). A hypothesis explains one change only.</summary>
    /// <param name="changes">The changes of one detection.</param>
    /// <param name="hypotheses">Accepted hypotheses.</param>
    /// <param name="used">Hypothesis ids already used by an earlier detection.</param>
    /// <param name="changelog">Changelog rows about cells.</param>
    /// <param name="parameters">The stored parameters of a strategy version by its hash, or null when unknown.</param>
    /// <param name="previousAt">When the earlier snapshot was taken.</param>
    /// <param name="detectedAt">When the change was detected.</param>
    public static IReadOnlyDictionary<string, string?> Explain(
        IReadOnlyList<ConfigChange> changes,
        IReadOnlyList<AcceptedHypothesis> hypotheses,
        IReadOnlySet<long> used,
        IReadOnlyList<ChangelogFact> changelog,
        Func<string, IReadOnlyDictionary<string, string>?> parameters,
        DateTimeOffset previousAt,
        DateTimeOffset detectedAt)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(hypotheses);
        ArgumentNullException.ThrowIfNull(used);
        ArgumentNullException.ThrowIfNull(changelog);
        ArgumentNullException.ThrowIfNull(parameters);
        var taken = new HashSet<long>(used);
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        var matched = new List<AcceptedHypothesis>();
        foreach (var change in changes.OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            string? explained = null;
            if (change.Key.StartsWith(PaperConfigKeys.CellsEligible, StringComparison.Ordinal) && change.After is { } hash)
            {
                var cell = change.Key[PaperConfigKeys.CellsEligible.Length..];
                var match = hypotheses.Where(h => h.AcceptedAt <= detectedAt && !taken.Contains(h.Id)).OrderBy(h => h.Id).FirstOrDefault(h => Matches(h, cell, change.Before is null, parameters(hash)));
                if (match is not null)
                {
                    taken.Add(match.Id);
                    matched.Add(match);
                    explained = "hypothesis:" + match.Id.ToString(CultureInfo.InvariantCulture);
                }
                else if (changelog.Where(c => c.Subject == cell && c.At > previousAt && c.At <= detectedAt).OrderBy(c => c.Id).FirstOrDefault() is { } row)
                {
                    explained = "changelog:" + row.Id.ToString(CultureInfo.InvariantCulture);
                }
            }

            result[change.Key] = explained;
        }

        // The old cell the matched hypothesis was about leaves the eligible set when its replacement arrives: the removal is the same act and carries the same explanation.
        foreach (var removed in changes.Where(c => c.After is null && c.Key.StartsWith(PaperConfigKeys.CellsEligible, StringComparison.Ordinal)))
        {
            if (matched.FirstOrDefault(h => h.CellKey == removed.Key[PaperConfigKeys.CellsEligible.Length..]) is { } owner)
            {
                result[removed.Key] = "hypothesis:" + owner.Id.ToString(CultureInfo.InvariantCulture);
            }
        }

        return result;
    }

    private static bool Matches(AcceptedHypothesis h, string cell, bool added, IReadOnlyDictionary<string, string>? versionParameters)
    {
        var parts = cell.Split('|');
        var hypothesisParts = h.CellKey?.Split('|');
        if (parts.Length != 6 || hypothesisParts is not { Length: 6 })
        {
            return false;
        }

        var sameButExit = Enumerable.Range(0, 6).Where(i => i != 4).All(i => parts[i] == hypothesisParts[i]);
        switch (h.KnobKind.ToLowerInvariant())
        {
            case "exit":
                return added && sameButExit && parts[4] == h.CandidateValue;
            case "parameter":
                if (!added && cell == h.CellKey && versionParameters is not null && versionParameters.TryGetValue(h.KnobName, out var stored))
                {
                    return SameValue(stored, h.CandidateValue);
                }

                return added && sameButExit && parts[4] != hypothesisParts[4] && parts[4].Contains(h.KnobName + "=" + h.CandidateValue, StringComparison.Ordinal);
            default:
                return false;
        }
    }

    private static bool SameValue(string a, string b) =>
        decimal.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) && decimal.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
            ? x == y
            : string.Equals(a, b, StringComparison.Ordinal);
}
