using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.OptionCosts;
using XDrishti.Application.Abstractions.OptionExpression;
using XDrishti.Application.Abstractions.OptionQuality;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Options.Remark;
using XDrishti.Domain.Options.Structures;
using XDrishti.Domain.Research;

namespace XDrishti.Application.OptionExpression;

/// <summary>
/// <c>xd options condors</c> (spec P14-10, ADR 0200): simulates the iron condor of the index range family. There is no cash run: the entries are the first session of each week after a range session, 09:30, the nearest weekly at least two sessions away; one row per entry (paired, or unpaired
/// with one reason), four legs per paired row. <b>Gates before any bar is read:</b> an index underlying; a stored regime (no label: refused <c>structure.regime_unavailable</c>, the reader never called); a measured half-spread model of at least the usable-session minimum (an assumption is not
/// enough: <c>structure.spread_not_measured</c>). The bars of the call and the put series are read in stretches of at most a month of entries. The run is content-addressed: the same inputs write nothing twice. Research only: nothing here selects, plans, sizes or orders.
/// </summary>
internal sealed class SimulateCondorsHandler(
    IExpressionProfileStore profiles,
    ICostSheetStore sheets,
    IOptionReference reference,
    IOptionQualityStore quality,
    IRollingBarReader barReader,
    IContractExpiryReader contractReader,
    ISpreadModelStore spreadModels,
    ISpreadModelSettings spreadSettings,
    IBarStore minuteBars,
    XDrishti.Application.Abstractions.Market.History.IBackfillSeams seams,
    ITradingCalendar calendars,
    IRemarkSettings settings,
    IRegimeLabels regime,
    IExpressionTradeStore store,
    ILogger<SimulateCondorsHandler> logger) : ICommandHandler<SimulateCondorsCommand, RemarkRunSummary>
{
    public const string InvalidCode = "condors.invalid";
    public const string IndexOnlyCode = "condors.index_only";
    public const string UnderlyingNotFoundCode = "condors.underlying_not_found";
    public const string EmptyArchiveCode = "condors.empty_archive";

    private const int MaxSpanDays = 3_700;
    private static readonly TimeSpan SpotWindowPad = TimeSpan.FromDays(1);

    public async ValueTask<Result<RemarkRunSummary>> HandleAsync(SimulateCondorsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.From > command.To || command.To.DayNumber - command.From.DayNumber > MaxSpanDays)
        {
            return Result.Failure<RemarkRunSummary>(Error.Validation(InvalidCode, "The dates must be in order and span at most ten years."));
        }

        if (command.Underlying is not (ExpiryRules.Nifty or ExpiryRules.BankNifty))
        {
            return Result.Failure<RemarkRunSummary>(Error.Validation(IndexOnlyCode, "The condor is built on NSE:NIFTY or NSE:BANKNIFTY only: the wings of a share do not fit its window."));
        }

        var stored = await profiles.GetAsync(command.ProfileHash, cancellationToken);
        if (stored is null)
        {
            return Result.Failure<RemarkRunSummary>(Error.NotFound(RemarkRunHandler.ProfileNotFoundCode, "No expression profile has that hash."));
        }

        var parameters = CondorParams.FromProfile(stored.Profile);
        if (parameters.IsFailure)
        {
            return Result.Failure<RemarkRunSummary>(parameters.Error);
        }

        // The gates come before the underlying, the sheet, the calendar or any bar is read.
        if (!await regime.AnyAsync(cancellationToken))
        {
            return Result.Failure<RemarkRunSummary>(Error.Validation(RemarkStructureHandler.RegimeUnavailableCode, "The condor trades only after a range session and no regime label is stored (P6-02 and P3-01d are not built): NOT_EVALUABLE regime_unavailable."));
        }

        var underlying = await reference.FindUnderlyingAsync(command.Underlying, cancellationToken);
        if (underlying is not { } underlyingId)
        {
            return Result.Failure<RemarkRunSummary>(Error.NotFound(UnderlyingNotFoundCode, "The underlying is not in the master list."));
        }

        var model = await spreadModels.LatestAsync(underlyingId, cancellationToken);
        if (model is null || model.Sessions < spreadSettings.MinUsableSessions)
        {
            return Result.Failure<RemarkRunSummary>(Error.Validation(RemarkStructureHandler.SpreadNotMeasuredCode, "A condor pays eight half-spreads and needs a measured spread model of at least the usable-session minimum: NOT_EVALUABLE spread_not_measured."));
        }

        // An archive with no bar of the underlying gives nothing to simulate: refused, not frozen as a run of empty rows.
        if (!await barReader.HasSeriesAsync(underlyingId, cancellationToken))
        {
            return Result.Failure<RemarkRunSummary>(Error.Validation(EmptyArchiveCode, "The option archive holds no rolling bar of that underlying: nothing was simulated and nothing was stored."));
        }

        CostSheet? sheet = null;
        if (settings.CondorCostSheetId is { } sheetId)
        {
            var found = await sheets.FindAsync(sheetId, cancellationToken);
            if (found.IsFailure)
            {
                return Result.Failure<RemarkRunSummary>(found.Error);
            }

            sheet = found.Value;
        }

        var profile = parameters.Value;
        var population = Population(command);
        RemarkLog.Start(logger, population, command.ProfileHash[..12], 0);
        var calendar = await calendars.GetAsync(cancellationToken);
        var epochs = await reference.EpochsAsync(cancellationToken);
        var lots = await reference.LotSizesAsync(underlyingId, cancellationToken);
        var assumed = spreadSettings.AssumedHalfSpreadPct(command.Underlying);
        var contracts = await contractReader.ExpiriesAsync(underlyingId, command.From.AddDays(-7), command.To.AddDays(120), cancellationToken);
        var expiries = new ExpiryCalendar(command.Underlying, contracts, epochs, calendar);
        var rangeSessions = (await regime.RangeSessionsAsync(command.From.AddDays(-14), command.To, cancellationToken)).ToHashSet();
        var schedule = CondorSchedule.Build(command.Underlying, command.From, command.To, calendar, expiries, epochs, rangeSessions.Contains);
        var usable = await RemarkShared.UsableAsync(quality, underlyingId, command.From, command.To.AddDays(30), cancellationToken);
        var usableKeys = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<RemarkRow>();
        var legs = new List<StructureLegRow>();
        long watermark = 0;

        foreach (var code in schedule.Entries.Select(e => e.Plan.Code).Distinct().Order())
        {
            var ofCode = schedule.Entries.Where(e => e.Plan.Code == code).OrderBy(e => e.Date).Select(e => (Trade: new RemarkShared.Prepared(underlyingId, ToTrade(e, sheet is not null)), e.Plan)).ToList();
            foreach (var chunk in RemarkShared.Chunks(ofCode))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chunkFirst = chunk.Min(p => p.Trade.Trade.TradeDate);
                var chunkLast = chunk.Max(p => p.Plan.LastSession);
                var from = new DateTimeOffset(chunkFirst.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset);
                var to = new DateTimeOffset(chunkLast.AddDays(1).ToDateTime(TimeOnly.MinValue), IndiaTime.Offset);
                var interval = chunk[0].Plan.IntervalMinutes;
                var calls = await GridAsync(underlyingId, code, "CE", interval, from, to, usable, usableKeys, cancellationToken);
                var puts = await GridAsync(underlyingId, code, "PE", interval, from, to, usable, usableKeys, cancellationToken);
                watermark = Math.Max(watermark, await barReader.WatermarkAsync(underlyingId, chunkFirst, chunkLast, cancellationToken));
                var spot = calls.Grid.Times.Count == 0 ? new Dictionary<long, int>() : await RemarkShared.SpotAsync(minuteBars, seams, underlyingId, from - SpotWindowPad, to, cancellationToken);
                var context = new RemarkContext
                {
                    Calendar = calendar,
                    UnderlyingId = underlyingId,
                    Lots = lots,
                    SpreadModel = model,
                    AssumedHalfSpreadPct = assumed,
                    Liquidity = spreadSettings.Liquidity,
                    Sheet = sheet,
                    SheetChanged = false,
                    MaxIvAgeMinutes = settings.MaxIvAgeMinutes,
                    SpotPaiseBefore = at => spot.TryGetValue(at.ToUnixTimeSeconds() - 60, out var paise) ? paise : null,
                };
                foreach (var (trade, plan) in chunk)
                {
                    var result = StructureRemark.EvaluateCondor(trade.Trade, plan, profile, calls.Grid, puts.Grid, context);
                    rows.Add(result.Row);
                    legs.AddRange(result.Legs);
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
        var spreadInputs = new List<string> { string.Create(CultureInfo.InvariantCulture, $"{underlyingId:N}:{model.ModelId}:{assumed?.ToString(CultureInfo.InvariantCulture) ?? "none"}") };
        var lotsSha = RemarkRunId.LotsSha(lots);
        var epochsSha = RemarkRunId.EpochsSha(epochs);
        var contractsSha = RemarkRunId.ContractsSha(contracts);
        var runId = RemarkRunId.Of(population, command.ProfileHash, watermark, usableSha, spreadInputs, sheetText, lotsSha, epochsSha, contractsSha, settingsText);
        var labels = rows.SelectMany(r => r.Labels).Append("remark: " + RemarkRunId.Version).Append("structure: iron_condor").Append(sheet is null ? "basis: gross" : "basis: net").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var isNew = !await store.ExistsAsync(runId, cancellationToken);
        if (isNew)
        {
            var extra = new Dictionary<string, string>
            {
                ["from"] = command.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["to"] = command.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["underlying"] = command.Underlying,
                ["weeks"] = schedule.Weeks.ToString(CultureInfo.InvariantCulture),
                ["not_range"] = schedule.NotRange.ToString(CultureInfo.InvariantCulture),
                ["no_weekly"] = schedule.NoWeekly.ToString(CultureInfo.InvariantCulture),
                ["no_series"] = schedule.NoSeries.ToString(CultureInfo.InvariantCulture),
            };
            var inputs = RemarkShared.InputsJson(population, command.ProfileHash, watermark, usableSha, spreadInputs, sheetText, lotsSha, epochsSha, contractsSha, settingsText, usableKeys.Count, extra);
            var row = new ExpressionRunRow(runId, population, command.ProfileHash, RemarkRunId.Version, inputs, rows.Count, paired, RemarkShared.UnpairedJson(unpaired), share, labels);
            isNew = await store.AppendAsync(row, rows, legs, cancellationToken);
        }

        RemarkLog.Done(logger, runId, rows.Count, paired, isNew);
        var lines = RemarkRunHandler.Lines(runId, isNew, rows.Count, paired, unpaired, share, labels, sheet is null);
        lines.Insert(1, string.Create(CultureInfo.InvariantCulture, $"  population {population[..12]}: {schedule.Weeks} weeks, {schedule.NotRange} not after a range session, {schedule.NoWeekly} with no weekly expiry, {schedule.NoSeries} needing a series the backfill does not hold, {schedule.Entries.Count} entries"));
        return Result.Success(new RemarkRunSummary(runId, isNew, rows.Count, paired, unpaired, share, lines));
    }

    /// <summary>The population key: what the verb was asked (the underlying and the dates), the key the evaluation is given as <c>--run</c>.</summary>
    internal static string Population(SimulateCondorsCommand command) =>
        CanonicalText.Sha256Hex(string.Create(CultureInfo.InvariantCulture, $"condor-population/1\n{command.Underlying}\n{command.From:yyyy-MM-dd}\n{command.To:yyyy-MM-dd}"));

    // shortcut: the exit instant is midnight after the last session, so the shared D6 rule (exit date after the plan's last session) fires and the exit is the close of that session; a test pins it.
    /// <summary>A condor entry as the trade the re-mark reads: 09:30 on the entry date, leaving at the close of the last session before the expiry (an exit instant after that session).</summary>
    private static RemarkTrade ToTrade(CondorEntry entry, bool net) => new(
        string.Create(CultureInfo.InvariantCulture, $"condor:{entry.Date:yyyy-MM-dd}:w{entry.Plan.Code}"),
        true,
        entry.Date,
        CondorSchedule.EntryEpoch(entry.Date),
        new DateTimeOffset(entry.Plan.LastSession.AddDays(1).ToDateTime(TimeOnly.MinValue), IndiaTime.Offset).ToUnixTimeSeconds(),
        RemarkFates.ExpiryExit,
        0m,
        0m,
        1m,
        true,
        1m,
        0d,
        net,
        false);

    private async ValueTask<(BarGrid Grid, int Bars)> GridAsync(Guid underlyingId, int code, string side, int interval, DateTimeOffset from, DateTimeOffset to, HashSet<string> usable, HashSet<string> usableKeys, CancellationToken cancellationToken)
    {
        var raw = await barReader.BarsAsync(underlyingId, 'W', code, side, interval, from, to, cancellationToken);
        var bars = new List<OptionBar>(raw.Count);
        foreach (var bar in raw)
        {
            var key = RemarkShared.KeySession(underlyingId, 'W', code, side, interval, bar.Offset, SessionClock.DateOf(bar.Ts.ToUnixTimeSeconds()));
            var ok = usable.Contains(key);
            if (ok)
            {
                usableKeys.Add(key);
            }

            bars.Add(bar with { Usable = ok });
        }

        return (BarGrid.Build(bars), bars.Count);
    }
}
