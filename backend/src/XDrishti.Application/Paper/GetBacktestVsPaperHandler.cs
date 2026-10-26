using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Paper;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Reporting;

namespace XDrishti.Application.Paper;

/// <summary>
/// Backtest versus paper (spec P10-04, ADR 0122, R14): the counted paper trades of an epoch against a composition-matched bootstrap of the backtest trades known before the epoch started. Computes on read (deterministic, nothing stored); it
/// depends on no write port and not on the charge-sheet confirmation source, only on <see cref="ICostConfirmationFact"/>.
/// </summary>
internal sealed class GetBacktestVsPaperHandler(
    IPaperLedgerData ledger,
    IBacktestPaperData paperData,
    BacktestReferenceReader references,
    IGradeReader grades,
    ICostConfirmationFact costs,
    TimeProvider time,
    ILogger<GetBacktestVsPaperHandler> logger) : IQueryHandler<GetBacktestVsPaperQuery, Result<BacktestVsPaperView>>
{
    private const int GradeLimit = 5_000;
    private const string Statement = "simulated on the official 1-minute bars; no order was placed or exists. Both sides are simulated: agreement shows the pipeline behaves as the research did, not that real fills will.";

    private static readonly string[] Limits =
    [
        "The reference is simulated (modelled fills, in-sample, no hold-out, members as of today); the paper side is simulated too.",
        "Cells were chosen because they looked good on this very history, so the reference mean is optimistic and the band is a stricter test than a fair-coin world.",
        "Draws are independent; trades of one session are correlated, so the intervals are somewhat too narrow. The percentile bootstrap is approximate below about 50 trades.",
        "With 60 trades and a spread near 1.5R the 90% interval is about plus or minus 0.3R: a met statistic is weak evidence, and the label says when the interval includes 0.",
        "Regime shifts and a changing market are not modelled.",
    ];

    public async ValueTask<Result<BacktestVsPaperView>> HandleAsync(GetBacktestVsPaperQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var options = new BandOptions(query.Draws ?? BandOptions.DefaultDraws);
        if (!options.IsValid)
        {
            CompareLog.Refused(logger, PaperErrors.InvalidCode);
            return Result.Failure<BacktestVsPaperView>(PaperErrors.Invalid("draws", string.Create(CultureInfo.InvariantCulture, $"Use {BandOptions.MinDraws} to {BandOptions.MaxDraws} draws.")));
        }

        if (query.EpochId is <= 0)
        {
            CompareLog.Refused(logger, PaperErrors.InvalidCode);
            return Result.Failure<BacktestVsPaperView>(PaperErrors.Invalid("epoch", "The epoch id must be positive."));
        }

        var epoch = await ledger.EpochAsync(query.EpochId, cancellationToken);
        if (epoch is null)
        {
            if (query.EpochId is not null)
            {
                CompareLog.Refused(logger, PaperErrors.NotFoundCode);
                return Result.Failure<BacktestVsPaperView>(PaperErrors.NotFound("paper epoch with this id"));
            }

            return Result.Success(Empty());
        }

        var started = time.GetTimestamp();
        var data = await paperData.ReadAsync(epoch.Id, cancellationToken);
        var set = CountedSet.Select(data.Rows, data.OtherEpochs);
        var trades = set.Trades;
        var n = trades.Count;
        CompareLog.Read(logger, epoch.Id, n, set.Counts.Reserve, set.Counts.Straddle, set.Counts.Open);

        var basis = n == 0 ? "none" : trades.All(t => t.RBasis == "net") ? "net" : trades.All(t => t.RBasis == "gross") ? "gross" : "mixed";
        var sheet = await SheetAsync(trades, cancellationToken);
        var reference = n < PaperExitStatistics.ProvisionalFrom ? ReferenceRead.Unavailable("not read: too few counted trades") : await references.ReadAsync(epoch.StartedOn, cancellationToken);
        var gate = n < PaperExitStatistics.ProvisionalFrom ? new BandGate("insufficient", string.Create(CultureInfo.InvariantCulture, $"{n} counted trades; at least {PaperExitStatistics.ProvisionalFrom} are needed")) : null;
        var analysis = CompareCells.Analyze(trades, reference);
        if (gate is null && reference.Problem is not null)
        {
            gate = new BandGate("unavailable", reference.Problem);
            if (reference.SnapshotKey is null)
            {
                CompareLog.NoSnapshot(logger, epoch.Id, epoch.StartedOn);
            }
            else
            {
                CompareLog.ReferenceUnreadable(logger, reference.SnapshotKey[..Math.Min(12, reference.SnapshotKey.Length)], "unreadable");
            }
        }
        else if (gate is null && analysis.Gate is { } cellGate)
        {
            gate = cellGate;
            if (cellGate.State == "not_evaluable")
            {
                CompareLog.BasisDiffers(logger, epoch.Id, analysis.Views.Count(v => v.State == "basis_differs"));
            }
            else
            {
                CompareLog.ThinReference(logger, epoch.Id, analysis.Views.Count(v => v.State is "no_reference" or "thin_reference"));
            }
        }

        var key = reference.SnapshotKey ?? "none";
        var results = trades.Select(t => t.R).ToList();
        BandResult? band = null;
        if (gate is null)
        {
            band = BacktestBand.Compute(
                [.. trades.Select(t => new PaperSlot(t.CellKey, t.R))], analysis.References, options,
                BacktestBand.SeedOf(epoch.Id, key, n), BacktestBand.IntervalSeedOf(epoch.Id, key, n));
        }

        var netBasis = basis == "net";
        var criteria = PaperExitStatistics.Evaluate(n, netBasis, sheet.Confirmed && netBasis, band, gate?.Reason);
        if (band is not null)
        {
            var elapsedMs = (long)time.GetElapsedTime(started).TotalMilliseconds;
            CompareLog.Drawn(logger, epoch.Id, n, options.Draws, band.Position, band.DrawdownPosition, elapsedMs);
        }

        if (criteria.EarlyWarning is not null)
        {
            CompareLog.EarlyWarning(logger, epoch.Id);
        }

        CompareLog.Criteria(logger, epoch.Id, criteria.Expectancy.Verdict, criteria.Drawdown.Verdict);
        var view = new BacktestVsPaperView(
            epoch.Id, n, new CompareCountsView(set.Counts.Counted, set.Counts.Reserve, set.Counts.Straddle, set.Counts.Open, set.Counts.OtherEpochs), basis,
            new CompareChargeSheetView(sheet.Id, sheet.ConfirmedOn),
            PaperSide(results, band, options, epoch.Id, key),
            BandSide(band, gate, n, options),
            DrawdownSide(band, gate, n),
            new CompareCriteriaView(new CompareVerdictView(criteria.Expectancy.Verdict, criteria.Expectancy.Label), new CompareVerdictView(criteria.Drawdown.Verdict, criteria.Drawdown.Label)),
            criteria.EarlyWarning,
            analysis.Views,
            band is null ? [] : [.. band.Fan.Select(f => new CompareFanPointView(f.Index, f.P05, f.P50, f.P95, f.Paper))],
            await SensitivityAsync(epoch, data.NextEpochStart, cancellationToken),
            new CompareEvidenceView(epoch.StartedOn, trades.Count == 0 ? null : IndiaTime.Today(trades[0].ExitAt), trades.Count == 0 ? null : IndiaTime.Today(trades[^1].ExitAt), reference.SnapshotKey, reference.AsOf, reference.RunKeys, Statement),
            [.. PaperWording.Limits, .. Limits]);
        return Result.Success(view);
    }

    private static MetricView Figure(double value, int n) => ReportViews.Metric(Shape(Metric.Of(value), n));

    private static Metric Shape(Metric metric, int n) =>
        n < PaperExitStatistics.ValueFrom ? metric.AsProvisional(string.Create(CultureInfo.InvariantCulture, $"interim: {n} counted trades, {PaperExitStatistics.ValueFrom} are needed for a value")) : metric;

    private static MetricView Gone(string state, string reason) => ReportViews.Metric(state switch
    {
        "unavailable" => Metric.Unavailable(reason),
        "not_evaluable" => Metric.NotEvaluable(reason),
        _ => Metric.Insufficient(reason),
    });

    private static MetricView Few(int n) => Gone("insufficient", string.Create(CultureInfo.InvariantCulture, $"{n} counted trades; at least {PaperExitStatistics.ProvisionalFrom} are needed"));

    private static ComparePaperView PaperSide(List<double> results, BandResult? band, BandOptions options, long epochId, string key)
    {
        var n = results.Count;
        if (n < PaperExitStatistics.ProvisionalFrom)
        {
            return new ComparePaperView(Few(n), new CompareIntervalView(Few(n), Few(n)), Few(n));
        }

        var (lo, hi) = band is null
            ? BacktestBand.PaperInterval(results, options.Draws, BacktestBand.IntervalSeedOf(epochId, key, n))
            : (band.IntervalLow, band.IntervalHigh);
        return new ComparePaperView(
            Figure(BacktestBand.PaperMean(results), n),
            new CompareIntervalView(Figure(lo, n), Figure(hi, n)),
            Figure(band?.PaperMaxDrawdown ?? XDrishti.Domain.Lab.MonteCarlo.MaxDrawdown(results), n));
    }

    private static CompareBandView BandSide(BandResult? band, BandGate? gate, int n, BandOptions options)
    {
        if (band is null)
        {
            var g = gate ?? new BandGate("insufficient", "no band");
            var gone = Gone(g.State, g.Reason);
            return new CompareBandView(gone, gone, gone, gone, null, options.Draws, null);
        }

        return new CompareBandView(Figure(band.BandP05, n), Figure(band.BandP50, n), Figure(band.BandP95, n), Figure(band.Rank, n), band.Position, band.Draws, band.Seed.ToString(CultureInfo.InvariantCulture));
    }

    private static CompareDrawdownView DrawdownSide(BandResult? band, BandGate? gate, int n)
    {
        if (band is null)
        {
            var g = gate ?? new BandGate("insufficient", "no band");
            var gone = Gone(g.State, g.Reason);
            return new CompareDrawdownView(gone, gone, gone, null);
        }

        return new CompareDrawdownView(Figure(band.DrawdownP50, n), Figure(band.DrawdownP90, n), Figure(band.DrawdownP95, n), band.DrawdownPosition);
    }

    private async ValueTask<(string? Id, DateOnly? ConfirmedOn, bool Confirmed)> SheetAsync(IReadOnlyList<CountedTrade> trades, CancellationToken cancellationToken)
    {
        var ids = trades.Select(t => t.ChargeSheetId).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (ids.Count == 0)
        {
            return (null, null, false);
        }

        DateOnly? latest = null;
        var all = true;
        foreach (var id in ids)
        {
            var on = await costs.ConfirmedOnAsync(id, cancellationToken);
            all &= on is not null;
            latest = on is { } d && (latest is null || d > latest) ? d : latest;
        }

        return (string.Join(", ", ids), all ? latest : null, all);
    }

    private async ValueTask<CompareSensitivityView> SensitivityAsync(EpochRow epoch, DateOnly? nextStart, CancellationToken cancellationToken)
    {
        const string label = "simulated on plan prices, all proposals";
        var page = await grades.ReportItemsAsync(epoch.StartedOn, nextStart?.AddDays(-1), GradeLimit, cancellationToken);
        var r = page.Items.Where(i => i.Group == TicketGroup.Primary && i.Grade.HasR).Select(i => i.Grade.R!.Value).ToList();
        if (r.Count == 0)
        {
            return new CompareSensitivityView(label, 0, ReportViews.Metric(Metric.Empty("no graded primary ticket in this epoch")));
        }

        var mean = r.Average();
        return new CompareSensitivityView(label, r.Count, r.Count < PaperExitStatistics.ProvisionalFrom
            ? ReportViews.Metric(Metric.Insufficient(string.Create(CultureInfo.InvariantCulture, $"{r.Count} graded tickets; at least {PaperExitStatistics.ProvisionalFrom} are needed")))
            : Figure(mean, r.Count));
    }

    private static BacktestVsPaperView Empty()
    {
        var none = ReportViews.Metric(Metric.Empty("no paper epoch yet"));
        var verdict = new CompareVerdictView(CriterionResult.NotEvaluable, "not evaluable: no paper epoch yet");
        return new BacktestVsPaperView(
            null, 0, new CompareCountsView(0, 0, 0, 0, 0), "none", new CompareChargeSheetView(null, null),
            new ComparePaperView(none, new CompareIntervalView(none, none), none), new CompareBandView(none, none, none, none, null, BandOptions.DefaultDraws, null),
            new CompareDrawdownView(none, none, none, null), new CompareCriteriaView(verdict, verdict), null, [], [],
            new CompareSensitivityView("simulated on plan prices, all proposals", 0, none),
            new CompareEvidenceView(null, null, null, null, null, [], Statement), [.. PaperWording.Limits, .. Limits]);
    }
}
