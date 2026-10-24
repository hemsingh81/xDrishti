using System.Globalization;
using XDrishti.Domain.Common;
using XDrishti.Domain.Lab;

namespace XDrishti.Domain.Reporting;

/// <summary>
/// Ranks the cells of a grouping for "what works" (P8-00 D5, ADR 0099). The cells that compete are those with every coordinate recorded and at least the minimum trades; their number N sets the sample a cell needs, <c>ceil(min x MultipleTesting.Factor(N))</c>, so
/// comparing more cells asks more of each. A ranked cell also has the minimum wins and non-wins (so its lower bound rests on payoffs, not on a single outlier). Ties are broken by more trades, then by the key text, so the order never depends on the input order.
/// </summary>
internal static class WhatWorksBuilder
{
    public static WhatWorks Build(ReportGrouping grouping, ReportOptions options, IReadOnlyList<string> unavailableDimensions)
    {
        var minTrades = options.Stats.MinTrades;
        var comparable = grouping.Groups.Count(g => g.Key.Parts.All(p => p.Value is not null) && g.N >= minTrades);
        var factor = MultipleTesting.Factor(comparable);
        var required = comparable == 0 ? minTrades : (int)Math.Ceiling((minTrades * factor) - 1e-9);

        var ranked = new List<ReportGroup>();
        var notRanked = new List<UnrankedCell>();
        var enoughWithoutBound = 0;
        foreach (var group in grouping.Groups)
        {
            if (!group.Key.Parts.All(p => p.Value is not null))
            {
                notRanked.Add(new UnrankedCell(group, group.ExpectancyR.Reason ?? "a coordinate of the cell is not recorded"));
            }
            else if (group.N < minTrades)
            {
                notRanked.Add(new UnrankedCell(group, "insufficient sample: " + group.ExpectancyR.Reason));
            }
            else if (group.N < required)
            {
                notRanked.Add(new UnrankedCell(group, string.Create(CultureInfo.InvariantCulture, $"{group.N} trades, {required} needed when {comparable} cells are compared ({minTrades} x {factor:0.00})")));
            }
            else if (group.ExpectancyLowerR.Number is null)
            {
                enoughWithoutBound++;
                notRanked.Add(new UnrankedCell(group, "no lower bound can be formed: " + group.ExpectancyLowerR.Reason));
            }
            else
            {
                ranked.Add(group);
            }
        }

        var ordered = ranked
            .OrderByDescending(g => g.ExpectancyLowerR.Number!.Value)
            .ThenByDescending(g => g.N)
            .ThenBy(g => g.Key.Text, StringComparer.Ordinal)
            .Select((g, i) => new RankedCell(i + 1, g, g.ExpectancyLowerR.Number!.Value > 0))
            .ToList();
        var rest = notRanked.OrderByDescending(u => u.Group.N).ThenBy(u => u.Group.Key.Text, StringComparer.Ordinal).ToList();
        var cells = grouping.Groups.Count;
        var largest = cells == 0 ? 0 : grouping.Groups.Max(g => g.N);
        var positive = ordered.Count(r => r.LowerBoundPositive);
        var chance = (1 - options.Stats.CredibleLevel) / 2 * ordered.Count;
        var chanceText = chance.ToString("0.0", CultureInfo.InvariantCulture);
        var min = ReportText.Count(minTrades);
        var need = ReportText.Count(required);
        var looked = ReportText.Count(cells);

        string headline;
        if (cells == 0)
        {
            headline = "Nothing to rank: the chosen runs hold no trades.";
        }
        else if (ordered.Count == 0)
        {
            headline = $"Nothing has enough evidence yet: looked at {looked} {(cells == 1 ? "cell" : "cells")}, the largest has {ReportText.Count(largest)} {(largest == 1 ? "trade" : "trades")} and {need} are needed.";
            if (required > minTrades)
            {
                headline += $" ({min} per cell, raised to {need} because {ReportText.Count(comparable)} cells are compared.)";
            }

            if (enoughWithoutBound > 0)
            {
                headline += $" {ReportText.Count(enoughWithoutBound)} with {need} or more trades have no lower bound (a cell needs at least {options.MinWins} wins and {options.MinNonWins} non-wins).";
            }
        }
        else
        {
            var tail = positive == 0
                ? "none of them has a lower bound above zero"
                : $"{ReportText.Count(positive)} of them {(positive == 1 ? "has" : "have")} a lower bound above zero, about {chanceText} expected by chance alone";
            headline = $"{ReportText.Count(ordered.Count)} of {looked} cells have {need} or more trades and are ranked by the credible lower bound of expectancy; {tail}.";
        }

        var note = comparable == 0
            ? "No cell is comparable yet; ranking many cells later will carry a selection effect: the best of many looks good by chance."
            : $"Comparing {ReportText.Count(comparable)} {(comparable == 1 ? "cell" : "cells")} and picking the best is a selection effect: the best of {ReportText.Count(comparable)} will look good by chance, and this describes replayed, in-sample history, not the future.";
        return new WhatWorks(
            ordered.Count > 0 ? WhatWorksKind.Ranked : WhatWorksKind.NothingQualifies,
            grouping.Dimensions,
            new ValueList<RankedCell>(ordered),
            new ValueList<UnrankedCell>(rest),
            cells,
            comparable,
            minTrades,
            required,
            largest,
            positive,
            chance,
            headline,
            note,
            new ValueList<string>(unavailableDimensions),
            grouping.NonAdditive,
            grouping.Evidence);
    }
}
