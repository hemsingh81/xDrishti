using System.Globalization;

namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>
/// What a run over one generator found, summed: the series compared (each in both fill modes, counted once), the comparisons, the candidates and trades, how many series had a gate with several candidates, the oracle's trace counted per
/// series (so that a coverage floor is measured from the trace and not from the engine's counters), the series that reached each of the 16 categories, and every disagreement with its key.
/// </summary>
internal sealed class Tally(string name)
{
    private readonly HashSet<(string, int, long)> _series = [];

    internal string Name { get; } = name;

    internal int Series => _series.Count;

    internal int Comparisons { get; private set; }

    internal long Candidates { get; private set; }

    internal long Trades { get; private set; }

    internal long Blocked { get; private set; }

    internal int GatedSeries { get; private set; }

    /// <summary>Series (per mode, counted per comparison) whose trace contains a row.</summary>
    internal Dictionary<string, int> RowSeries { get; } = new(StringComparer.Ordinal);

    /// <summary>Per category 1 to 16, the comparisons whose trace reaches it (from the oracle's trace).</summary>
    internal int[] CategoryReached { get; } = new int[OracleCategories.Count + 1];

    /// <summary>Per category, the comparisons of series that O generated for it (OracleSeries only).</summary>
    internal int[] CategoryGenerated { get; } = new int[OracleCategories.Count + 1];

    internal List<string> Disagreements { get; } = [];

    internal int DisagreementCount { get; private set; }

    internal void Add(SeriesResult result)
    {
        _series.Add((result.Generator, result.Category, result.Seed));
        Comparisons++;
        Candidates += result.Candidates;
        Trades += result.Trades;
        Blocked += result.Blocked;
        if (result.Gated)
        {
            GatedSeries++;
        }

        // the trace is counted once per series, in the modelled mode (the exact mode walks the same minutes with no slippage)
        if (result.Mode == ComparisonCases.Mode(false))
        {
            foreach (var row in result.Trace)
            {
                RowSeries[row] = RowSeries.GetValueOrDefault(row) + 1;
            }

            for (var category = 1; category <= OracleCategories.Count; category++)
            {
                if (OracleCategories.Reached(category, result.Trace))
                {
                    CategoryReached[category]++;
                }
            }

            if (result.Category is >= 1 and <= 16)
            {
                CategoryGenerated[result.Category]++;
            }
        }

        foreach (var (where, mismatch) in result.Mismatches)
        {
            DisagreementCount++;
            if (Disagreements.Count < 400)
            {
                Disagreements.Add(string.Create(CultureInfo.InvariantCulture, $"{result.Generator} seed {result.Seed} category {result.Category} {result.Mode} {where}: {mismatch}"));
            }
        }
    }

    /// <summary>The summary of the run as text, in a fixed order: the same seeds give the same bytes (criterion 12).</summary>
    internal string Summary()
    {
        var lines = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture, $"{Name}: series {Series}, comparisons {Comparisons}, candidates {Candidates}, trades {Trades}, blocked {Blocked}, gated {GatedSeries}, disagreements {DisagreementCount}"),
            "categories reached: " + string.Join(",", CategoryReached.Skip(1)),
            "categories generated: " + string.Join(",", CategoryGenerated.Skip(1)),
            "rows: " + string.Join(",", RowSeries.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => r.Key + "=" + r.Value.ToString(CultureInfo.InvariantCulture))),
        };
        lines.AddRange(Disagreements.Order(StringComparer.Ordinal));
        return string.Join("\n", lines);
    }
}
