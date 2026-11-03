using System.Globalization;
using System.Text;
using System.Text.Json;
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
using XDrishti.Domain.Options;
using XDrishti.Domain.Options.Quality;
using XDrishti.Domain.Options.Remark;
using XDrishti.Domain.Research;

namespace XDrishti.Application.OptionExpression;

/// <summary>
/// <c>xd options remark</c> (spec P14-08, ADR 0197): re-marks every trade of a stored cash run as a single long option under a registered <c>option_buy</c> profile. Each trade gets exactly one row, paired or unpaired with its reason; the counts of both are in the run row.
/// The bars of a series are read in bounded stretches (one underlying, one series, at most a month of entries), marked usable only where the archive quality checks passed that key-session, and shared by the trades on them. The run id hashes every input, so the same inputs
/// are read, found stored and not written again. Research only: nothing here selects, plans or orders, and the cash run is read, never changed.
/// </summary>
internal sealed class RemarkRunHandler(
    IRunStore runs,
    IExpressionProfileStore profiles,
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
    IExpressionTradeStore store,
    ILogger<RemarkRunHandler> logger) : ICommandHandler<RemarkRunCommand, RemarkRunSummary>
{
    public const string RunNotFoundCode = "expression.run_not_found";
    public const string ProfileNotFoundCode = "expression.profile_not_found";

    private const int ChunkDays = 31;
    private const int QualityRowLimit = 1_000_000;
    private static readonly TimeSpan SpotWindowPad = TimeSpan.FromDays(1);

    public async ValueTask<Result<RemarkRunSummary>> HandleAsync(RemarkRunCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var stored = await profiles.GetAsync(command.ProfileHash, cancellationToken);
        if (stored is null)
        {
            return Result.Failure<RemarkRunSummary>(Error.NotFound(ProfileNotFoundCode, "No expression profile has that hash."));
        }

        var parameters = OptionProfileParams.FromProfile(stored.Profile);
        if (parameters.IsFailure)
        {
            return Result.Failure<RemarkRunSummary>(parameters.Error);
        }

        var header = await runs.FindAsync(command.RunKey, cancellationToken);
        if (header is null)
        {
            return Result.Failure<RemarkRunSummary>(Error.NotFound(RunNotFoundCode, "No stored run has that key."));
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

        var cash = new List<Prepared>();
        await foreach (var run in runs.StreamTradesAsync(command.RunKey, openAtEnd: false, cancellationToken))
        {
            cash.Add(Prepare(run.Trade, openAtEnd: false));
        }

        await foreach (var run in runs.StreamTradesAsync(command.RunKey, openAtEnd: true, cancellationToken))
        {
            cash.Add(Prepare(run.Trade, openAtEnd: true));
        }

        RemarkLog.Start(logger, command.RunKey, command.ProfileHash[..12], cash.Count);
        var calendar = await calendars.GetAsync(cancellationToken);
        var epochs = await reference.EpochsAsync(cancellationToken);
        var symbols = await reference.SymbolsAsync([.. cash.Select(c => c.InstrumentId).Distinct()], cancellationToken);
        var rows = new List<RemarkRow>();
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
                rows.AddRange(trades.Select(t => t.Trade.OpenAtEnd ? OpenAtEnd(t.Trade) : Plain(t.Trade, RemarkFates.NoSeries)));
                continue;
            }

            var lots = await reference.LotSizesAsync(group.Key, cancellationToken);
            allLots.AddRange(lots);
            var model = await spreadModels.LatestAsync(group.Key, cancellationToken);
            var assumed = spreadSettings.AssumedHalfSpreadPct(symbol);
            spreadInputs.Add(string.Create(CultureInfo.InvariantCulture, $"{group.Key:N}:{model?.ModelId ?? "none"}:{assumed?.ToString(CultureInfo.InvariantCulture) ?? "none"}"));
            var first = trades.Min(t => t.Trade.TradeDate);
            var last = trades.Max(t => SessionClock.DateOf(t.Trade.ExitEpoch));
            var contracts = await contractReader.ExpiriesAsync(group.Key, first.AddDays(-7), last.AddDays(120), cancellationToken);
            allContracts.AddRange(contracts);
            var expiries = new ExpiryCalendar(symbol, contracts, epochs, calendar);
            var usable = await UsableAsync(group.Key, first, last, cancellationToken);

            var planned = new List<(Prepared Trade, SeriesPlan Plan)>();
            foreach (var t in trades)
            {
                if (t.Trade.OpenAtEnd)
                {
                    rows.Add(OpenAtEnd(t.Trade));
                    continue;
                }

                var plannedLast = SeriesPlanner.PlannedLastSession(calendar, t.Trade.TradeDate, !t.Trade.IsBuy, parameters.Value.MaxHoldSessions);
                var choice = SeriesPlanner.Plan(symbol, t.Trade.TradeDate, plannedLast, expiries, epochs);
                if (choice.Plan is null)
                {
                    rows.Add(Plain(t.Trade, choice.Fate!));
                }
                else
                {
                    planned.Add((t, choice.Plan));
                }
            }

            foreach (var series in planned.GroupBy(p => (p.Plan.Flag, p.Plan.Code, p.Plan.IntervalMinutes, Side: p.Trade.Trade.IsBuy ? "CE" : "PE")).OrderBy(g => g.Key.Flag).ThenBy(g => g.Key.Code).ThenBy(g => g.Key.Side, StringComparer.Ordinal))
            {
                foreach (var chunk in Chunks([.. series.OrderBy(p => p.Trade.Trade.EntryEpoch).ThenBy(p => p.Trade.Trade.Key, StringComparer.Ordinal)]))
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
                        var key = KeySession(group.Key, series.Key.Flag, series.Key.Code, series.Key.Side, series.Key.IntervalMinutes, bar.Offset, SessionClock.DateOf(bar.Ts.ToUnixTimeSeconds()));
                        var ok = usable.Contains(key);
                        if (ok)
                        {
                            usableKeys.Add(key);
                        }

                        bars.Add(bar with { Usable = ok });
                    }

                    var grid = BarGrid.Build(bars);
                    var spot = bars.Count == 0 ? new Dictionary<long, int>() : await SpotAsync(group.Key, from - SpotWindowPad, to, cancellationToken);
                    foreach (var (trade, plan) in chunk)
                    {
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
                        rows.Add(OptionRemark.Evaluate(trade.Trade, plan, parameters.Value, grid, context));
                    }
                }
            }
        }

        rows.Sort((a, b) => string.CompareOrdinal(a.TradeKey, b.TradeKey));
        var paired = rows.Count(r => r.IsPaired);
        var unpaired = RemarkFates.Unpaired.ToDictionary(r => r, r => rows.Count(x => x.Fate == r), StringComparer.Ordinal);
        var fallback = rows.Count(r => r.IsPaired && r.Labels.Contains(RemarkFates.BsFallback));
        double? share = paired == 0 ? null : (double)fallback / paired;
        var sheetText = sheet is null ? "gross" : sheet.RatesSha256;
        var settingsText = string.Create(CultureInfo.InvariantCulture, $"max_iv_age={settings.MaxIvAgeMinutes};liquidity={spreadSettings.Liquidity.MinOiNotionalCr}/{spreadSettings.Liquidity.MinBarTurnoverLakh}");
        var usableSha = RemarkRunId.UsableSha(usableKeys);
        var lotsSha = RemarkRunId.LotsSha(allLots);
        var epochsSha = RemarkRunId.EpochsSha(epochs);
        var contractsSha = RemarkRunId.ContractsSha(allContracts);
        var runId = RemarkRunId.Of(command.RunKey, command.ProfileHash, watermark, usableSha, spreadInputs, sheetText, lotsSha, epochsSha, contractsSha, settingsText);
        var labels = rows.SelectMany(r => r.Labels).Append("remark: " + RemarkRunId.Version).Append(sheet is null ? "basis: gross" : "basis: net").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var isNew = !await store.ExistsAsync(runId, cancellationToken);
        if (isNew)
        {
            var inputs = InputsJson(header.RunKey, command.ProfileHash, watermark, usableSha, spreadInputs, sheetText, lotsSha, epochsSha, contractsSha, settingsText, usableKeys.Count);
            var row = new ExpressionRunRow(runId, command.RunKey, command.ProfileHash, RemarkRunId.Version, inputs, rows.Count, paired, UnpairedJson(unpaired), share, labels);
            isNew = await store.AppendAsync(row, rows, cancellationToken);
        }

        RemarkLog.Done(logger, runId, rows.Count, paired, isNew);
        return Result.Success(new RemarkRunSummary(runId, isNew, rows.Count, paired, unpaired, share, Lines(runId, isNew, rows.Count, paired, unpaired, share, labels, sheet is null)));
    }

    private static List<List<(Prepared Trade, SeriesPlan Plan)>> Chunks(List<(Prepared Trade, SeriesPlan Plan)> ordered)
    {
        var chunks = new List<List<(Prepared, SeriesPlan)>>();
        DateOnly? start = null;
        foreach (var item in ordered)
        {
            if (start is null || item.Trade.Trade.TradeDate.DayNumber - start.Value.DayNumber >= ChunkDays)
            {
                chunks.Add([]);
                start = item.Trade.Trade.TradeDate;
            }

            chunks[^1].Add(item);
        }

        return chunks;
    }

    /// <summary>The usable key-sessions of the underlying over the dates of all its trades, read once: <c>fetch_key|date</c> for every key-session whose stored verdicts make it usable (ADR 0178). A key-session with no verdict is not in it.</summary>
    private async ValueTask<HashSet<string>> UsableAsync(Guid underlyingId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var results = await quality.RollingResultsAsync(underlyingId, from, to, QualityRowLimit, cancellationToken);
        var usable = new HashSet<string>(StringComparer.Ordinal);
        foreach (var keySession in results.Where(r => r.FetchKey is not null).GroupBy(r => (Key: r.FetchKey!, r.SessionDate)))
        {
            if (SessionUsability.IsUsable(QualityCodes.Rolling, [.. keySession.Select(r => r.Check)]))
            {
                usable.Add(string.Create(CultureInfo.InvariantCulture, $"{keySession.Key.Key}|{keySession.Key.SessionDate:yyyy-MM-dd}"));
            }
        }

        return usable;
    }

    private static string KeySession(Guid underlyingId, char flag, int code, string side, int interval, int offset, DateOnly date) =>
        string.Create(CultureInfo.InvariantCulture, $"{underlyingId}:{flag}{code}:{offset}:{side}:{interval}|{date:yyyy-MM-dd}");

    private async ValueTask<Dictionary<long, int>> SpotAsync(Guid instrumentId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var minutes = await minuteBars.ReadAsync(instrumentId, from.ToUniversalTime(), to.ToUniversalTime(), cancellationToken);
        var map = new Dictionary<long, int>(minutes.Count);
        foreach (var bar in minutes)
        {
            map[bar.EpochSeconds] = (int)Math.Round(bar.Close * 100, MidpointRounding.AwayFromZero);
        }

        return map;
    }

    private static Prepared Prepare(SimTrade trade, bool openAtEnd)
    {
        var costing = trade.Costing;
        var net = trade.Costs == CostBasis.Charged && costing is not null;
        var quantity = costing?.Quantity ?? 1L;
        var entry = (decimal)trade.EntryPrice;
        var stop = (decimal)trade.StopPrice;
        var risk = costing?.RiskTaken ?? Math.Abs(entry - stop);
        return new Prepared(
            trade.InstrumentId,
            new RemarkTrade(trade.Key, trade.Side == Side.Buy, trade.TradeDate, trade.EntryEpochSeconds, trade.ExitEpochSeconds, ExitWord(trade.ExitReason), entry, stop, quantity, costing is not null, risk, net ? costing!.RNet : trade.RFilled, net, openAtEnd));
    }

    private static string ExitWord(ExitReason reason)
    {
        var name = reason.ToString();
        var text = new StringBuilder(name.Length + 2);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
            {
                text.Append('_');
            }

            text.Append(char.ToLowerInvariant(name[i]));
        }

        return text.ToString();
    }

    private static RemarkRow OpenAtEnd(RemarkTrade trade) => Plain(trade, RemarkFates.OpenAtEnd);

    private static RemarkRow Plain(RemarkTrade trade, string fate) => OptionRemark.Unpaired(trade, null, fate, []);

    private static string UnpairedJson(IReadOnlyDictionary<string, int> counts)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var (reason, count) in counts.OrderBy(c => c.Key, StringComparer.Ordinal))
            {
                writer.WriteNumber(reason, count);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string InputsJson(string cashRunKey, string profileHash, long watermark, string usableSha, List<string> spreadInputs, string sheet, string lotsSha, string epochsSha, string contractsSha, string settingsText, int usableKeySessions)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("format", RemarkRunId.Format);
            writer.WriteString("cash_run_key", cashRunKey);
            writer.WriteString("profile_hash", profileHash);
            writer.WriteString("remark_version", RemarkRunId.Version);
            writer.WriteNumber("archive_watermark", watermark);
            writer.WriteString("usable_key_sessions_sha256", usableSha);
            writer.WriteNumber("usable_key_sessions", usableKeySessions);
            writer.WriteStartArray("spread");
            foreach (var input in spreadInputs.Order(StringComparer.Ordinal))
            {
                writer.WriteStringValue(input);
            }

            writer.WriteEndArray();
            writer.WriteString("cost_sheet", sheet);
            writer.WriteString("lots_sha256", lotsSha);
            writer.WriteString("epochs_sha256", epochsSha);
            writer.WriteString("contract_expiries_sha256", contractsSha);
            writer.WriteString("settings", settingsText);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static List<string> Lines(string runId, bool isNew, int trades, int paired, IReadOnlyDictionary<string, int> unpaired, double? share, IReadOnlyList<string> labels, bool gross)
    {
        var lines = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture, $"expression run {runId[..12]}{(isNew ? string.Empty : " (already stored; nothing written)")}: {trades} trades, {paired} paired, {trades - paired} unpaired, {(gross ? "gross" : "net")}"),
        };
        lines.AddRange(unpaired.Where(u => u.Value > 0).Select(u => string.Create(CultureInfo.InvariantCulture, $"  unpaired {u.Key}: {u.Value}")));
        var left = unpaired.GetValueOrDefault(RemarkFates.StrikeLeftWindow);
        if (trades > 0 && left > 0)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"  strike_left_window share {(double)left / trades:P1} of trades (the paired remainder is biased toward short trades)"));
        }

        lines.Add(share is { } s ? string.Create(CultureInfo.InvariantCulture, $"  model-price (bs_fallback) share of paired: {s:P1}") : "  model-price (bs_fallback) share of paired: n/a (no paired trade)");
        lines.Add("labels: " + string.Join("; ", labels));
        return lines;
    }

    private sealed record Prepared(Guid InstrumentId, RemarkTrade Trade);
}
