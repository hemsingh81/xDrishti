using System.Globalization;
using XDrishti.Domain.Common;

namespace XDrishti.Domain.Learning;

/// <summary>
/// Tags every loss of a set of closed trades with <see cref="LossTagger"/>, counts the tags per cell and finds the tags that grow (P6-15, ADR 0071). Pure and deterministic: the input order does not matter.
/// </summary>
public static class LossTagAnalyzer
{
    /// <summary>Failure code for bad options or input.</summary>
    public const string InvalidCode = "loss_tags.invalid";

    /// <summary>A rough share of comparisons that flag by chance alone (a rough planning figure, not a test); the report prints comparisons times this.</summary>
    private const double ChanceFlagRate = 0.05;

    /// <summary>Tags the losses of <paramref name="inputs"/>; fails on bad options, no trades, a repeated trade key or a non-finite R.</summary>
    public static Result<LossTagReport> Analyze(IReadOnlyList<LossTagInput> inputs, LossTagOptions options)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(options);
        var problems = options.Problems();
        if (problems.Count > 0)
        {
            return Fail(string.Join("; ", problems));
        }

        if (inputs.Count == 0)
        {
            return Fail("no closed trades were given");
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var input in inputs)
        {
            if (!double.IsFinite(input.R))
            {
                return Fail("a trade has an R that is not a number");
            }

            if (!keys.Add(input.Trade.Key))
            {
                return Fail("a trade key appears twice: " + input.Trade.Key);
            }
        }

        var kinds = Enum.GetValues<LossTagKind>();
        var cells = new List<CellLossTags>();
        var allLosses = new List<TaggedLoss>();
        var comparisons = 0;
        foreach (var group in inputs.GroupBy(i => i.CellKey, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var losses = group.Where(i => i.R < 0)
                .Select(i => new TaggedLoss(i.Trade.Key, group.Key, i.R, i.Trade.ExitEpochSeconds, LossTagger.Tag(i.Trade, options)))
                .OrderBy(l => l.ExitEpochSeconds).ThenBy(l => l.Key, StringComparer.Ordinal)
                .ToList();
            var counts = kinds.Select(k => losses.Count(l => l.Tags.Any(t => t.Kind == k))).ToList();
            var lostR = kinds.Select(k => losses.Where(l => l.Tags.Any(t => t.Kind == k)).Sum(l => l.R)).ToList();
            var growing = new List<GrowingTag>();
            var w = options.GrowingWindow;
            if (losses.Count >= 2 * w)
            {
                var recent = losses.Skip(losses.Count - w).ToList();
                var earlier = losses.Skip(losses.Count - (2 * w)).Take(w).ToList();
                foreach (var kind in kinds.Where(k => k != LossTagKind.NormalVariance))
                {
                    comparisons++;
                    var rc = recent.Count(l => l.Tags.Any(t => t.Kind == kind));
                    var ec = earlier.Count(l => l.Tags.Any(t => t.Kind == kind));
                    if (rc >= options.GrowingMinCount && (double)(rc - ec) / w >= options.GrowingMinRise)
                    {
                        growing.Add(new GrowingTag(group.Key, kind, rc, ec, w));
                    }
                }
            }

            cells.Add(new CellLossTags(group.Key, group.Count(), losses.Count, counts, losses.Count(l => l.Tags.Count == 0), growing, lostR));
            allLosses.AddRange(losses);
        }

        var worst = allLosses.OrderBy(l => l.R).ThenBy(l => l.Key, StringComparer.Ordinal).Take(options.ShowWorst).ToList();
        var c = CultureInfo.InvariantCulture;
        var expected = comparisons * ChanceFlagRate;
        var limits = new List<string>
        {
            "Tags describe closed trades after the fact, with the numbers shown; they are not features and must not be used at decision time.",
            "A loss can carry several tags; NormalVariance is given only when no other tag fits, and untagged losses are counted as such.",
            string.Create(c, $"The growing-tag detector compared {comparisons} cell and tag pairs, counting only cells with at least {2 * options.GrowingWindow} losses; at an assumed 5 per cent chance per comparison about {expected:F1} flags are expected by luck alone. A flag is a question for a lesson, not a finding."),
            "Replayed history with modelled fills: slippage and gap figures are as good as the fill model.",
        };
        return Result.Success(new LossTagReport(options, inputs.Count, allLosses.Count, cells, worst, comparisons, limits));
    }

    private static Result<LossTagReport> Fail(string message) => Result.Failure<LossTagReport>(Error.Validation(InvalidCode, message));
}
