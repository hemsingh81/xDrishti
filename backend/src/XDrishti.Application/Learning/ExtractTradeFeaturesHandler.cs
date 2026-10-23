using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Market.Actions;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Learning;

/// <summary>
/// Extracts the decision-time features of the closed trades of a stored run (spec P6-04, ADR 0066): one instrument at a time it reads the stored bars and the applied corporate actions, asks the extractor port for the features of that instrument's trades
/// and appends them to <c>research.trade_features</c>. A trade is described to the extractor by its decision instant, side, stop and target only, so nothing of its outcome can reach a feature. Idempotent; logs counts only.
/// </summary>
internal sealed class ExtractTradeFeaturesHandler(
    IRunStore runs,
    IBarStore bars,
    IAdjustmentFactors adjustments,
    ITradingCalendar calendars,
    ITradeFeatureExtractor extractor,
    ITradeFeatureStore store,
    TimeProvider time,
    ILogger<ExtractTradeFeaturesHandler> logger) : ICommandHandler<ExtractTradeFeaturesCommand, TradeFeaturesOutcome>
{
    public async ValueTask<Result<TradeFeaturesOutcome>> HandleAsync(ExtractTradeFeaturesCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var problems = new List<FieldError>();
        if (!RunRecordText.IsHex64(command.RunKey))
        {
            problems.Add(new FieldError("runKey", "A run key is 64 lower-case hex characters."));
        }

        if (problems.Count > 0)
        {
            FeatureLog.Refused(logger, LearningErrors.InvalidCode);
            return Result.Failure<TradeFeaturesOutcome>(LearningErrors.Invalid(problems));
        }

        if (await runs.FindAsync(command.RunKey, cancellationToken) is null)
        {
            FeatureLog.Refused(logger, LearningErrors.RunNotFoundCode);
            return Result.Failure<TradeFeaturesOutcome>(LearningErrors.RunNotFound(command.RunKey));
        }

        var started = time.GetTimestamp();
        var trades = new List<SimTradeView>();
        await foreach (var runTrade in runs.StreamTradesAsync(command.RunKey, false, cancellationToken))
        {
            var t = runTrade.Trade;
            trades.Add(new SimTradeView(t.InstrumentId, t.Key, t.StrategyId, t.TriggerTimeframe, t.Side, t.TradeDate, t.DecisionEpochSeconds, t.StopPrice, t.TargetPrice));
        }

        var runKey = command.RunKey;
        var calendar = await calendars.GetAsync(cancellationToken);
        var rows = new List<TradeFeatureRow>(trades.Count);
        var noBars = 0;
        try
        {
            foreach (var group in trades.GroupBy(t => t.InstrumentId))
            {
                var last = group.Max(t => SessionClock.DateOf(t.DecisionEpochSeconds));
                var raw = await bars.ReadAsync(group.Key, SimulationBounds.From, SimulationBounds.Until(last), cancellationToken);
                if (raw.Count == 0)
                {
                    noBars += group.Count();
                    continue;
                }

                var factors = await adjustments.GetAsync(group.Key, cancellationToken);
                var decisions = group.Select(t => new TradeFeatureDecision(t.Key, t.Timeframe, t.DecisionEpochSeconds, t.Side, t.StopPrice, t.TargetPrice)).ToList();
                var values = extractor.Extract(raw, factors, calendar, decisions);
                foreach (var t in group)
                {
                    if (values.TryGetValue(t.Key, out var v))
                    {
                        rows.Add(new TradeFeatureRow(runKey, t.Key, t.Strategy, t.Timeframe.ToCode(), t.Side.ToString().ToUpperInvariant(), t.TradeDate, t.DecisionEpochSeconds, v));
                    }
                }
            }

            var stored = await store.StoreAsync(rows, time.GetUtcNow(), cancellationToken);
            var elapsedMs = (long)time.GetElapsedTime(started).TotalMilliseconds;
            FeatureLog.Extracted(logger, runKey[..12], trades.Count, stored, noBars, elapsedMs);
            return Result.Success(new TradeFeaturesOutcome(trades.Count, stored, noBars));
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            FeatureLog.Failed(logger, runKey[..12]);
            throw;
        }
    }

    private sealed record SimTradeView(Guid InstrumentId, string Key, string Strategy, Timeframe Timeframe, Domain.Research.Side Side, DateOnly TradeDate, long DecisionEpochSeconds, double StopPrice, double? TargetPrice);
}
