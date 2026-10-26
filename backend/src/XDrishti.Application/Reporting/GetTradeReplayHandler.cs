using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Reporting;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Market.Actions;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Reporting;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Reporting;

/// <summary>
/// One stored trade on its own bars (P8-11, ADR 0125). Reads the stored trade, reads the 1-minute bars of its window by range, adjusts them by the factors in force, builds the chosen timeframe with the domain aggregator, checks that the stored entry still fits the bars,
/// and adds the stored features. Read only: no write, no order. A window above 5,000 bars is refused, never thinned; net figures appear only for a charge sheet the owner confirmed.
/// </summary>
internal sealed class GetTradeReplayHandler(
    IRunStore runs,
    ITradeFeatureStore features,
    IBarStore bars,
    IBarQueries barQueries,
    IAdjustmentFactors adjustments,
    ITradingCalendar calendar,
    IChargeSheetConfirmationSource confirmations,
    TimeProvider time,
    ILogger<GetTradeReplayHandler> logger) : IQueryHandler<GetTradeReplayQuery, Result<TradeReplayView>>
{
    private static readonly string[] BaseLimits =
    [
        "A simulated trade on replayed history with modelled fills: the chart shows the bars the trade was simulated on, not a fill you got.",
        "The prices are adjusted for the corporate actions applied now; if the data changed since the run the picture and the stored trade may disagree (the entry check catches the common case, not every case).",
        "A trailing exit's level path is not stored, so none is drawn; the stored record has the legs and the exit reason.",
    ];

    private const string MultiSessionLimit = "The time axis skips nights and weekends; overnight gaps are not drawn to scale.";

    public async ValueTask<Result<TradeReplayView>> HandleAsync(GetTradeReplayQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var started = time.GetTimestamp();
        var asked = Validate(query);
        if (asked.IsFailure)
        {
            return Result.Failure<TradeReplayView>(asked.Error);
        }

        var (timeframe, before, after) = asked.Value;
        var stored = await runs.ReadTradeAsync(query.RunKey, query.OpenAtEnd, query.Ordinal, cancellationToken);
        if (stored is null)
        {
            return Result.Failure<TradeReplayView>(ReportErrors.TradeNotFound());
        }

        var trade = stored.Trade;
        var tradingCalendar = await calendar.GetAsync(cancellationToken);
        var window = TradeReplayWindow.Compute(tradingCalendar, trade.TradeDate, SessionClock.DateOf(trade.ExitEpochSeconds), before, after);
        var raw = await bars.ReadAsync(trade.InstrumentId, DateTimeOffset.FromUnixTimeSeconds(window.FromEpoch), DateTimeOffset.FromUnixTimeSeconds(window.ToEpoch), cancellationToken);
        var factors = await adjustments.GetAsync(trade.InstrumentId, cancellationToken);
        var minutes = ReplayBars.Adjust(raw, factors);
        var viewBars = ReplayBars.Aggregate(minutes, timeframe);
        if (viewBars.Count > TradeReplayWindow.MaxBars)
        {
            return Result.Failure<TradeReplayView>(ReportErrors.WindowTooLarge(viewBars.Count, TradeReplayWindow.MaxBars));
        }

        var instrument = await barQueries.FindInstrumentAsync(trade.InstrumentId, cancellationToken);
        var tick = TickSpec.FromMasterPaise(instrument?.TickSize);
        var tickSize = (double)(tick.IsSuccess ? tick.Value.Size : TickSpec.FallbackSize);
        var coherence = ReplayCoherence.Check(minutes, trade.EntryEpochSeconds, trade.EntryPrice, tickSize, trade.EntryFill == EntryFill.AtLimit);

        var row = await features.ReadOneAsync(query.RunKey, trade.Key, TradeFeatureCatalog.Version, cancellationToken);
        var costing = await CostingAsync(query.RunKey, trade, cancellationToken);
        var confirmed = costing?.Confirmed == true;
        var multi = trade.SessionsHeld > 1 || window.FromDate != window.ToDate;
        IReadOnlyList<string> limits = multi ? [.. BaseLimits, MultiSessionLimit] : BaseLimits;
        var chart = new ReplayChartView(
            timeframe.ToCode(),
            true,
            factors.Actions.Count == 0 ? null : factors.Actions.Max(a => a.ExDate),
            DateTimeOffset.FromUnixTimeSeconds(window.FromEpoch),
            DateTimeOffset.FromUnixTimeSeconds(window.ToEpoch),
            viewBars.Count,
            viewBars,
            new ReplayCoherenceView(ReplayNames.Name(coherence.State), coherence.EntryStored, coherence.EntryOnBars, coherence.Detail));
        var view = new TradeReplayView(
            query.RunKey,
            query.Ordinal,
            query.OpenAtEnd,
            trade.Key,
            confirmed ? "net" : "gross",
            TradeView(stored),
            [.. (trade.Legs is { } legs ? legs.AsEnumerable() : []).Select(l => new ReplayLegView(DateTimeOffset.FromUnixTimeSeconds(l.EpochSeconds), ReplayNames.Name(l.Kind), l.Price, l.FilledPrice, l.Share, l.Reason))],
            costing?.View,
            chart,
            ReplayFeatures.Build(row, trade.DecisionEpochSeconds),
            Evidence(query.RunKey, trade, confirmed, costing?.Confirmation, limits),
            limits);
        var milliseconds = (long)time.GetElapsedTime(started).TotalMilliseconds;
        var prefix = query.RunKey[..8];
        ReportLog.Replay(logger, prefix, query.Ordinal, chart.Timeframe, viewBars.Count, milliseconds);
        return Result.Success(view);
    }

    private static Result<(Timeframe Timeframe, int Before, int After)> Validate(GetTradeReplayQuery query)
    {
        if (query.RunKey is not { Length: 64 } || !query.RunKey.All(Uri.IsHexDigit))
        {
            return Invalid("runKey is a 64-character hexadecimal run key.");
        }

        if (query.Ordinal < 0)
        {
            return Invalid("ordinal is not negative.");
        }

        var timeframe = Timeframe.M5;
        if (query.Timeframe is not null && (!Timeframes.TryParse(query.Timeframe, out timeframe) || !TradeReplayWindow.IsAllowed(timeframe)))
        {
            return Invalid("timeframe is one of 1m, 5m, 15m, 1h, 1D.");
        }

        var max = TradeReplayWindow.MaxSessions(timeframe);
        var before = query.Before ?? TradeReplayWindow.DefaultBefore(timeframe);
        var after = query.After ?? TradeReplayWindow.DefaultAfter(timeframe);
        if (before < 0 || before > max)
        {
            return Invalid(string.Create(CultureInfo.InvariantCulture, $"before is 0 to {max} sessions for {timeframe.ToCode()}."));
        }

        return after < 0 || after > max
            ? Invalid(string.Create(CultureInfo.InvariantCulture, $"after is 0 to {max} sessions for {timeframe.ToCode()}."))
            : Result.Success((timeframe, before, after));
    }

    private static Result<(Timeframe Timeframe, int Before, int After)> Invalid(string message) =>
        Result.Failure<(Timeframe, int, int)>(ReportErrors.Invalid(message));

    private static ReplayTradeView TradeView(RunTrade stored)
    {
        var t = stored.Trade;
        return new ReplayTradeView(
            t.StrategyId,
            t.InstrumentId,
            t.Symbol,
            t.Side.ToString().ToUpperInvariant(),
            t.TradeDate,
            t.TriggerTimeframe.ToCode(),
            DateTimeOffset.FromUnixTimeSeconds(t.DecisionEpochSeconds),
            DateTimeOffset.FromUnixTimeSeconds(t.EntryEpochSeconds),
            t.EntryPrice,
            t.EntryFilledPrice,
            t.StopPrice,
            t.TargetPrice,
            DateTimeOffset.FromUnixTimeSeconds(t.ExitEpochSeconds),
            t.ExitPrice,
            t.ExitFilledPrice,
            ReplayNames.Name(t.ExitReason),
            t.RExact,
            t.RFilled,
            t.SlippageR,
            t.MaeR,
            t.MfeR,
            t.SessionsHeld,
            t.SessionsUnseen,
            t.Fills.ToString().ToLowerInvariant(),
            ReplayNames.Name(t.EntryFill),
            t.TickFallback,
            [.. stored.BasketIds.Select(b => b.ToString("D"))]);
    }

    /// <summary>The figures of the costing only for a sheet and rates the owner confirmed (the stored confirmation, nothing from the request).</summary>
    private async ValueTask<CostingResult?> CostingAsync(string runKey, SimTrade trade, CancellationToken cancellationToken)
    {
        if (trade.Costing is not { } costing)
        {
            return null;
        }

        var confirmation = await confirmations.FindAsync(cancellationToken);
        var header = confirmation is null ? null : await runs.FindAsync(runKey, cancellationToken);
        var confirmed = confirmation is not null && header is not null
            && string.Equals(confirmation.SheetId, costing.SheetId, StringComparison.Ordinal)
            && string.Equals(confirmation.RatesSha256, header.CostsRatesSha256, StringComparison.Ordinal);
        return new CostingResult(
            confirmed ? new ReplayCostingView(costing.SheetId, true, costing.Quantity, costing.TotalCost, costing.CostR, costing.RNet) : new ReplayCostingView(costing.SheetId, false, null, null, null, null),
            confirmed,
            confirmed ? confirmation?.Text : null);
    }

    private static ReportEvidenceView Evidence(string runKey, SimTrade trade, bool net, string? confirmation, IReadOnlyList<string> limits) =>
        new(net ? "net" : "gross", 1, trade.TradeDate, SessionClock.DateOf(trade.ExitEpochSeconds), false, [runKey], [], false, limits, [], 0, 0, confirmation);

    private sealed record CostingResult(ReplayCostingView View, bool Confirmed, string? Confirmation);
}
