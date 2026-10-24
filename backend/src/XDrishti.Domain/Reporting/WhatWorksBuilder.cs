using XDrishti.Domain.Common;

namespace XDrishti.Domain.Reporting;

/// <summary>Ranks the cells of a grouping for "what works" (P8-00 D5, ADR 0099). Ties are broken by more trades, then by the key text, so the order never depends on the input order.</summary>
internal static class WhatWorksBuilder
{
    public static WhatWorks Build(ReportGrouping grouping, int minTrades, IReadOnlyList<string> unavailableDimensions)
    {
        var ranked = new List<ReportGroup>();
        var notRanked = new List<UnrankedCell>();
        var enoughWithoutBound = 0;
        foreach (var group in grouping.Groups)
        {
            var recorded = group.Key.Parts.All(p => p.Value is not null);
            if (!recorded)
            {
                notRanked.Add(new UnrankedCell(group, group.ExpectancyR.Reason ?? "a coordinate of the cell is not recorded"));
            }
            else if (group.N < minTrades)
            {
                notRanked.Add(new UnrankedCell(group, "insufficient sample: " + group.ExpectancyR.Reason));
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
        var min = ReportText.Count(minTrades);
        var looked = ReportText.Count(cells);

        string headline;
        if (cells == 0)
        {
            headline = "Nothing to rank: the chosen runs hold no trades.";
        }
        else if (ordered.Count == 0)
        {
            headline = $"Nothing has enough evidence yet: looked at {looked} {(cells == 1 ? "cell" : "cells")}, the largest has {ReportText.Count(largest)} {(largest == 1 ? "trade" : "trades")} and {min} are needed.";
            if (enoughWithoutBound > 0)
            {
                headline = $"Nothing can be ranked yet: looked at {looked} {(cells == 1 ? "cell" : "cells")}; {ReportText.Count(enoughWithoutBound)} with {min} or more trades have no lower bound (a cell needs at least one win and one non-win).";
            }
        }
        else
        {
            var tail = positive == 0 ? "none of them has a lower bound above zero" : $"{ReportText.Count(positive)} of them {(positive == 1 ? "has" : "have")} a lower bound above zero";
            headline = $"{ReportText.Count(ordered.Count)} of {looked} cells have {min} or more trades and are ranked by the credible lower bound of expectancy; {tail}.";
        }

        var note = $"Looking at {looked} {(cells == 1 ? "cell" : "cells")} and picking the best is a selection effect: the best of {looked} will look good by chance, and this describes replayed, in-sample history, not the future.";
        return new WhatWorks(
            ordered.Count > 0 ? WhatWorksKind.Ranked : WhatWorksKind.NothingQualifies,
            grouping.Dimensions,
            new ValueList<RankedCell>(ordered),
            new ValueList<UnrankedCell>(rest),
            cells,
            minTrades,
            largest,
            positive,
            headline,
            note,
            new ValueList<string>(unavailableDimensions),
            grouping.NonAdditive,
            grouping.Evidence);
    }
}
