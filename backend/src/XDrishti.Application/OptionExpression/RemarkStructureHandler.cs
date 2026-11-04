using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.OptionCosts;
using XDrishti.Application.Abstractions.OptionExpression;
using XDrishti.Application.Abstractions.OptionQuality;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Options;
using XDrishti.Domain.Options.Remark;
using XDrishti.Domain.Options.Structures;
using XDrishti.Domain.Research;

namespace XDrishti.Application.OptionExpression;

/// <summary>
/// <c>xd options remark</c> for a <c>debit_spread</c> or <c>credit_spread</c> profile (spec P14-10, ADR 0200): re-marks every trade of a stored cash run as a two-leg defined-risk structure, one row per trade (paired, or unpaired with one reason) and one row per leg of a paired one.
/// <b>Gates before any bar is read:</b> the profile must fit the underlying (<c>width_outside_window</c> for a share, a credit spread for an index only), and a credit spread needs a stored regime (no label: refused <c>structure.regime_unavailable</c>, the reader never called) and a measured
/// half-spread model (an underlying without one has every trade <c>spread_unavailable</c>, never costed on an assumption). A credit spread trades only a session whose previous session was labelled <c>range</c> (every other is <c>not_range</c>). Research only: nothing here selects, plans, sizes or orders.
/// </summary>
internal sealed class RemarkStructureHandler(
    IRunStore runs,
    ICostSheetStore sheets,
    IOptionReference reference,
    IOptionQualityStore quality,
    IRollingBarReader barReader,
    IContractExpiryReader contractReader,
    ISpreadModelStore spreadModels,
    ISpreadModelSettings spreadSettings,
    IBarStore minuteBars,
    ITradingCalendar calendars,
    IRemarkSettings settings,
    IRegimeLabels regime,
    IExpressionTradeStore store,
    ILogger<RemarkStructureHandler> logger)
{
    public const string RegimeUnavailableCode = "structure.regime_unavailable";
    public const string SpreadNotMeasuredCode = "structure.spread_not_measured";
    public const string EmptyArchiveCode = "structure.empty_archive";

    private static readonly TimeSpan SpotWindowPad = TimeSpan.FromDays(1);

    public async ValueTask<Result<RemarkRunSummary>> HandleAsync(RemarkRunCommand command, StoredExpressionProfile stored, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(stored);
        var parameters = SpreadParams.FromProfile(stored.Profile);
        if (parameters.IsFailure)
        {
            return Result.Failure<RemarkRunSummary>(parameters.Error);
        }

        var profile = parameters.Value;
        var credit = profile.Kind == Expression.CreditSpread;
        var header = await runs.FindAsync(command.RunKey, cancellationToken);
        if (header is null)
        {
            return Result.Failure<RemarkRunSummary>(Error.NotFound(RemarkRunHandler.RunNotFoundCode, "No stored run has that key."));
        }

        // The regime gate comes before any trade, any bar and any spread model is read.
        if (credit && !await regime.AnyAsync(cancellationToken))
        {
            return Result.Failure<RemarkRunSummary>(Error.Validation(RegimeUnavailableCode, "A credit spread trades only after a range session and no regime label is stored (P6-02 and P3-01d are not built): NOT_EVALUABLE regime_unavailable."));
        }

        CostSheet? sheet = null;
        var sheetChanged = false;
        if (!string.IsNullOrWhiteSpace(header.CostsSheetId))
        {
            var found = await sheets.FindAsync(header.CostsSheetId, cancellationToken);
            if (found.IsFailure)
            {
                return Result.Failure<RemarkRunSummary>(found.Error);
            }

            sheet = found.Value;
            sheetChanged = !string.Equals(sheet.RatesSha256, header.CostsRatesSha256, StringComparison.Ordinal);
        }

        var cash = new List<RemarkShared.Prepared>();
        await foreach (var run in runs.StreamTradesAsync(command.RunKey, openAtEnd: false, cancellationToken))
        {
            cash.Add(RemarkShared.Prepare(run.Trade, openAtEnd: false));
        }

        await foreach (var run in runs.StreamTradesAsync(command.RunKey, openAtEnd: true, cancellationToken))
        {
            cash.Add(RemarkShared.Prepare(run.Trade, openAtEnd: true));
        }

        var symbols = await reference.SymbolsAsync([.. cash.Select(c => c.InstrumentId).Distinct()], cancellationToken);

        // The profile must fit every underlying it will touch: refused before a bar is read.
        foreach (var symbol in symbols.Values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var fits = profile.ValidateFor(symbol);
            if (fits.IsFailure)
            {
                return Result.Failure<RemarkRunSummary>(fits.Error);
            }
        }

        // Four half-spreads decide a credit spread: with no measured model (the minimum of usable sessions) for any underlying of the run it is refused, before any archive read.
        if (credit)
        {
            var measured = false;
            foreach (var id in symbols.Keys.Order())
            {
                if (await spreadModels.LatestAsync(id, cancellationToken) is { } best && best.Sessions >= spreadSettings.MinUsableSessions)
                {
                    measured = true;
                    break;
                }
            }

            if (!measured)
            {
                return Result.Failure<RemarkRunSummary>(Error.Validation(SpreadNotMeasuredCode, "A credit spread pays four half-spreads and needs a measured spread model of at least the usable-session minimum for an underlying of the run: NOT_EVALUABLE spread_not_measured."));
            }
        }

        // An archive with no bar of any underlying the run trades gives nothing to re-mark: refused, not frozen as a run of empty rows.
        var withBars = false;
        foreach (var id in symbols.Keys.Order())
        {
            withBars = await barReader.HasSeriesAsync(id, cancellationToken);
            if (withBars)
            {
                break;
            }
        }

        if (!withBars)
        {
            return Result.Failure<RemarkRunSummary>(Error.Validation(EmptyArchiveCode, "The option archive holds no rolling bar of any underlying of that run: nothing was re-marked and nothing was stored."));
        }

        RemarkLog.Start(logger, command.RunKey, command.ProfileHash[..12], cash.Count);
        var calendar = await calendars.GetAsync(cancellationToken);
        var epochs = await reference.EpochsAsync(cancellationToken);
        HashSet<DateOnly> rangeSessions = [];
        if (credit && cash.Count > 0)
        {
            rangeSessions = [.. await regime.RangeSessionsAsync(cash.Min(c => c.Trade.TradeDate).AddDays(-14), cash.Max(c => c.Trade.TradeDate), cancellationToken)];
        }

        var rows = new List<RemarkRow>();
        var legs = new List<StructureLegRow>();
        var usableKeys = new HashSet<string>(StringComparer.Ordinal);
        var spreadInputs = new List<string>();
        var allLots = new List<LotSizeEntry>();
        var allContracts = new List<ContractExpiry>();
        long watermark = 0;

        foreach (var group in cash.GroupBy(c => c.InstrumentId).OrderBy(g => g.Key))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var trades = group.ToList();
            if (!symbols.TryGetValue(group.Key, out var symbol))
            {
                rows.AddRange(trades.Select(t => Unpaired(t.Trade, t.Trade.OpenAtEnd ? RemarkFates.OpenAtEnd : RemarkFates.NoSeries, credit)));
                continue;
            }

            var lots = await reference.LotSizesAsync(group.Key, cancellationToken);
            allLots.AddRange(lots);
            var model = await spreadModels.LatestAsync(group.Key, cancellationToken);
            var assumed = spreadSettings.AssumedHalfSpreadPct(symbol);
            spreadInputs.Add(string.Create(CultureInfo.InvariantCulture, $"{group.Key:N}:{model?.ModelId ?? "none"}:{assumed?.ToString(CultureInfo.InvariantCulture) ?? "none"}"));
            if (credit && (model is null || model.Sessions < spreadSettings.MinUsableSessions))
            {
                // four half-spreads decide a credit spread: no measured model, no costed trade, no bar read.
                rows.AddRange(trades.Select(t => t.Trade.OpenAtEnd ? Unpaired(t.Trade, RemarkFates.OpenAtEnd, credit) : Unpaired(t.Trade, RemarkFates.SpreadUnavailable, credit, StructureNames.SpreadNotMeasuredLabel)));
                continue;
            }

            var first = trades.Min(t => t.Trade.TradeDate);
            var last = trades.Max(t => SessionClock.DateOf(t.Trade.ExitEpoch));
            var contracts = await contractReader.ExpiriesAsync(group.Key, first.AddDays(-7), last.AddDays(120), cancellationToken);
            allContracts.AddRange(contracts);
            var expiries = new ExpiryCalendar(symbol, contracts, epochs, calendar);
            var usable = await RemarkShared.UsableAsync(quality, group.Key, first, last, cancellationToken);

            var planned = new List<(RemarkShared.Prepared Trade, SeriesPlan Plan)>();
            foreach (var t in trades)
            {
                if (t.Trade.OpenAtEnd)
                {
                    rows.Add(Unpaired(t.Trade, RemarkFates.OpenAtEnd, credit));
                    continue;
                }

                if (credit && !(CondorSchedule.PreviousSession(calendar, t.Trade.TradeDate) is { } previous && rangeSessions.Contains(previous)))
                {
                    rows.Add(Unpaired(t.Trade, StructureNames.NotRange, credit));
                    continue;
                }

                var plannedLast = SeriesPlanner.PlannedLastSession(calendar, t.Trade.TradeDate, !t.Trade.IsBuy, profile.MaxHoldSessions);
                var choice = SeriesPlanner.Plan(symbol, t.Trade.TradeDate, plannedLast, expiries, epochs);
                if (choice.Plan is null)
                {
                    rows.Add(Unpaired(t.Trade, choice.Fate!, credit));
                }
                else
                {
                    planned.Add((t, choice.Plan));
                }
            }

            foreach (var series in planned.GroupBy(p => (p.Plan.Flag, p.Plan.Code, p.Plan.IntervalMinutes, Side: SideOf(credit, p.Trade.Trade.IsBuy))).OrderBy(g => g.Key.Flag).ThenBy(g => g.Key.Code).ThenBy(g => g.Key.Side, StringComparer.Ordinal))
            {
                foreach (var chunk in RemarkShared.Chunks([.. series.OrderBy(p => p.Trade.Trade.EntryEpoch).ThenBy(p => p.Trade.Trade.Key, StringComparer.Ordinal)]))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var chunkFirst = chunk.Min(p => p.Trade.Trade.TradeDate);
                    var chunkLast = chunk.Max(p => SessionClock.DateOf(p.Trade.Trade.ExitEpoch));
                    var from = new DateTimeOffset(chunkFirst.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset);
                    var to = new DateTimeOffset(chunkLast.AddDays(1).ToDateTime(TimeOnly.MinValue), IndiaTime.Offset);
                    var raw = await barReader.BarsAsync(group.Key, series.Key.Flag, series.Key.Code, series.Key.Side, series.Key.IntervalMinutes, from, to, cancellationToken);
                    watermark = Math.Max(watermark, await barReader.WatermarkAsync(group.Key, chunkFirst, chunkLast, cancellationToken));
                    var bars = new List<OptionBar>(raw.Count);
                    foreach (var bar in raw)
                    {
                        var key = RemarkShared.KeySession(group.Key, series.Key.Flag, series.Key.Code, series.Key.Side, series.Key.IntervalMinutes, bar.Offset, SessionClock.DateOf(bar.Ts.ToUnixTimeSeconds()));
                        var ok = usable.Contains(key);
                        if (ok)
                        {
                            usableKeys.Add(key);
                        }

                        bars.Add(bar with { Usable = ok });
                    }

                    var grid = BarGrid.Build(bars);
                    var spot = bars.Count == 0 ? new Dictionary<long, int>() : await RemarkShared.SpotAsync(minuteBars, group.Key, from - SpotWindowPad, to, cancellationToken);
                    var context = new RemarkContext
                    {
                        Calendar = calendar,
                        UnderlyingId = group.Key,
                        Lots = lots,
                        SpreadModel = model,
                        AssumedHalfSpreadPct = assumed,
                        Liquidity = spreadSettings.Liquidity,
                        Sheet = sheet,
                        SheetChanged = sheetChanged,
                        MaxIvAgeMinutes = settings.MaxIvAgeMinutes,
                        SpotPaiseBefore = at => spot.TryGetValue(at.ToUnixTimeSeconds() - 60, out var paise) ? paise : null,
                    };
                    foreach (var (trade, plan) in chunk)
                    {
                        var result = StructureRemark.EvaluateSpread(trade.Trade, plan, profile, grid, context);
                        rows.Add(result.Row);
                        legs.AddRange(result.Legs);
                    }
                }
            }
        }

        rows.Sort((a, b) => string.CompareOrdinal(a.TradeKey, b.TradeKey));
        var paired = rows.Count(r => r.IsPaired);
        var unpaired = StructureNames.Unpaired.ToDictionary(r => r, r => rows.Count(x => x.Fate == r), StringComparer.Ordinal);
        var fallback = rows.Count(r => r.IsPaired && r.Labels.Contains(RemarkFates.BsFallback));
        double? share = paired == 0 ? null : (double)fallback / paired;
        var sheetText = sheet is null ? "gross" : sheet.RatesSha256;
        var regimeSha = RemarkRunId.UsableSha(rangeSessions.Select(d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        var settingsText = string.Create(CultureInfo.InvariantCulture, $"max_iv_age={settings.MaxIvAgeMinutes};liquidity={spreadSettings.Liquidity.MinOiNotionalCr}/{spreadSettings.Liquidity.MinBarTurnoverLakh};min_sessions={spreadSettings.MinUsableSessions};range_sessions={regimeSha}");
        var usableSha = RemarkRunId.UsableSha(usableKeys);
        var lotsSha = RemarkRunId.LotsSha(allLots);
        var epochsSha = RemarkRunId.EpochsSha(epochs);
        var contractsSha = RemarkRunId.ContractsSha(allContracts);
        var runId = RemarkRunId.Of(command.RunKey, command.ProfileHash, watermark, usableSha, spreadInputs, sheetText, lotsSha, epochsSha, contractsSha, settingsText);
        var labels = rows.SelectMany(r => r.Labels).Append("remark: " + RemarkRunId.Version).Append("structure: " + profile.Kind.Code()).Append(sheet is null ? "basis: gross" : "basis: net").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var isNew = !await store.ExistsAsync(runId, cancellationToken);
        if (isNew)
        {
            var inputs = RemarkShared.InputsJson(header.RunKey, command.ProfileHash, watermark, usableSha, spreadInputs, sheetText, lotsSha, epochsSha, contractsSha, settingsText, usableKeys.Count);
            var row = new ExpressionRunRow(runId, command.RunKey, command.ProfileHash, RemarkRunId.Version, inputs, rows.Count, paired, RemarkShared.UnpairedJson(unpaired), share, labels);
            isNew = await store.AppendAsync(row, rows, legs, cancellationToken);
        }

        RemarkLog.Done(logger, runId, rows.Count, paired, isNew);
        return Result.Success(new RemarkRunSummary(runId, isNew, rows.Count, paired, unpaired, share, RemarkRunHandler.Lines(runId, isNew, rows.Count, paired, unpaired, share, labels, sheet is null)));
    }

    /// <summary>The option side the structure's bars are read from: a bought call spread and a bear call spread read calls, the other two puts.</summary>
    private static string SideOf(bool credit, bool isBuy) => isBuy != credit ? "CE" : "PE";

    /// <summary>An unpaired row on the side word the structure's paired rows carry (a credit spread of a buy is a put spread), so the side windows of the evaluation hold all of one side's rows.</summary>
    private static RemarkRow Unpaired(RemarkTrade trade, string fate, bool credit, params string[] labels) => OptionRemark.Unpaired(trade, null, fate, labels) with { OptionSide = SideOf(credit, trade.IsBuy) };
}
