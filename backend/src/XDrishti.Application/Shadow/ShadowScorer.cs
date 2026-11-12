using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.History;
using XDrishti.Application.Abstractions.Shadow;
using XDrishti.Application.Learning;
using XDrishti.Application.Market.Actions;
using XDrishti.Application.Market.History;
using XDrishti.Application.Planning;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Shadow;

namespace XDrishti.Application.Shadow;

/// <summary>
/// Scores every candidate of a shadow session with the champion and the challengers (CF-10e part 10e-1, ADR 0281). The features are those of the decision at the close of the last session before the candidate's session, in catalog <see cref="TradeFeatureCatalog.Version"/>: the stored
/// bar features come from the extractor port and are given only bars that start before that close (nothing later is even read); the cell features are the newest stored cell statistics whose as-of date is before the session. A candidate whose features cannot be built, or whose
/// required features are unknown, gets no score row; nothing is imputed. At most <see cref="MaxModels"/> models are scored (the champion first, then the newest challengers; the rest are skipped and logged). Read only: it returns the scores, the writer stores them with the session.
/// </summary>
internal sealed class ShadowScorer(
    IModelRegistry registry,
    IModelFiles files,
    IModelScorerLoader loader,
    IBasketQueries baskets,
    IBarStore bars,
    IBackfillSeams seams,
    IAdjustmentFactors adjustments,
    ITradeFeatureExtractor extractor,
    ICellStatsStore cells,
    TimeProvider time,
    ILogger<ShadowScorer> logger)
{
    /// <summary>The most models scored in one night.</summary>
    internal const int MaxModels = 5;

    private const int ModelLimit = 200;

    // shortcut: the bars are read from this many calendar days before the decision, not from the start of history as the training extraction does. Two years warm every indicator of the catalog on timeframes up to the weekly one (EMA50 on weekly bars needs about 350 days);
    // a monthly cell has too few bars, its required features stay unknown and it gets no score (fail closed). The nightly time is logged (shadow.score).
    private const int LookbackDays = 730;

    private Dictionary<string, Guid>? _instruments;

    public async ValueTask<IReadOnlyList<NewShadowScore>> ScoreAsync(DateOnly session, TradingCalendar calendar, IReadOnlyList<ShadowEntry> entries, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
        {
            return [];
        }

        var scorers = await LoadModelsAsync(cancellationToken);
        if (scorers.Count == 0)
        {
            return [];
        }

        var started = time.GetTimestamp();
        _instruments ??= await InstrumentsAsync(cancellationToken);
        var instruments = _instruments;
        var statsRows = CashCells.Only(await cells.ListLatestBeforeAsync(session, null, CellWindow.All.Code(), cancellationToken), "shadow_score", logger); // cash cells only (ADR 0193)
        var stats = new Dictionary<string, double[]>(StringComparer.Ordinal);
        foreach (var s in statsRows.Where(r => r.Row.Window == CellWindow.All))
        {
            var r = s.Row;
            stats[r.Cell.Key] = [r.N, r.WinRatePosterior, r.WinRateLower, r.ExpectancyR ?? double.NaN, r.Enough ? 1 : 0];
        }

        var information = TicketReplayer.Before(calendar, session);
        var decisionEpoch = new DateTimeOffset(information.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset).ToUnixTimeSeconds() + (SessionClock.CloseMinute * 60L);
        var until = DateTimeOffset.FromUnixTimeSeconds(decisionEpoch - 60); // the last minute that starts before the decision
        var result = new List<NewShadowScore>();
        var missing = 0;
        foreach (var group in entries.Where(e => instruments.ContainsKey(e.Symbol)).GroupBy(e => instruments[e.Symbol]))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var decisions = new List<TradeFeatureDecision>();
            foreach (var e in group)
            {
                if (CellId.TryParse(e.CellKey) is { } cell && Timeframes.TryParse(cell.Timeframe, out var timeframe) && e.Stop is { } stop && (e.Side == "BUY" || e.Side == "SELL"))
                {
                    decisions.Add(new TradeFeatureDecision(e.TradeKey, timeframe, decisionEpoch, e.Side == "BUY" ? Domain.Research.Side.Buy : Domain.Research.Side.Sell, (double)stop, e.Target is { } t ? (double)t : null));
                }
            }

            if (decisions.Count == 0)
            {
                continue;
            }

            // shortcut: the plain read with the high-water cut and the plain factors (ADR 0273 decision 6 lets only the run reader admit fresh pre-seam bars, and an architecture test pins it). It matches training for every instrument whose run did not admit pre-seam bars;
            // for an admitted one the 730-day window lies after the seam in practice, so the bars are the same.
            var raw = await SeamAwareBars.ReadAsync(bars, seams, group.Key, until.AddDays(-LookbackDays), until, cancellationToken);
            if (raw.Count == 0)
            {
                missing += decisions.Count;
                continue;
            }

            var values = extractor.Extract(raw, await adjustments.GetAsync(group.Key, cancellationToken), calendar, decisions);
            foreach (var e in group)
            {
                if (!values.TryGetValue(e.TradeKey, out var stored) || stored.Length != TradeFeatureCatalog.StoredCount || !stats.TryGetValue(e.CellKey, out var cell))
                {
                    missing++;
                    continue;
                }

                var vector = new double[TradeFeatureCatalog.All.Count];
                stored.CopyTo(vector, 0);
                cell.CopyTo(vector, TradeFeatureCatalog.StoredCount);
                if (TradeFeatureCatalog.DefaultRequired.Any(n => !double.IsFinite(vector[TradeFeatureCatalog.IndexOf(n)])))
                {
                    missing++;
                    continue;
                }

                var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', vector.Select(v => v.ToString("R", CultureInfo.InvariantCulture))))));
                var row = new ValidationRow("score", 0, 0, default, 0, 0, string.Empty, [.. vector]);
                foreach (var (model, scorer) in scorers)
                {
                    var p = scorer.Score(row);
                    if (double.IsFinite(p) && p is >= 0 and <= 1)
                    {
                        result.Add(new NewShadowScore(e.TradeKey, model.Id, model.Model.Sha256, TradeFeatureCatalog.Version, p, hash));
                    }
                }
            }
        }

        var elapsedMs = (int)time.GetElapsedTime(started).TotalMilliseconds;
        ShadowLog.Scored(logger, scorers.Count, result.Count, missing, elapsedMs);
        return result;
    }

    /// <summary>The champion, then the newest challengers, of the catalog version and width this scorer builds, whose file loads; at most <see cref="MaxModels"/>.</summary>
    private async Task<List<(StoredModel Model, IExplainableScorer Scorer)>> LoadModelsAsync(CancellationToken cancellationToken)
    {
        var candidates = new List<StoredModel>();
        candidates.AddRange(await registry.ListAsync(ModelRole.Champion, ModelLimit, cancellationToken));
        candidates.AddRange(await registry.ListAsync(ModelRole.Challenger, ModelLimit, cancellationToken));
        candidates = [.. candidates.Where(m => m.Model.FeatureVersion == TradeFeatureCatalog.Version && m.Model.FeatureWidth == TradeFeatureCatalog.All.Count)];
        if (candidates.Count > MaxModels)
        {
            ShadowLog.ModelsSkipped(logger, candidates.Count - MaxModels);
            candidates = candidates.Take(MaxModels).ToList();
        }

        var loaded = new List<(StoredModel, IExplainableScorer)>();
        foreach (var m in candidates)
        {
            var bytes = await files.ReadAsync(m.Model.Sha256, cancellationToken);
            var scorer = bytes.IsSuccess ? loader.Load(bytes.Value, m.Model.Sha256) : null;
            if (scorer is { IsSuccess: true })
            {
                loaded.Add((m, scorer.Value));
            }
        }

        return loaded;
    }

    private async Task<Dictionary<string, Guid>> InstrumentsAsync(CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var basket in await baskets.ListAsync(false, cancellationToken))
        {
            if (await baskets.MembersAsync(basket.Id, null, null, cancellationToken) is { } members)
            {
                foreach (var m in members.Items)
                {
                    result.TryAdd(m.Instrument.Symbol, m.Instrument.Id);
                }
            }
        }

        return result;
    }
}
