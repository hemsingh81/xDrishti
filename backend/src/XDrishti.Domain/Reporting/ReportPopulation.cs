using System.Globalization;
using XDrishti.Domain.Common;

namespace XDrishti.Domain.Reporting;

/// <summary>
/// The trades a report is computed over, after the population rule (P8-00 D2, ADR 0099): built from stored runs and their trades, refused when it would count a trade twice or mix gross and net, and then asked for groups, pivots, the "what works" ranking and
/// the auto-insights. Pure: no clock, no I/O, no random; the same runs and trades in any order give the same answer.
/// <para>
/// The rule: identical records of one trade in one run are kept once; two runs of one strategy, universe, overlapping window and the same style and exit are refused; one signal (its stored trade key and timeframe) in two runs under the same style and exit is refused;
/// one signal in two runs under different labels is allowed (an exit comparison) but then every grouping must split by the label that differs, so the signal is never added once per label; runs with and without charges are never mixed; a net report needs the
/// owner's confirmation of the charge sheet.
/// </para>
/// </summary>
public sealed class ReportPopulation
{
    private readonly IReadOnlyList<ReportRow> _rows;
    private readonly Dictionary<(string?, string?), Pool> _pools;

    private ReportPopulation(IReadOnlyList<ReportRow> rows, ReportOptions options, bool styleVaries, bool exitVaries, ReportEvidence evidence, bool anyExitRecorded, bool anyBasketRecorded)
    {
        _rows = rows;
        Options = options;
        StyleVaries = styleVaries;
        ExitVaries = exitVaries;
        Evidence = evidence;
        AnyExitKnown = anyExitRecorded;
        AnyBasketKnown = anyBasketRecorded;
        _pools = PoolsOf(rows, exitVaries, styleVaries);
    }

    /// <summary>The options the population was built with.</summary>
    public ReportOptions Options { get; }

    /// <summary>The evidence of the whole population: runs, dates, basis, limits and unavailable inputs.</summary>
    public ReportEvidence Evidence { get; }

    /// <summary>Trades in the population, each once.</summary>
    public int TradeCount => _rows.Count;

    /// <summary>The same signals appear under different style labels, so a grouping must include the style.</summary>
    public bool StyleVaries { get; }

    /// <summary>The same signals appear under different exit labels, so a grouping must include the exit.</summary>
    public bool ExitVaries { get; }

    internal bool AnyExitKnown { get; }

    internal bool AnyBasketKnown { get; }

    /// <summary>Applies the population rule to the runs and trades and returns the population, or the typed refusal.</summary>
    /// <param name="runs">The runs the report may use.</param>
    /// <param name="trades">Their closed trades.</param>
    /// <param name="options">The report options.</param>
    public static Result<ReportPopulation> Create(IReadOnlyList<ReportRun> runs, IReadOnlyList<ReportTrade> trades, ReportOptions options)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(trades);
        ArgumentNullException.ThrowIfNull(options);
        var problems = options.Problems();
        if (problems.Count > 0)
        {
            return Fail(ReportErrors.Invalid(string.Join("; ", problems)));
        }

        var byKey = new Dictionary<string, ReportRun>(StringComparer.Ordinal);
        foreach (var run in runs.OrderBy(r => r.RunKey, StringComparer.Ordinal))
        {
            var bad = RunProblem(run);
            if (bad is not null)
            {
                return Fail(ReportErrors.Invalid(bad));
            }

            if (byKey.TryGetValue(run.RunKey, out var existing))
            {
                if (existing != run)
                {
                    return Fail(ReportErrors.Invalid($"Run '{run.RunKey}' is listed twice with different definitions."));
                }

                continue;
            }

            byKey[run.RunKey] = run;
        }

        var sortedRuns = byKey.Values.OrderBy(r => r.RunKey, StringComparer.Ordinal).ToList();
        var costed = sortedRuns.Where(r => r.Costed).Select(r => r.RunKey).ToList();
        var gross = sortedRuns.Where(r => !r.Costed).Select(r => r.RunKey).ToList();
        if (costed.Count > 0 && gross.Count > 0)
        {
            return Fail(ReportErrors.MixedBasis(costed, gross));
        }

        if (options.Basis == ReportBasis.Net)
        {
            if (options.NetBasisConfirmation is null)
            {
                return Fail(ReportErrors.NetNotConfirmed());
            }

            if (sortedRuns.Count > 0 && costed.Count == 0)
            {
                return Fail(ReportErrors.NetUnavailable());
            }
        }

        // sorted once by run key, trade key and timeframe (a total order: the input order does not matter); a plain array sort, since this is the one place a 500,000-trade population is sorted twice
        var sorted = new ReportTrade[trades.Count];
        for (var i = 0; i < sorted.Length; i++)
        {
            sorted[i] = trades[i] ?? throw new ArgumentException("A trade is null.", nameof(trades));
        }

        Array.Sort(sorted, CompareByKey);
        var kept = new List<ReportTrade>(sorted.Length);
        var dropped = 0;
        ReportTrade? previous = null;
        foreach (var trade in sorted)
        {
            var bad = TradeProblem(trade, byKey);
            if (bad is not null)
            {
                return Fail(bad);
            }

            if (previous is not null && previous.RunKey == trade.RunKey && previous.TradeKey == trade.TradeKey && previous.Timeframe == trade.Timeframe)
            {
                if (previous != trade)
                {
                    return Fail(ReportErrors.Invalid($"Trade '{trade.TradeKey}' is in run '{trade.RunKey}' twice with different values."));
                }

                dropped++;
                continue;
            }

            if (previous is not null && previous.RunKey == trade.RunKey && previous.TradeKey == trade.TradeKey)
            {
                return Fail(ReportErrors.Invalid($"Trade '{trade.TradeKey}' is in run '{trade.RunKey}' twice with different timeframes."));
            }

            kept.Add(trade);
            previous = trade;
        }

        var overlap = RunOverlapOf(sortedRuns, kept);
        if (overlap is not null)
        {
            return Fail(overlap);
        }

        var (signalError, styleVaries, exitVaries) = SignalsOf(kept, byKey);
        if (signalError is not null)
        {
            return Fail(signalError);
        }

        var rows = new ReportRow[kept.Count];
        ReportRun? current = null;
        string? lastKey = null;
        for (var i = 0; i < rows.Length; i++)
        {
            var t = kept[i];
            if (current is null || !ReferenceEquals(lastKey, t.RunKey))
            {
                current = byKey[t.RunKey];
                lastKey = t.RunKey;
            }

            rows[i] = new ReportRow(t, current.Style, current.Exit, options.Basis == ReportBasis.Net ? t.RNet!.Value : t.RGross, current.Provisional);
        }

        Array.Sort(rows, CompareByExit);

        var population = GroupMath.EvidenceOf(rows, options.Basis);
        population = population with { RunKeys = new ValueList<string>(sortedRuns.Select(r => r.RunKey)), Provisional = sortedRuns.Any(r => r.Provisional) };
        var unavailable = UnavailableInputs(sortedRuns, rows);
        var confirmation = options.Basis == ReportBasis.Net ? options.NetBasisConfirmation : null;
        var evidence = new ReportEvidence(population, new ValueList<string>(ReportLimits.For(options.Basis, population.Provisional, confirmation)), new ValueList<string>(unavailable), dropped, confirmation);
        return Result.Success(new ReportPopulation(rows, options, styleVaries, exitVaries, evidence, sortedRuns.Any(r => r.Exit is not null), rows.Any(r => r.Trade.BasketIds.Count > 0)));
    }

    /// <summary>Groups the trades by the dimensions, in order. A trade is in exactly one group, except under the basket dimension where it is in one per basket (then <see cref="ReportGrouping.NonAdditive"/> says so). Refused when a dimension has no data or when the grouping would add one signal once per label.</summary>
    /// <param name="dimensions">The dimensions; empty is the whole population as one group.</param>
    /// <param name="maxGroups">The most groups the caller will take; the pass over the trades stops at the first group past it and refuses, before any figure is built.</param>
    public Result<ReportGrouping> GroupBy(IReadOnlyList<ReportDimension> dimensions, int maxGroups = int.MaxValue) => GroupAtMinimum(dimensions, Options.Stats.MinTrades, maxGroups);

    /// <summary>A pivot of two different dimensions by one figure. Cells with no trade are empty and cells below the minimum are masked, never zero.</summary>
    /// <param name="rows">The row dimension.</param>
    /// <param name="columns">The column dimension.</param>
    /// <param name="metric">The figure.</param>
    /// <param name="maxCells">The most cells (rows times columns) the caller will take; refused before any figure or cell is built when the pass over the trades finds more non-empty cells than that, or the grid itself would be larger.</param>
    public Result<ReportPivot> Pivot(ReportDimension rows, ReportDimension columns, ReportMetric metric, int maxCells = int.MaxValue)
    {
        if (rows == columns)
        {
            return Result.Failure<ReportPivot>(ReportErrors.Invalid("A pivot needs two different dimensions."));
        }

        if (!Enum.IsDefined(metric))
        {
            return Result.Failure<ReportPivot>(ReportErrors.Invalid("Unknown figure."));
        }

        var grouped = GroupBy([rows, columns], maxCells);
        if (grouped.IsFailure)
        {
            return Result.Failure<ReportPivot>(grouped.Error);
        }

        var grouping = grouped.Value;
        var rowValues = grouping.Groups.Select(g => g.Key.Parts[0].Value).Distinct().OrderBy(v => v, Comparer<string?>.Create((a, b) => GroupMath.CompareValues(rows, a, b))).ToList();
        var columnValues = grouping.Groups.Select(g => g.Key.Parts[1].Value).Distinct().OrderBy(v => v, Comparer<string?>.Create((a, b) => GroupMath.CompareValues(columns, a, b))).ToList();
        if ((long)rowValues.Count * columnValues.Count > maxCells)
        {
            return Result.Failure<ReportPivot>(ReportErrors.TooManyGroups((long)rowValues.Count * columnValues.Count, maxCells, "cells"));
        }

        var index = grouping.Groups.ToDictionary(g => (g.Key.Parts[0].Value, g.Key.Parts[1].Value));
        var cells = new List<PivotCell>(rowValues.Count * columnValues.Count);
        foreach (var r in rowValues)
        {
            foreach (var c in columnValues)
            {
                cells.Add(index.TryGetValue((r, c), out var group)
                    ? new PivotCell(r, c, group.N, group.Get(metric))
                    : new PivotCell(r, c, 0, Metric.Empty("no trades")));
            }
        }

        return Result.Success(new ReportPivot(rows, columns, metric, new ValueList<string?>(rowValues), new ValueList<string?>(columnValues), new ValueList<PivotCell>(cells), grouping.NonAdditive, grouping.Evidence));
    }

    /// <summary>
    /// "What works, in which basket, on which timeframe, with which exit": the cells (by default basket, strategy, timeframe, side and exit, plus style when it varies) ranked by the credible lower bound of expectancy. Only cells with the minimum sample and a lower bound are ranked;
    /// the others follow with the reason. When none qualifies the answer says so with the counts. A dimension no run or trade recorded (the exit, the basket) is left out of the default cell and named in the answer.
    /// </summary>
    /// <param name="dimensions">The dimensions of a cell, or null for the default.</param>
    /// <param name="maxCells">The most cells the caller will take; refused at the first cell past it, before any figure is built.</param>
    public Result<WhatWorks> FindWhatWorks(IReadOnlyList<ReportDimension>? dimensions = null, int maxCells = int.MaxValue)
    {
        var left = new List<string>();
        List<ReportDimension> dims;
        if (dimensions is null)
        {
            dims = [];
            if (AnyBasketKnown)
            {
                dims.Add(ReportDimension.Basket);
            }
            else
            {
                left.Add("basket left out: no trade records the baskets of its instrument");
            }

            dims.AddRange([ReportDimension.Strategy, ReportDimension.Timeframe, ReportDimension.Side]);
            if (StyleVaries)
            {
                dims.Add(ReportDimension.Style);
            }

            if (AnyExitKnown)
            {
                dims.Add(ReportDimension.Exit);
            }
            else
            {
                left.Add("exit left out: no run records its exit policy");
            }
        }
        else
        {
            dims = [.. dimensions];
        }

        var grouped = GroupBy(dims, maxCells);
        if (grouped.IsFailure)
        {
            return Result.Failure<WhatWorks>(grouped.Error);
        }

        return Result.Success(WhatWorksBuilder.Build(grouped.Value, Options, left));
    }

    /// <summary>The auto-insights of doc 15 section 6, always the same eight in the same order; each states a figure only when every group it compares has the minimum number of trades.</summary>
    public IReadOnlyList<Insight> Insights() => InsightBuilder.Build(this);

    /// <summary>
    /// The equity curve (running R and its drawdown from the running peak, one point per exit date in the order of the exits) of every group of the dimensions, empty for the whole population. A group below the minimum sample or with a coordinate no run recorded
    /// has no points: its total is <see cref="MetricState.Insufficient"/> or <see cref="MetricState.Unavailable"/>, never a flat line.
    /// </summary>
    /// <param name="dimensions">The dimensions; empty is the whole population as one series.</param>
    /// <param name="maxGroups">The most curves the caller will take; refused at the first group past it, before any point is built.</param>
    public Result<ReportEquity> Equity(IReadOnlyList<ReportDimension> dimensions, int maxGroups = int.MaxValue)
    {
        var bucketed = Bucketise(dimensions, maxGroups);
        if (bucketed.IsFailure)
        {
            return Result.Failure<ReportEquity>(bucketed.Error);
        }

        var series = new List<ReportEquitySeries>(bucketed.Value.Buckets.Count);
        foreach (var bucket in bucketed.Value.Buckets)
        {
            var (nonValue, evidence) = SeriesState(bucket);
            if (nonValue is not null)
            {
                series.Add(new ReportEquitySeries(bucket.Key, bucket.Rows.Count, nonValue, new ValueList<EquityPoint>([]), evidence));
                continue;
            }

            var points = new List<EquityPoint>();
            double running = 0;
            double peak = 0;
            foreach (var row in bucket.Rows)
            {
                running += row.R;
                peak = Math.Max(peak, running);
                var drawdown = peak - running;
                if (points.Count > 0 && points[^1].Date == row.Trade.ExitDate)
                {
                    // the point of a date carries the running R at the end of the date and the deepest fall reached during it, so the deepest point of the curve is the maximum drawdown of the group whatever the order within a date
                    points[^1] = new EquityPoint(row.Trade.ExitDate, running, Math.Max(points[^1].DrawdownR, drawdown), points[^1].Trades + 1);
                }
                else
                {
                    points.Add(new EquityPoint(row.Trade.ExitDate, running, drawdown, 1));
                }
            }

            var total = Metric.Of(running);
            series.Add(new ReportEquitySeries(bucket.Key, bucket.Rows.Count, evidence.Provisional ? total.AsProvisional(ProvisionalReason(bucket.Rows)) : total, new ValueList<EquityPoint>(points), evidence));
        }

        return Result.Success(new ReportEquity(new ValueList<ReportDimension>(dimensions), new ValueList<ReportEquitySeries>(series), bucketed.Value.NonAdditive, Evidence));
    }

    /// <summary>
    /// The distribution of R (the count of trades in each bin of <paramref name="binWidthR"/>, from <see cref="DistributionLowerR"/> to <see cref="DistributionUpperR"/> with an open bin below and above) of every group of the dimensions. A group below the minimum
    /// sample or with a coordinate no run recorded has no bins; its mean is <see cref="MetricState.Insufficient"/> or <see cref="MetricState.Unavailable"/>.
    /// </summary>
    /// <param name="dimensions">The dimensions; empty is the whole population as one group.</param>
    /// <param name="binWidthR">The width of a bin in R, 0.1 to 5.</param>
    /// <param name="maxGroups">The most distributions the caller will take; refused at the first group past it, before any bin is built.</param>
    public Result<ReportDistribution> Distribution(IReadOnlyList<ReportDimension> dimensions, double binWidthR, int maxGroups = int.MaxValue)
    {
        if (!double.IsFinite(binWidthR) || binWidthR is < 0.1 or > 5)
        {
            return Fail<ReportDistribution>(ReportErrors.Invalid("The bin width is 0.1 to 5 R."));
        }

        var bucketed = Bucketise(dimensions, maxGroups);
        if (bucketed.IsFailure)
        {
            return Result.Failure<ReportDistribution>(bucketed.Error);
        }

        var edges = new List<double>();
        for (var i = 0; DistributionLowerR + (i * binWidthR) < DistributionUpperR - 1e-9; i++)
        {
            edges.Add(DistributionLowerR + (i * binWidthR));
        }

        edges.Add(edges[^1] + binWidthR);
        var groups = new List<ReportDistributionGroup>(bucketed.Value.Buckets.Count);
        foreach (var bucket in bucketed.Value.Buckets)
        {
            var (nonValue, evidence) = SeriesState(bucket);
            if (nonValue is not null)
            {
                groups.Add(new ReportDistributionGroup(bucket.Key, bucket.Rows.Count, nonValue, new ValueList<DistributionBin>([]), evidence));
                continue;
            }

            var counts = new int[edges.Count + 1];
            double sum = 0;
            foreach (var row in bucket.Rows)
            {
                sum += row.R;
                var index = row.R < edges[0] ? 0 : (int)Math.Floor((row.R - edges[0]) / binWidthR) + 1;
                counts[Math.Min(index, edges.Count)]++;
            }

            var bins = new List<DistributionBin>(counts.Length);
            for (var i = 0; i < counts.Length; i++)
            {
                bins.Add(new DistributionBin(i == 0 ? null : edges[i - 1], i == counts.Length - 1 ? null : edges[i], counts[i]));
            }

            var mean = Metric.Of(sum / bucket.Rows.Count);
            groups.Add(new ReportDistributionGroup(bucket.Key, bucket.Rows.Count, evidence.Provisional ? mean.AsProvisional(ProvisionalReason(bucket.Rows)) : mean, new ValueList<DistributionBin>(bins), evidence));
        }

        return Result.Success(new ReportDistribution(binWidthR, new ValueList<ReportDimension>(dimensions), new ValueList<ReportDistributionGroup>(groups), bucketed.Value.NonAdditive, Evidence));
    }

    /// <summary>
    /// The excursions (MAE by result grid, MFE histogram, levels reached, p90 of the MAE of winners) of every group of the dimensions, as counts. A group below the minimum sample or with a coordinate no run recorded carries only the reason.
    /// </summary>
    /// <param name="dimensions">The dimensions; empty is the whole population as one group.</param>
    /// <param name="maxGroups">The most groups the caller will take; refused at the first group past it.</param>
    public Result<ReportMaeMfe> MaeMfe(IReadOnlyList<ReportDimension> dimensions, int maxGroups = int.MaxValue)
    {
        var bucketed = Bucketise(dimensions, maxGroups);
        if (bucketed.IsFailure)
        {
            return Result.Failure<ReportMaeMfe>(bucketed.Error);
        }

        var groups = new List<ReportMaeMfeGroup>(bucketed.Value.Buckets.Count);
        foreach (var bucket in bucketed.Value.Buckets)
        {
            var (nonValue, evidence) = SeriesState(bucket);
            if (nonValue is not null)
            {
                groups.Add(new ReportMaeMfeGroup(bucket.Key, bucket.Rows.Count, 0, nonValue, null, null, new ValueList<ReachedLevel>([]), evidence));
                continue;
            }

            var (grid, mfe, reached, n, p90) = MaeMfeBuilder.Build(bucket.Rows, Options.MinWins);
            if (n == 0)
            {
                groups.Add(new ReportMaeMfeGroup(bucket.Key, bucket.Rows.Count, 0, Metric.Unavailable("no excursion is recorded for these trades"), null, null, new ValueList<ReachedLevel>([]), evidence));
                continue;
            }

            groups.Add(new ReportMaeMfeGroup(bucket.Key, bucket.Rows.Count, n, p90, grid, mfe, reached, evidence));
        }

        return Result.Success(new ReportMaeMfe(new ValueList<ReportDimension>(dimensions), new ValueList<ReportMaeMfeGroup>(groups), bucketed.Value.NonAdditive, Evidence));
    }

    /// <summary>A slice of the trades of the population in the order of the exits (date, run key, trade key), each once, with the figures a screen lists. An offset past the end gives an empty slice.</summary>
    /// <param name="offset">How many trades to skip.</param>
    /// <param name="count">How many to return at most.</param>
    public IReadOnlyList<ReportTradeLine> Trades(int offset, int count)
    {
        if (offset < 0 || count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "The offset and the count are not negative.");
        }

        var end = (int)Math.Min((long)offset + count, _rows.Count);
        var lines = new List<ReportTradeLine>(Math.Max(0, end - offset));
        for (var i = offset; i < end; i++)
        {
            var row = _rows[i];
            var t = row.Trade;
            lines.Add(new ReportTradeLine(t.RunKey, t.TradeKey, t.Strategy, t.Timeframe, t.Side, t.Symbol, t.BasketIds, t.EntryDate, t.ExitDate, row.R, t.SessionsHeld, row.Style, row.Exit, row.Provisional));
        }

        return lines.AsReadOnly();
    }

    /// <summary>The lower edge of the distribution in R; trades below it are in the open bin.</summary>
    public const double DistributionLowerR = -4;

    /// <summary>The upper edge of the distribution in R; trades at or above it are in the open bin.</summary>
    public const double DistributionUpperR = 6;

    private (Metric? NonValue, EvidenceBlock Evidence) SeriesState(Bucket bucket)
    {
        var evidence = GroupMath.EvidenceOf(bucket.Rows, Options.Basis);
        var missing = bucket.Key.Parts.Where(p => p.Value is null).Select(p => p.Dimension).ToList();
        if (missing.Count > 0)
        {
            var runs = string.Join(", ", bucket.Rows.Select(r => r.Trade.RunKey).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
            return (Metric.Unavailable($"{string.Join(" and ", missing.Select(d => d.ToString().ToLowerInvariant()))} not recorded for run {runs}"), evidence);
        }

        var n = bucket.Rows.Count;
        return n < Options.Stats.MinTrades
            ? (Metric.Insufficient(string.Create(CultureInfo.InvariantCulture, $"{n} {(n == 1 ? "trade" : "trades")}, at least {Options.Stats.MinTrades} needed")), evidence)
            : (null, evidence);
    }

    private static string ProvisionalReason(List<ReportRow> rows) =>
        "rests on a provisional run: " + string.Join(", ", rows.Where(r => r.Provisional).Select(r => r.Trade.RunKey).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));

    internal Result<ReportGrouping> GroupAtMinimum(IReadOnlyList<ReportDimension> dimensions, int minTrades, int maxGroups = int.MaxValue)
    {
        var bucketed = Bucketise(dimensions, maxGroups);
        if (bucketed.IsFailure)
        {
            return Result.Failure<ReportGrouping>(bucketed.Error);
        }

        var groups = new List<ReportGroup>(bucketed.Value.Buckets.Count);
        foreach (var bucket in bucketed.Value.Buckets)
        {
            groups.Add(GroupMath.Build(bucket.Key, bucket.Rows, PriorsFor(bucket.Key, bucket.Rows[0]), Options, minTrades));
        }

        return Result.Success(new ReportGrouping(new ValueList<ReportDimension>(dimensions), new ValueList<ReportGroup>(groups), bucketed.Value.NonAdditive, Evidence));
    }

    /// <summary>One pass over the trades into buckets, one per combination of the dimensions' values (the basket dimension puts a trade into one bucket per basket), in the fixed order of the keys.</summary>
    private Result<Bucketed> Bucketise(IReadOnlyList<ReportDimension> dimensions, int maxBuckets = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(dimensions);
        if (dimensions.Distinct().Count() != dimensions.Count || dimensions.Any(d => !Enum.IsDefined(d)))
        {
            return Fail<Bucketed>(ReportErrors.Invalid("The dimensions are repeated or unknown."));
        }

        foreach (var dimension in dimensions)
        {
            if (dimension is ReportDimension.Regime or ReportDimension.Account)
            {
                return Fail<Bucketed>(ReportErrors.DimensionUnavailable(dimension));
            }
        }

        var required = new List<ReportDimension>();
        if (StyleVaries && !dimensions.Contains(ReportDimension.Style))
        {
            required.Add(ReportDimension.Style);
        }

        if (ExitVaries && !dimensions.Contains(ReportDimension.Exit))
        {
            required.Add(ReportDimension.Exit);
        }

        if (required.Count > 0)
        {
            return Fail<Bucketed>(ReportErrors.DoubleCount(required));
        }

        // one pass, no LINQ and no string per trade: every dimension but the basket has one value per trade; a trade in several baskets is added once under each
        var bucketer = new Bucketer(dimensions);
        var months = new Dictionary<int, string>();
        var basketIndex = -1;
        for (var i = 0; i < dimensions.Count; i++)
        {
            if (dimensions[i] == ReportDimension.Basket)
            {
                basketIndex = i;
            }
        }

        var nonAdditive = false;
        foreach (var row in _rows)
        {
            // the work is bounded, not only the answer: the pass stops at the first group past the cap, before a figure, a point or a bin is built
            if (bucketer.Count > maxBuckets)
            {
                return Fail<Bucketed>(ReportErrors.TooManyGroups(null, maxBuckets, "groups"));
            }

            for (var i = 0; i < dimensions.Count; i++)
            {
                if (i != basketIndex)
                {
                    bucketer.Values[i] = SingleValue(row, dimensions[i], months);
                }
            }

            if (basketIndex < 0)
            {
                bucketer.Add(row);
                continue;
            }

            var baskets = row.Trade.BasketIds;
            if (baskets.Count <= 1)
            {
                bucketer.Values[basketIndex] = baskets.Count == 0 ? null : baskets[0];
                bucketer.Add(row);
                continue;
            }

            var distinct = baskets.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            nonAdditive |= distinct.Count > 1;
            foreach (var basket in distinct)
            {
                bucketer.Values[basketIndex] = basket;
                bucketer.Add(row);
            }
        }

        if (bucketer.Count > maxBuckets)
        {
            return Fail<Bucketed>(ReportErrors.TooManyGroups(null, maxBuckets, "groups"));
        }

        var ordered = bucketer.Buckets.OrderBy(b => b.Key, Comparer<GroupKey>.Create(GroupMath.CompareKeys)).ToList();
        return Result.Success(new Bucketed(ordered, nonAdditive));
    }

    private sealed record Bucketed(List<Bucket> Buckets, bool NonAdditive);

    /// <summary>Run key, trade key, then timeframe, all ordinal; the run key of the trades of one run is usually one string instance, which the equality shortcut of the reference check uses.</summary>
    private static int CompareByKey(ReportTrade a, ReportTrade b)
    {
        var c = CompareRunKeys(a.RunKey, b.RunKey);
        if (c != 0)
        {
            return c;
        }

        c = string.CompareOrdinal(a.TradeKey, b.TradeKey);
        return c != 0 ? c : string.CompareOrdinal(a.Timeframe, b.Timeframe);
    }

    /// <summary>Exit date, run key, trade key, then timeframe: the order of the exits.</summary>
    private static int CompareByExit(ReportRow a, ReportRow b)
    {
        var c = a.Trade.ExitDate.CompareTo(b.Trade.ExitDate);
        if (c != 0)
        {
            return c;
        }

        c = CompareRunKeys(a.Trade.RunKey, b.Trade.RunKey);
        if (c != 0)
        {
            return c;
        }

        c = string.CompareOrdinal(a.Trade.TradeKey, b.Trade.TradeKey);
        return c != 0 ? c : string.CompareOrdinal(a.Trade.Timeframe, b.Trade.Timeframe);
    }

    private static int CompareRunKeys(string a, string b) => ReferenceEquals(a, b) ? 0 : string.CompareOrdinal(a, b);

    private static Result<T> Fail<T>(Error error) => Result.Failure<T>(error);

    private static Result<ReportPopulation> Fail(Error error) => Result.Failure<ReportPopulation>(error);

    private static string? RunProblem(ReportRun run)
    {
        if (string.IsNullOrWhiteSpace(run.RunKey) || string.IsNullOrWhiteSpace(run.Universe))
        {
            return "A run needs a key and a universe.";
        }

        if (run.WindowStart > run.WindowEnd)
        {
            return $"Run '{run.RunKey}' has a window that ends before it starts.";
        }

        if ((run.Style is not null && string.IsNullOrWhiteSpace(run.Style)) || (run.Exit is not null && string.IsNullOrWhiteSpace(run.Exit)))
        {
            return $"Run '{run.RunKey}' has an empty style or exit label; use null for not recorded.";
        }

        return null;
    }

    private static Error? TradeProblem(ReportTrade t, Dictionary<string, ReportRun> runs)
    {
        if (!runs.TryGetValue(t.RunKey, out var run))
        {
            return ReportErrors.UnknownRun(t.RunKey);
        }

        var where = $"Trade '{t.TradeKey}' of run '{t.RunKey}'";
        if (string.IsNullOrWhiteSpace(t.TradeKey) || string.IsNullOrWhiteSpace(t.Strategy) || string.IsNullOrWhiteSpace(t.Timeframe) || string.IsNullOrWhiteSpace(t.Side) || string.IsNullOrWhiteSpace(t.Symbol) || t.BasketIds is null)
        {
            return ReportErrors.Invalid($"{where} is missing its key, strategy, timeframe, side, symbol or baskets.");
        }

        if (!double.IsFinite(t.RGross) || (t.RNet is { } net && !double.IsFinite(net)))
        {
            return ReportErrors.Invalid($"{where} has an R that is not a finite number.");
        }

        if (t.ExitDate < t.EntryDate || t.SessionsHeld < 0)
        {
            return ReportErrors.Invalid($"{where} exits before it enters or holds a negative number of sessions.");
        }

        if (run.Costed && t.RNet is null)
        {
            return ReportErrors.Invalid($"{where} belongs to a run with charges but has no net R.");
        }

        return null;
    }

    private static Error? RunOverlapOf(List<ReportRun> runs, List<ReportTrade> trades)
    {
        var strategies = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        string? lastKey = null;
        HashSet<string>? current = null;
        foreach (var t in trades)
        {
            if (current is null || !ReferenceEquals(lastKey, t.RunKey))
            {
                lastKey = t.RunKey;
                if (!strategies.TryGetValue(t.RunKey, out current))
                {
                    current = new HashSet<string>(StringComparer.Ordinal);
                    strategies[t.RunKey] = current;
                }
            }

            current.Add(t.Strategy);
        }

        for (var i = 0; i < runs.Count; i++)
        {
            for (var j = i + 1; j < runs.Count; j++)
            {
                var a = runs[i];
                var b = runs[j];
                if (a.Universe != b.Universe || a.Style != b.Style || a.Exit != b.Exit || a.WindowStart > b.WindowEnd || b.WindowStart > a.WindowEnd)
                {
                    continue;
                }

                if (strategies.TryGetValue(a.RunKey, out var sa) && strategies.TryGetValue(b.RunKey, out var sb))
                {
                    var shared = sa.Intersect(sb, StringComparer.Ordinal).Order(StringComparer.Ordinal).FirstOrDefault();
                    if (shared is not null)
                    {
                        return ReportErrors.RunOverlap(a.RunKey, b.RunKey, shared);
                    }
                }
            }
        }

        return null;
    }

    private static (Error? Error, bool StyleVaries, bool ExitVaries) SignalsOf(List<ReportTrade> trades, Dictionary<string, ReportRun> runs)
    {
        // a signal can only repeat across runs (a repeat inside one run was dropped or refused above); find the ones that do without building a string per trade, and look only at those
        var seen = new HashSet<(string, string)>(trades.Count);
        HashSet<(string, string)>? repeated = null;
        foreach (var t in trades)
        {
            if (!seen.Add((t.TradeKey, t.Timeframe)))
            {
                (repeated ??= []).Add((t.TradeKey, t.Timeframe));
            }
        }

        var styleVaries = false;
        var exitVaries = false;
        if (repeated is null)
        {
            return (null, styleVaries, exitVaries);
        }

        foreach (var signal in trades.Where(t => repeated.Contains((t.TradeKey, t.Timeframe))).GroupBy(t => t.TradeKey + "\u0001" + t.Timeframe, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var members = signal.OrderBy(t => t.RunKey, StringComparer.Ordinal).ToList();
            for (var i = 0; i < members.Count; i++)
            {
                for (var j = i + 1; j < members.Count; j++)
                {
                    var a = runs[members[i].RunKey];
                    var b = runs[members[j].RunKey];
                    if (a.Style == b.Style && a.Exit == b.Exit)
                    {
                        return (ReportErrors.TradeOverlap(members[i].TradeKey, a.RunKey, b.RunKey), false, false);
                    }

                    styleVaries |= a.Style != b.Style;
                    exitVaries |= a.Exit != b.Exit;
                }
            }
        }

        return (null, styleVaries, exitVaries);
    }

    private static List<string> UnavailableInputs(List<ReportRun> runs, IReadOnlyList<ReportRow> rows)
    {
        var list = new List<string>();
        list.AddRange(runs.Where(r => r.Style is null).Select(r => $"style not recorded for run {r.RunKey}"));
        list.AddRange(runs.Where(r => r.Exit is null).Select(r => $"exit not recorded for run {r.RunKey}"));
        var noBasket = rows.Count(r => r.Trade.BasketIds.Count == 0);
        if (noBasket > 0)
        {
            list.Add(string.Create(CultureInfo.InvariantCulture, $"basket membership not recorded for {noBasket} {(noBasket == 1 ? "trade" : "trades")}"));
        }

        list.Add(ReportErrors.DimensionUnavailable(ReportDimension.Regime).Message);
        list.Add(ReportErrors.DimensionUnavailable(ReportDimension.Account).Message);
        return list;
    }

    /// <summary>The value of a single-valued dimension for a trade (the basket is handled by the caller).</summary>
    private static string? SingleValue(ReportRow row, ReportDimension dimension, Dictionary<int, string> months) => dimension switch
    {
        ReportDimension.Instrument => row.Trade.Symbol,
        ReportDimension.Strategy => row.Trade.Strategy,
        ReportDimension.Timeframe => row.Trade.Timeframe,
        ReportDimension.Side => row.Trade.Side,
        ReportDimension.Style => row.Style,
        ReportDimension.Exit => row.Exit,
        ReportDimension.Month => Month(row.Trade.EntryDate, months),
        ReportDimension.Weekday => GroupMath.WeekdayOf(row.Trade.EntryDate),
        ReportDimension.Sector => row.Trade.Sector,
        ReportDimension.Hour => HourOf(row),
        _ => throw new ArgumentOutOfRangeException(nameof(dimension)),
    };

    /// <summary>The entry hour (two digits) of an intraday trade; a swing or unlabelled run, or an entry with no recorded minute, has none.</summary>
    private static string? HourOf(ReportRow row) =>
        row.Style == "intraday" && row.Trade.EntryMinuteIst is { } minute ? (minute / 60).ToString("D2", CultureInfo.InvariantCulture) : null;

    private static string Month(DateOnly date, Dictionary<int, string> months)
    {
        var index = (date.Year * 12) + date.Month;
        if (!months.TryGetValue(index, out var text))
        {
            text = GroupMath.MonthOf(date);
            months[index] = text;
        }

        return text;
    }

    private static Dictionary<(string?, string?), Pool> PoolsOf(IReadOnlyList<ReportRow> rows, bool exitVaries, bool styleVaries)
    {
        var pools = new Dictionary<(string?, string?), Pool>();
        foreach (var row in rows)
        {
            var key = (exitVaries ? row.Exit : string.Empty, styleVaries ? row.Style : string.Empty);
            if (!pools.TryGetValue(key, out var pool))
            {
                pool = new Pool();
                pools[key] = pool;
            }

            pool.Add(row);
        }

        return pools;
    }

    /// <summary>What a group is shrunk toward. The system level is the pool (all trades with the same differing labels); when the strategy is a coordinate of the group, the strategy level is the system level shrunk toward the pool, as in the cell statistics.</summary>
    private GroupPriors PriorsFor(GroupKey key, ReportRow first)
    {
        var pool = _pools[(ExitVaries ? first.Exit : string.Empty, StyleVaries ? first.Style : string.Empty)];
        var strength = Options.Stats.PriorStrength;
        var system = pool.All;
        var rate = system.N > 0 ? (double)system.Wins / system.N : 0.5;
        double? win = system.WinCount > 0 ? system.WinSum / system.WinCount : null;
        double? nonWin = system.NonWinCount > 0 ? system.NonWinSum / system.NonWinCount : null;
        var strategy = key.ValueOf(ReportDimension.Strategy);
        if (strategy is not null && pool.Strategies.TryGetValue(strategy, out var t))
        {
            var denominator = t.N + strength;
            rate = denominator > 0 ? (t.Wins + (strength * rate)) / denominator : rate;
            win = Shrink(t.WinSum, t.WinCount, win, strength);
            nonWin = Shrink(t.NonWinSum, t.NonWinCount, nonWin, strength);
        }

        return new GroupPriors(rate, win, nonWin);
    }

    private static double? Shrink(double sum, int count, double? parentMean, double strength)
    {
        if (parentMean is null)
        {
            return null;
        }

        var denominator = count + strength;
        return denominator > 0 ? (sum + (strength * parentMean.Value)) / denominator : parentMean;
    }

    private sealed class Tally
    {
        public int N { get; private set; }

        public int Wins { get; private set; }

        public int WinCount { get; private set; }

        public double WinSum { get; private set; }

        public int NonWinCount { get; private set; }

        public double NonWinSum { get; private set; }

        public void Add(double r)
        {
            N++;
            if (r > 0)
            {
                Wins++;
                WinCount++;
                WinSum += r;
            }
            else
            {
                NonWinCount++;
                NonWinSum += r;
            }
        }
    }

    private sealed class Pool
    {
        public Tally All { get; } = new();

        public Dictionary<string, Tally> Strategies { get; } = new(StringComparer.Ordinal);

        public void Add(ReportRow row)
        {
            All.Add(row.R);
            if (!Strategies.TryGetValue(row.Trade.Strategy, out var tally))
            {
                tally = new Tally();
                Strategies[row.Trade.Strategy] = tally;
            }

            tally.Add(row.R);
        }
    }

    private sealed class Bucket(GroupKey key)
    {
        public GroupKey Key { get; } = key;

        public List<ReportRow> Rows { get; } = [];
    }

    /// <summary>Finds or makes the bucket of a combination of values without building a string per trade: the key is written into a reused buffer and looked up as a span.</summary>
    private sealed class Bucketer(IReadOnlyList<ReportDimension> dimensions)
    {
        private readonly Dictionary<string, Bucket> _buckets = new(StringComparer.Ordinal);
        private char[] _buffer = new char[128];

        public string?[] Values { get; } = new string?[dimensions.Count];

        public IEnumerable<Bucket> Buckets => _buckets.Values;

        public int Count => _buckets.Count;

        public void Add(ReportRow row)
        {
            var length = 0;
            foreach (var value in Values)
            {
                length += (value?.Length ?? 1) + 1;
            }

            if (_buffer.Length < length)
            {
                _buffer = new char[length * 2];
            }

            var position = 0;
            foreach (var value in Values)
            {
                if (value is null)
                {
                    _buffer[position++] = '\u0002';
                }
                else
                {
                    value.CopyTo(0, _buffer, position, value.Length);
                    position += value.Length;
                }

                _buffer[position++] = '\u0001';
            }

            var lookup = _buckets.GetAlternateLookup<ReadOnlySpan<char>>();
            var span = _buffer.AsSpan(0, position);
            if (!lookup.TryGetValue(span, out var bucket))
            {
                bucket = new Bucket(new GroupKey(new ValueList<GroupPart>(Values.Select((v, i) => new GroupPart(dimensions[i], v)))));
                lookup[span] = bucket;
            }

            bucket.Rows.Add(row);
        }
    }
}
