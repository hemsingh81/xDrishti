using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using XDrishti.Domain.Assistant.Drafting;
using XDrishti.Domain.Common;

namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>
/// Grades what the draft pipeline made of an idea by the fixed assertion kinds (spec P9-09 AC 6): no model judge, the owner's own reading of ten drafts stays an owner act. The assertions read the YAML text, its plain tree and the model's answer; they cannot tell
/// a sensible strategy from a literal one. Pure.
/// </summary>
public static partial class DraftGrader
{
    public static IdeaGrade Grade(IdeaCase idea, IdeaOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(idea);
        ArgumentNullException.ThrowIfNull(outcome);
        var failed = new List<string>();
        JsonDocument? tree = null;
        try
        {
            tree = outcome.TreeJson is null ? null : JsonDocument.Parse(outcome.TreeJson);
            foreach (var assertion in idea.Assertions)
            {
                if (!Holds(assertion, outcome, tree?.RootElement))
                {
                    failed.Add(Name(assertion));
                }
            }
        }
        finally
        {
            tree?.Dispose();
        }

        var accepted = Accepted(idea, outcome);
        bool? declineCorrect = idea.IsDeclineKind ? accepted && idea.Assertions.OfType<UnsupportedMentions>().All(a => Holds(a, outcome, null)) : null;
        return new IdeaGrade(idea.Id, accepted, idea.Assertions.Count - failed.Count, idea.Assertions.Count, declineCorrect, new ValueList<string>(failed));
    }

    /// <summary>The outcome is the kind the idea wants: a valid text for a draft, a decline for a decline, a decline or a valid text that names the unsupported part for the partial kind.</summary>
    public static bool Accepted(IdeaCase idea, IdeaOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(idea);
        ArgumentNullException.ThrowIfNull(outcome);
        return idea.Kind switch
        {
            IdeaCase.Draft => outcome.Status == DraftStatus.Valid,
            IdeaCase.Decline => outcome.Status == DraftStatus.Declined,
            IdeaCase.DeclineOrPartial => outcome.Status == DraftStatus.Declined || (outcome.Status == DraftStatus.Valid && outcome.Answer?.NotExpressed.Count > 0),
            _ => false,
        };
    }

    private static bool Holds(IdeaAssertion assertion, IdeaOutcome outcome, JsonElement? tree)
    {
        var yaml = outcome.Yaml ?? string.Empty;
        switch (assertion)
        {
            case PathEquals equals:
                return Resolve(tree, equals.Path) is { } value && Same(Scalar(value), equals.Value);
            case PathExists exists:
                return Resolve(tree, exists.Path) is not null;
            case YamlContainsAny any:
                return any.Words.Any(w => yaml.Contains(w, StringComparison.OrdinalIgnoreCase));
            case NumberCovered covered:
                var haystack = DraftIdea.Haystack(yaml);
                haystack.UnionWith(DraftIdea.Haystack(string.Join('\n', outcome.Answer?.NotExpressed.Select(n => n.What + " " + n.Why) ?? [])));
                return haystack.Contains(covered.Token);
            case DeclineAsserted:
                return outcome.Status == DraftStatus.Declined && outcome.Answer?.Decline == true;
            case UnsupportedMentions mentions:
                var said = string.Join('\n', (outcome.Answer?.Unsupported ?? []).Concat(outcome.Answer?.NotExpressed.Select(n => n.What + " " + n.Why) ?? []));
                return mentions.Words.Any(w => said.Contains(w, StringComparison.OrdinalIgnoreCase));
            case SidesInclude sides:
                return sides.Sides.All(s => HasSide(tree, s));
            default:
                return false;
        }
    }

    // Sides come from the strategy's own list; a SELL side may instead be a sell block. A text with no list is a BUY strategy (UNVERIFIED against the language's default).
    private static bool HasSide(JsonElement? tree, string side)
    {
        if (Resolve(tree, "strategy.sides") is { ValueKind: JsonValueKind.Array } list)
        {
            return list.EnumerateArray().Any(e => string.Equals(Scalar(e), side, StringComparison.OrdinalIgnoreCase));
        }

        return string.Equals(side, "SELL", StringComparison.OrdinalIgnoreCase) ? Resolve(tree, "strategy.sell") is not null : string.Equals(side, "BUY", StringComparison.OrdinalIgnoreCase);
    }

    private static JsonElement? Resolve(JsonElement? root, string path)
    {
        if (root is not { } current)
        {
            return null;
        }

        foreach (var part in path.Split('.'))
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

    private static string Scalar(JsonElement e) => e.ValueKind == JsonValueKind.String ? e.GetString() ?? string.Empty : e.GetRawText();

    private static bool Same(string a, string b) =>
        decimal.TryParse(a.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var x) && decimal.TryParse(b.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
            ? x == y
            : string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string Name(IdeaAssertion assertion) => assertion switch
    {
        PathEquals e => "path_equals:" + e.Path,
        PathExists e => "path_exists:" + e.Path,
        YamlContainsAny => "yaml_contains_any",
        NumberCovered n => "number_covered:" + n.Token,
        DeclineAsserted => "decline",
        UnsupportedMentions => "unsupported_mentions",
        SidesInclude => "sides_include",
        _ => "unknown",
    };

    [GeneratedRegex(@"^([A-Za-z_][A-Za-z0-9_]*)(?:\[(\d{1,3})\])?$", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Segment();
}
