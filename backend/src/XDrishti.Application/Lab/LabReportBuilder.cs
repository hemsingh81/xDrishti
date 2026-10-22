using System.Globalization;
using System.Text;

namespace XDrishti.Application.Lab;

/// <summary>
/// Builds the report of a finished full or robust test from the stored run and verdict (spec P5-06, ADR 0063). Pure: no clock, no I/O, no random source. The warning thresholds are the constants below and are printed in the warning text.
/// </summary>
internal static class LabReportBuilder
{
    /// <summary>A cell with fewer out-of-sample trades than this is a low sample.</summary>
    public const int LowCellTrades = 30;

    /// <summary>Net expectancy (R) under which a small rise in costs would erase the edge.</summary>
    public const double ThinExpectancyR = 0.15;

    /// <summary>The share of the out-of-sample trades one cell may hold before the result is called concentrated.</summary>
    public const double ConcentrationShare = 0.6;

    /// <summary>The p-value and the share of Monte Carlo draws without edge above which chance is not excluded.</summary>
    public const double ChanceLevel = 0.05;

    private static readonly string[] Missing =
    [
        "Win rate, average win and loss, and net rupees after costs: the stored result holds the R figures only.",
        "By basket and by instrument tables: not stored with the result.",
        "Charts (equity, R histogram, MAE/MFE, parameter heatmap, Monte Carlo fan): not built; the Monte Carlo and by-year numbers are in the robustness block.",
        "Comparisons with existing strategies and with index buy-and-hold: not built.",
        "The trade list with chart replay: the trades are the candidate runs kept by the research module, whose keys are in the run section.",
    ];

    public static LabReportView Build(LabRunView run, LabVerdictView verdict, int minFoldsForPass)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(verdict);
        verdict = Sanitise(verdict);
        var e = verdict.Evidence;
        var cells = verdict.Cells
            .OrderByDescending(c => c.OosMeanR ?? double.NegativeInfinity)
            .ThenBy(c => c.Cell, StringComparer.Ordinal)
            .ToList();
        var report = new LabReportView(
            run.Id,
            verdict.Outcome,
            verdict.VariantCount,
            verdict.AdjustmentFactor,
            new ReportRunView(run.Key, S(run.Strategy.Id), run.Strategy.Version, run.Strategy.Hash, run.Method, run.Window.From, run.Window.To, S(run.Universe.Label), run.Universe.Instruments.Count, run.SimRunKeys),
            new ReportKpisView(e.OosTrades, e.ExpectancyR, e.ProfitFactor, e.MaxDrawdownR, e.Folds, e.PositiveFolds),
            verdict.Gates,
            cells,
            verdict.FullPeriod,
            verdict.Robustness,
            Warnings(verdict, cells),
            Limits(run, verdict, minFoldsForPass),
            Missing,
            [.. run.Labels.Select(S).Concat(verdict.Labels).Distinct(StringComparer.Ordinal)],
            string.Empty);
        return report with { Summary = ReportSummary.Write(report) };
    }

    private static List<ReportWarningView> Warnings(LabVerdictView v, List<CellView> cells)
    {
        var w = new List<ReportWarningView>();
        var tradesGate = v.Gates.FirstOrDefault(g => g.Name == "oos_trades");
        var lowCells = cells.Where(c => c.OosTrades < LowCellTrades).Select(c => c.Cell).ToList();
        if (tradesGate is { Passed: false } || lowCells.Count > 0)
        {
            var text = tradesGate is { Passed: false } ? $"The out-of-sample trade count {v.Evidence.OosTrades} is under the adjusted minimum {F(tradesGate.Adjusted)}. " : string.Empty;
            if (lowCells.Count > 0)
            {
                text += $"Cells under {LowCellTrades} out-of-sample trades: {string.Join(", ", lowCells)}.";
            }

            w.Add(new ReportWarningView("low_samples", text.Trim()));
        }

        if (v.Evidence.ExpectancyR is { } x && x < ThinExpectancyR)
        {
            w.Add(new ReportWarningView("cost_sensitivity", $"Net expectancy {F(x)} R is under {F(ThinExpectancyR)} R; a small rise in costs or slippage could erase it."));
        }

        var total = v.Evidence.OosTrades;
        var top = cells.OrderByDescending(c => c.OosTrades).FirstOrDefault();
        if (total > 0 && cells.Count > 1 && top is not null && top.OosTrades >= ConcentrationShare * total)
        {
            w.Add(new ReportWarningView("concentration", $"Cell {top.Cell} holds {top.OosTrades} of {total} out-of-sample trades (at least {F(ConcentrationShare * 100)} %)."));
        }

        var years = v.Robustness?.ByYear;
        if (years is { Count: > 1 })
        {
            var sum = years.Sum(y => y.TotalR);
            var best = years.MaxBy(y => y.TotalR)!;
            if (sum > 0 && best.TotalR > sum)
            {
                w.Add(new ReportWarningView("concentration", $"Year {best.Year} alone made {F(best.TotalR)} R, more than the whole total of {F(sum)} R."));
            }
        }

        var unstable = new List<string>();
        // Shortcut: anything but "stable" counts as unstable; a new verdict word of the sweep would be flagged until listed here.
        unstable.AddRange(v.Robustness?.Sensitivity.Parameters.Where(p => p.Verdict != "stable").Select(p => $"{p.Param} ({p.Verdict})") ?? []);
        unstable.AddRange(cells.Where(c => c.Stable == false).Select(c => $"exit choice of {c.Cell}"));
        if (unstable.Count > 0)
        {
            w.Add(new ReportWarningView("unstable_parameters", "Not stable: " + string.Join(", ", unstable) + "."));
        }

        var chance = new List<string>();
        if (v.Robustness?.SignFlipBaseline is { } b && b.PValue > ChanceLevel)
        {
            chance.Add($"sign-flip p-value {P(b.PValue)}");
        }

        if (v.Robustness?.MonteCarlo is { } m && m.ShareNotPositive >= ChanceLevel)
        {
            chance.Add($"Monte Carlo share without edge {P(m.ShareNotPositive)}");
        }

        if (chance.Count > 0)
        {
            w.Add(new ReportWarningView("chance_not_excluded", "Chance is not excluded: " + string.Join(", ", chance) + $" (limit {P(ChanceLevel)})."));
        }

        return w;
    }

    private static List<string> Limits(LabRunView run, LabVerdictView v, int minFolds)
    {
        var l = new List<string>
        {
            // Shortcut: the 2.1 years is the history stored today (P0 data load), not read from the store; revisit when the history grows.
            $"Data: the run window is {run.Window.From:yyyy-MM-dd} to {run.Window.To:yyyy-MM-dd}; the stored history is about 2.1 years with no hold-out, so every figure rests on a short sample.",
            $"Walk-forward folds: {v.Evidence.Folds}; a PASS needs at least {minFolds}.",
            $"Full-period figures are {LabFullTest.ExplorationLabel}.",
        };
        if (v.Robustness is not null)
        {
            l.Add("The sign-flip baseline is lenient: it tests the mean R against 0 given the trade sizes, not a re-run with random entries.");
            l.Add("The sensitivity sweep is in-sample on the whole window.");
            l.AddRange(v.Robustness.Limits);
        }

        l.AddRange(v.Limits);
        l.AddRange(v.Labels.Where(x => x.StartsWith("exploration", StringComparison.OrdinalIgnoreCase)));
        return [.. l.Distinct(StringComparer.Ordinal)];
    }

    internal static string F(double value) => NoNegativeZero(value.ToString("0.##", CultureInfo.InvariantCulture));

    /// <summary>A p-value or share: four decimals, and a positive value under 0.0001 as "&lt;0.0001", so a small value never prints as 0.</summary>
    internal static string P(double value) => value > 0 && value < 0.0001 ? "<0.0001" : NoNegativeZero(value.ToString("0.####", CultureInfo.InvariantCulture));

    private static string NoNegativeZero(string text) => text == "-0" ? "0" : text;

    /// <summary>Free text from outside (strategy id, labels, cell names, sweep text): control, format (bidi) and unpaired surrogate characters removed, line breaks and tabs become spaces.</summary>
    internal static string S(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var text = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    if (CharUnicodeInfo.GetUnicodeCategory(value, i) != UnicodeCategory.Format)
                    {
                        text.Append(c).Append(value[i + 1]);
                    }

                    i++;
                }
            }
            else if (c is '\n' or '\r' or '\t')
            {
                text.Append(' ');
            }
            else if (!char.IsLowSurrogate(c) && CharUnicodeInfo.GetUnicodeCategory(c) is not (UnicodeCategory.Control or UnicodeCategory.Format))
            {
                text.Append(c);
            }
        }

        return text.ToString();
    }

    private static LabVerdictView Sanitise(LabVerdictView v) => v with
    {
        Gates = [.. v.Gates.Select(g => g with { Name = S(g.Name), Reason = S(g.Reason) })],
        Cells = [.. v.Cells.Select(c => c with { Cell = S(c.Cell), Default = S(c.Default), Chosen = S(c.Chosen), NoChoiceReason = c.NoChoiceReason is null ? null : S(c.NoChoiceReason), StabilityReason = S(c.StabilityReason) })],
        Limits = [.. v.Limits.Select(S)],
        Labels = [.. v.Labels.Select(S)],
        Robustness = v.Robustness is null ? null : v.Robustness with
        {
            Sensitivity = v.Robustness.Sensitivity with
            {
                Parameters = [.. v.Robustness.Sensitivity.Parameters.Select(p => p with { Param = S(p.Param), Verdict = S(p.Verdict), Reason = S(p.Reason) })],
            },
            ByRegime = S(v.Robustness.ByRegime),
            Limits = [.. v.Robustness.Limits.Select(S)],
        },
    };
}
