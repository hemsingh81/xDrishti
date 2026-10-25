using System.Text;
using XDrishti.Domain.Research;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Model;

namespace XDrishti.Strategies.Mirror;

/// <summary>
/// Expands the automatic SELL (spec P3-02e, D1): a definition that lists SELL, has no <c>sell</c> block and has BUY rules gets the mirror of its BUY side as its <c>Sell</c>, and carries the table version, so the canonical
/// definition (and with it the hash and the run key) shows exactly what SELL trades and which table made it. The stored text is not rewritten. Intraday only (swing SELL is refused by the model). Any other definition comes back as it
/// is. Pure and thread-safe.
/// </summary>
public static class StrategyMirror
{
    private static readonly SourceSpan NoText = new(0, 0, 1, 1);

    /// <summary>True when <see cref="Expand"/> would mirror this definition.</summary>
    public static bool Applies(StrategyDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return definition.Sell is null && definition.Buy is not null && definition.Style == Style.Intraday && definition.Sides?.Contains(Side.Sell) == true;
    }

    /// <summary>
    /// The definition with its SELL mirrored and one information problem <c>applied</c> (path <c>sell</c>) that holds the printed block, or the unchanged definition and the errors (paths <c>sell.trigger[1]</c>).
    /// <paramref name="printBlock"/> writes a side as the strategy file would (the Lab passes the file writer); without it the block is a short listing of the conditions.
    /// </summary>
    public static (StrategyDefinition Definition, IReadOnlyList<LocatedProblem> Problems) Expand(StrategyDefinition definition, Func<SideRules, string>? printBlock = null)
    {
        if (!Applies(definition))
        {
            return (definition, []);
        }

        var names = (definition.Params ?? []).Where(p => p is not null).Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var (rules, problems) = SideMirror.Mirror(definition.Buy!, names);
        if (rules is null)
        {
            return (definition, problems);
        }

        var block = printBlock?.Invoke(rules) ?? Listing(rules);
        var applied = new LocatedProblem(
            "sell",
            new Problem(
                MirrorCodes.Applied,
                "The SELL side is the mirror of the BUY side (table " + MirrorTable.Version + "). To own it, copy this into the strategy as `sell:`:\n" + block,
                NoText,
                null,
                ProblemSeverity.Information));
        return (definition with { Sell = rules, SellMirroredWith = MirrorTable.Version }, [applied]);
    }

    private static string Listing(SideRules rules)
    {
        var text = new StringBuilder();
        text.Append("sell:\n");
        foreach (var (name, list) in new[] { ("setup", rules.Setup), ("trigger", rules.Trigger), ("invalidate", rules.Invalidate) })
        {
            if (list?.Items is null)
            {
                continue;
            }

            text.Append("  ").Append(name).Append(": ").Append(list.Mode == ListMode.All ? "all" : "any").Append('\n');
            foreach (var item in list.Items)
            {
                text.Append("    - ").Append(item).Append('\n');
            }
        }

        return text.ToString();
    }
}
