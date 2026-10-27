using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace XDrishti.Domain.Assistant.Drafting;

/// <summary>
/// The machine checks of a draft (spec P9-07 AC 2 to 5, D5): C3 the assumptions are true, C4 the text is only a draft, C5 the explanation holds no number that is not in the idea or the text, C6 no number or time of the idea is dropped silently. Pure; the validity of the text itself
/// (C1 and C2) is the Lab validator's. A failing check is a finding and a repair reason.
/// </summary>
public static partial class DraftChecks
{
    /// <summary>The most params a draft may declare.</summary>
    public const int MaxParams = 4;

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"^([A-Za-z_][A-Za-z0-9_]*)(?:\[(\d{1,3})\])?$", RegexOptions.CultureInvariant)]
    private static partial Regex Segment();

    /// <summary>The findings for an answer that carries a text, in a fixed order; empty when every check passes.</summary>
    public static IReadOnlyList<DraftFinding> Run(string idea, DraftAnswer answer, DraftYamlFacts yaml)
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(yaml);
        var findings = new List<DraftFinding>();
        if (yaml.TreeJson is null)
        {
            findings.Add(new DraftFinding(DraftCodes.YamlUnreadable, string.Empty));
        }

        findings.AddRange(Draftness(yaml));
        findings.AddRange(Assumptions(answer, yaml));
        findings.AddRange(ExplanationNumbers(idea, answer, yaml));
        findings.AddRange(DroppedNumbers(idea, answer, yaml));
        return findings;
    }

    /// <summary>C4: no <c>version</c>, an id of the right shape, at most four params. (An id that is taken is the owner's to rename at Confirm.)</summary>
    public static IReadOnlyList<DraftFinding> Draftness(DraftYamlFacts yaml)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        var list = new List<DraftFinding>();
        if (yaml.Version is not null)
        {
            list.Add(new DraftFinding(DraftCodes.HasVersion, "version"));
        }

        if (yaml.Id is not null && !IdPattern().IsMatch(yaml.Id))
        {
            list.Add(new DraftFinding(DraftCodes.IdInvalid, "id"));
        }

        if (yaml.ParamCount > MaxParams)
        {
            list.Add(new DraftFinding(DraftCodes.TooManyParams, yaml.ParamCount.ToString(CultureInfo.InvariantCulture)));
        }

        return list;
    }

    /// <summary>C3: each assumption's field exists in the text with the stated value.</summary>
    public static IReadOnlyList<DraftFinding> Assumptions(DraftAnswer answer, DraftYamlFacts yaml)
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(yaml);
        var list = new List<DraftFinding>();
        if (yaml.TreeJson is null)
        {
            return list;
        }

        using var document = JsonDocument.Parse(yaml.TreeJson);
        foreach (var assumption in answer.Assumptions)
        {
            var found = Resolve(document.RootElement, assumption.Field);
            if (found is not { ValueKind: JsonValueKind.String } scalar || !SameValue(scalar.GetString(), assumption.Value))
            {
                list.Add(new DraftFinding(DraftCodes.AssumptionNotInYaml, assumption.Field));
            }
        }

        return list;
    }

    /// <summary>C5: every number in <c>understood</c> and in each assumption's <c>why</c> occurs in the idea or the text.</summary>
    public static IReadOnlyList<DraftFinding> ExplanationNumbers(string idea, DraftAnswer answer, DraftYamlFacts yaml)
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(yaml);
        var source = DraftIdea.Haystack(idea);
        source.UnionWith(DraftIdea.Haystack(yaml.Text));
        var list = new List<DraftFinding>();
        foreach (var token in DraftIdea.Numbers(string.Join('\n', answer.Understood.Concat(answer.Assumptions.Select(a => a.Why)))))
        {
            if (!source.Contains(token))
            {
                list.Add(new DraftFinding(DraftCodes.ExplanationNumberNotInSource, token));
            }
        }

        return list;
    }

    /// <summary>C6: every number or clock time of the idea is in the text, in the requested test or listed in <c>not_expressed</c>.</summary>
    public static IReadOnlyList<DraftFinding> DroppedNumbers(string idea, DraftAnswer answer, DraftYamlFacts yaml)
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(yaml);
        var covered = DraftIdea.Haystack(yaml.Text);
        covered.UnionWith(DraftIdea.Haystack(string.Join('\n', answer.NotExpressed.Select(n => n.What + " " + n.Why))));
        covered.UnionWith(DraftIdea.Haystack(string.Join('\n', new[] { answer.RequestedTest.From, answer.RequestedTest.To }.Concat(answer.RequestedTest.Baskets).Where(s => s is not null))));
        var list = new List<DraftFinding>();
        foreach (var token in DraftIdea.Numbers(idea))
        {
            var ok = covered.Contains(token) || (DraftIdea.IsRatio(token) && covered.Contains(DraftIdea.RatioSecond(token)));
            if (!ok)
            {
                list.Add(new DraftFinding(DraftCodes.IdeaNumberDropped, token));
            }
        }

        return list;
    }

    /// <summary>The strategy mapping of a text: the value of the <c>strategy</c> key of the file, or the root itself when the text has no such key.</summary>
    public static JsonElement Strategy(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("strategy", out var inner) && inner.ValueKind == JsonValueKind.Object ? inner : root;

    private static JsonElement? Resolve(JsonElement root, string path)
    {
        var current = Strategy(root);
        foreach (var part in path.Split('.', StringSplitOptions.None))
        {
            var m = Segment().Match(part);
            if (!m.Success || current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(m.Groups[1].Value, out current))
            {
                return null;
            }

            if (m.Groups[2].Success)
            {
                var index = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                if (current.ValueKind != JsonValueKind.Array || index >= current.GetArrayLength())
                {
                    return null;
                }

                current = current[index];
            }
        }

        return current;
    }

    private static bool SameValue(string? a, string? b)
    {
        var x = (a ?? string.Empty).Trim();
        var y = (b ?? string.Empty).Trim();
        return decimal.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var dx) && decimal.TryParse(y, NumberStyles.Float, CultureInfo.InvariantCulture, out var dy)
            ? dx == dy
            : string.Equals(x, y, StringComparison.OrdinalIgnoreCase);
    }
}
