using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using XDrishti.SimExport.Manifest;
using XDrishti.SimExport.Verification;
using XDrishti.SimReader;

namespace XDrishti.Api.Simulate.Assist;

/// <summary>What the assisted check did: the exit code, the lines for the console, and the hashes it printed.</summary>
internal sealed record AssistOutcome(int Exit, IReadOnlyList<string> Lines, string? LogHash, string? EngineHash, AssistDetails? Details = null);

/// <summary>What the gate (spec P3-09d) needs from a finished read: the pack identity, the verdicts, the engine's values and the hash of the second-source file.</summary>
internal sealed record AssistDetails(string PackId, string Seed, IReadOnlyList<RowVerdict> Verdicts, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Engine, string CacheHash);

/// <summary>
/// The assisted check (spec P3-09c), in the order that keeps it blind (D5): the second source's bars (cached in <c>second-source-bars.csv</c>), the stored bars, the reader on the second source's day, the bar comparison, then <c>assist-log.csv</c> is written
/// and hashed, and only after that is <c>engine-values.csv</c> read, for the field comparison and the verdicts. It never writes <c>check-log.csv</c> and never a verdict: the <c>verdict</c> column stays empty. Same inputs, same bytes (no clock, no random source).
/// </summary>
internal static class AssistRunner
{
    private const string Orb = "ORB";
    private const string VwapPullback = "VWAP_PULLBACK";
    private const string BaseBreakout = "BASE_BREAKOUT";

    /// <summary>The strategies the gate has an independent reader for (ADR 0048 amended, ADR 0291). Any other strategy is refused before a bar is read.</summary>
    internal static readonly IReadOnlyList<string> SupportedStrategies = [Orb, VwapPullback, BaseBreakout];

    /// <summary>How far back the history of the VWAP_PULLBACK and BASE_BREAKOUT readers goes, in calendar days (about 90 sessions: the 50-session averages and the weekly average converge), and how far forward the hold of a swing trade can reach.</summary>
    private const int HistoryDays = 130;

    private const int HoldDays = 14;

    public static async Task<AssistOutcome> RunAsync(IPackFolder folder, ISecondSource source, IStoredDays stored, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(stored);

        var pack = Pack.Read(folder.Read(PackFiles.PackJsonName));
        if (pack is null)
        {
            return Refused("pack.json is missing or is not the pack.json of a verification pack");
        }

        if (pack.Rows.Select(r => r.Strategy).Distinct(StringComparer.Ordinal).Where(n => !SupportedStrategies.Contains(n, StringComparer.Ordinal)).Order(StringComparer.Ordinal).ToList() is { Count: > 0 } unsupported)
        {
            return Refused("unsupported strategy: the gate has no independent reader for " + string.Join(", ", unsupported) + " (it reads " + string.Join(", ", SupportedStrategies) + "); write the reader first, nothing was read or written");
        }

        foreach (var row in pack.Rows)
        {
            if (DateProblem(row) is { } problem)
            {
                return Refused(problem + "; nothing was read or written");
            }

            if (row.Missing(RequiredParameters(row.Strategy)) is { Count: > 0 } absent)
            {
                return Refused("pack row " + row.Seq.ToString(CultureInfo.InvariantCulture) + " (" + row.Strategy + ") lacks the parameter " + string.Join(", ", absent) + ": the reader does not guess a default; nothing was read or written");
            }
        }

        // the cache is never invalidated: a day already in it is used whatever source this run names (move the file away to fetch again)
        var cacheBytes = folder.Read(AssistBarsCsv.CacheName);
        var days = cacheBytes is null ? [] : AssistBarsCsv.ReadCache(Utf8(cacheBytes));
        if (days is null)
        {
            return Refused(AssistBarsCsv.CacheName + " is not a second-source cache; move it away to fetch again");
        }

        var missing = new Dictionary<(string, DateOnly), string>();
        foreach (var key in pack.Rows.SelectMany(r => r.FetchDates.Select(d => (r.Symbol, Date: d))).Distinct())
        {
            if (days.ContainsKey(key))
            {
                continue;
            }

            var day = await source.GetAsync(key.Symbol, key.Date, cancellationToken);
            if (day.Bars is { Count: > 0 } bars)
            {
                days[key] = new AssistBarsCsv.CachedDay(source.Name, bars);
            }
            else
            {
                missing[key] = day.Unavailable ?? (day.Bars is null ? "unavailable" : "the second source returned no bars for the day");
            }
        }

        var cache = Encoding.UTF8.GetBytes(AssistBarsCsv.WriteCache(days));
        if (cacheBytes is null || !cacheBytes.AsSpan().SequenceEqual(cache))
        {
            folder.Write(AssistBarsCsv.CacheName, cache);
        }

        var rows = new List<AssistRow>();
        foreach (var row in pack.Rows)
        {
            rows.Add(await ReadRowAsync(row, days, missing, source, stored, cancellationToken));
        }

        var log = Encoding.UTF8.GetBytes(AssistLog.Text(rows));
        folder.Write(DiskPackFolder.LogName, log);
        var logHash = Hash(log);

        // blindness (D5): the engine's values are read only now, after the readings are fixed and hashed
        var engineBytes = folder.Read(PackFiles.EngineValuesName);
        var engine = engineBytes is null ? null : EngineValues.Read(Utf8(engineBytes));
        if (engine is null)
        {
            return new AssistOutcome(SimulateCommand.UsageError, [PackFiles.EngineValuesName + " is missing or cannot be read; the readings are written in " + DiskPackFolder.LogName + " (sha256 " + logHash + ")"], logHash, null);
        }

        var engineHash = Hash(engineBytes!);
        if (EngineHashProblem(folder.Read(ExportPaths.ManifestName), engineHash) is { } changed)
        {
            return new AssistOutcome(SimulateCommand.UsageError, [changed + "; the readings are written in " + DiskPackFolder.LogName + " (sha256 " + logHash + ")"], logHash, engineHash);
        }

        var verdicts = rows.Select(r => Judge(r, engine)).ToList();
        var cacheHash = Hash(cache);
        var sources = string.Join(", ", days.Values.Select(d => d.Source).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        var report = Encoding.UTF8.GetBytes(AssistReport.Html(pack.PackId, pack.Seed, sources, verdicts, logHash, engineHash, cacheHash));
        folder.Write(DiskPackFolder.ReportName, report);
        folder.Write(DiskPackFolder.ManifestName, Encoding.UTF8.GetBytes(Manifest(pack.PackId, (AssistBarsCsv.CacheName, cache), (DiskPackFolder.LogName, log), (DiskPackFolder.ReportName, report))));

        var lines = new List<string> { "assist-log.csv sha256: " + logHash, "engine-values.csv sha256 (read after the log was written): " + engineHash, "second-source-bars.csv sha256: " + cacheHash, "second sources in the cache: " + sources };
        lines.AddRange(Verdicts.All.Select(v => v + ": " + verdicts.Count(x => x.Verdict == v).ToString(CultureInfo.InvariantCulture)));
        lines.AddRange(AssistCounters.Lines(verdicts));
        lines.Add("this is an assisted read, not a signed check: check-log.csv was not touched and no verdict was written");
        return new AssistOutcome(SimulateCommand.Done, lines, logHash, engineHash, new AssistDetails(pack.PackId, pack.Seed, verdicts, engine, cacheHash));
    }

    /// <summary>The trade key ends in the decision date (ADR 0043 D6); an intraday trade is decided and filled on one date, a swing trade is filled after the day it is decided.</summary>
    private static string? DateProblem(PackedTrade row)
    {
        var where = "pack row " + row.Seq.ToString(CultureInfo.InvariantCulture) + " (trade key '" + row.TradeKey + "')";
        if (row.DecisionDate is not { } decision)
        {
            return where + " does not end in a yyyy-MM-dd decision date";
        }

        var swing = string.Equals(row.Strategy, BaseBreakout, StringComparison.Ordinal);
        if (!swing && decision != row.Date)
        {
            return where + " is an intraday trade whose decision date " + decision.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " is not its date " + row.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return swing && decision >= row.Date ? where + " is a swing trade whose decision date " + decision.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " is not before its entry date " + row.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
    }

    private static IReadOnlyList<string> RequiredParameters(string strategy) => strategy switch
    {
        VwapPullback => ["pull_atr", "rr"],
        BaseBreakout => ["base_len", "base_pct", "vol_x", "rr"],
        _ => [],
    };

    private sealed record RowReader(Func<IReadOnlyDictionary<DateOnly, IReadOnlyList<ReaderBar>>, ReaderReading?> Read, Func<DateOnly, IReadOnlyList<ReaderBar>> StoredBars, int Opening, int Evaluation, string? Caveat);

    private static async Task<AssistRow> ReadRowAsync(
        PackedTrade row, Dictionary<(string, DateOnly), AssistBarsCsv.CachedDay> days, Dictionary<(string, DateOnly), string> missing, ISecondSource source, IStoredDays stored, CancellationToken cancellationToken)
    {
        var storedDay = await stored.GetAsync(row.Symbol, row.Date, cancellationToken);
        var label = "auto:" + source.Name;
        foreach (var date in row.FetchDates)
        {
            if (!days.ContainsKey((row.Symbol, date)))
            {
                return new AssistRow(row, missing.GetValueOrDefault((row.Symbol, date), "unavailable"), label, null, storedDay.Tick, []);
            }
        }

        label = "auto:" + days[(row.Symbol, row.Date)].Source;
        var reader = await ReaderFor(row, storedDay, stored, cancellationToken);
        var secondBars = row.FetchDates.ToDictionary(d => d, d => days[(row.Symbol, d)].Bars);

        // the same reader on the stored bars (the data the engine used): a difference there is about the rules, not the data
        ReaderReading? storedReading = null;
        string? storedNote = null;
        try
        {
            storedReading = reader.Read(new Dictionary<DateOnly, IReadOnlyList<ReaderBar>>());
        }
        catch (ArgumentException)
        {
            storedNote = "the stored day is not usable by the reader";
        }

        storedNote ??= storedReading is null ? "the reader found no trade on the stored bars" : null;

        ReaderReading? reading = null;
        string? secondNote = null;
        try
        {
            reading = reader.Read(secondBars);
        }
        catch (ArgumentException)
        {
            secondNote = "the second source's day is not usable by the reader";
        }

        var (minutes, offsetNote, ratios) = Compare(row, reader, secondBars, reading, storedDay.Tick);
        if (ratios.Count > 0)
        {
            // adjusted history against as-traded prices: the reader reads the second source at the stored price level, so that a constant offset is not mistaken for a different trade
            var scaled = secondBars.ToDictionary(kv => kv.Key, kv => ratios.TryGetValue(kv.Key, out var ratio) ? (IReadOnlyList<ReaderBar>)[.. kv.Value.Select(b => b with { Open = b.Open * ratio, High = b.High * ratio, Low = b.Low * ratio, Close = b.Close * ratio })] : kv.Value);
            try
            {
                reading = reader.Read(scaled);
                (minutes, offsetNote, _) = Compare(row, reader, secondBars, reading, storedDay.Tick);
            }
            catch (ArgumentException)
            {
                reading = null;
                secondNote = "the second source's day is not usable by the reader";
            }
        }

        return new AssistRow(row, null, label, reading, storedDay.Tick, minutes, storedDay.TickFallback, offsetNote, storedReading, storedNote, secondNote, reader.Caveat);
    }

    /// <summary>The bar comparison of every fetched day (one for an intraday row, the decision day and the entry day for a swing row): the minutes, the first offset note and the price ratio of each day that has one.</summary>
    private static (IReadOnlyList<MinuteComparison> Minutes, string? OffsetNote, Dictionary<DateOnly, decimal> Ratios) Compare(PackedTrade row, RowReader reader, Dictionary<DateOnly, IReadOnlyList<ReaderBar>> secondBars, ReaderReading? reading, decimal tick)
    {
        var minutes = new List<MinuteComparison>();
        string? note = null;
        var ratios = new Dictionary<DateOnly, decimal>();
        foreach (var (date, bars) in secondBars)
        {
            var outcome = BarComparison.Analyse(reader.StoredBars(date), bars, Cited(date, reader, row, reading), tick);
            minutes.AddRange(outcome.Minutes);
            note ??= outcome.OffsetNote;
            if (outcome.Ratio is { } ratio)
            {
                ratios[date] = ratio;
            }
        }

        return (minutes, note, ratios);
    }

    /// <summary>
    /// The reader of one row as a function of the bars the second source replaces (by date; none = the stored bars), the stored bars of any date, the minutes from the open that the comparison cites before the trigger, and the trigger bar's length.
    /// VWAP_PULLBACK reads the stored sessions before the day (the trend setup and the ATR need them) and BASE_BREAKOUT the stored sessions around the DECISION date (the trade date is the entry date). The second source replaces only the fetched days; the context is always stored (ADR 0291).
    /// </summary>
    private static async Task<RowReader> ReaderFor(PackedTrade row, StoredDay storedDay, IStoredDays stored, CancellationToken cancellationToken)
    {
        var tick = storedDay.Tick;
        switch (row.Strategy)
        {
            case VwapPullback:
                var prior = await stored.GetRangeAsync(row.Symbol, row.Date.AddDays(-HistoryDays), row.Date.AddDays(-1), row.Date, row.AdmittedSeam, cancellationToken);
                var vwap = new VwapPullbackStrategy(row.Parameter("pull_atr"), row.Parameter("rr"), tick);
                return new RowReader(ov => VwapPullbackReader.Read(prior.Sessions, ov.GetValueOrDefault(row.Date) ?? storedDay.Bars, vwap, row.Side), d => storedDay.Bars, 0, 15, prior.Caveat);
            case BaseBreakout:
                var around = await stored.GetRangeAsync(row.Symbol, row.Signal.AddDays(-HistoryDays), row.Signal.AddDays(HoldDays), row.Signal.AddDays(HoldDays), row.AdmittedSeam, cancellationToken);
                var swing = new BaseBreakoutStrategy((int)row.Parameter("base_len"), row.Parameter("base_pct"), row.Parameter("vol_x"), row.Parameter("rr"), tick);
                return new RowReader(
                    ov => BaseBreakoutReader.Read([.. around.Sessions.Where(a => !ov.ContainsKey(a.Date)).Concat(ov.Select(o => new ReaderSession(o.Key, o.Value))).OrderBy(a => a.Date)], row.Signal, swing),
                    d => around.Sessions.FirstOrDefault(a => a.Date == d)?.Bars ?? [],
                    0,
                    0,
                    around.Caveat);
            default:
                var orb = ReaderStrategy.OrbV1(tick) with { OrMinutes = row.OrMinutes, RewardRisk = row.RewardRisk };
                return new RowReader(ov => ChartReader.Read(ov.GetValueOrDefault(row.Date) ?? storedDay.Bars, orb, row.Side), d => storedDay.Bars, row.OrMinutes, orb.EvaluationMinutes, null);
        }
    }

    /// <summary>The minutes the comparison cites for one day. VWAP_PULLBACK: every minute from the open to the trigger bar (the VWAP depends on them). BASE_BREAKOUT: only the minutes of that day (the decision day's last minutes, the entry day's first).</summary>
    private static IReadOnlyCollection<long> Cited(DateOnly date, RowReader reader, PackedTrade row, ReaderReading? reading)
    {
        var open = new DateTimeOffset(date.ToDateTime(new TimeOnly(9, 15)), TimeSpan.FromMinutes(330)).ToUnixTimeSeconds();
        var opening = row.Strategy == VwapPullback && reading is not null ? Math.Max(reader.Opening, (int)((reading.TriggerBarStartEpoch - open) / 60)) : reader.Opening;
        var minutes = BarComparison.Minutes(open, opening, reader.Evaluation, reading);
        return row.Strategy == BaseBreakout ? [.. minutes.Where(m => m >= open && m < open + (375 * 60L))] : minutes;
    }

    /// <summary>
    /// The verdict of the assistant for one row (D6), in this precedence: source_unavailable; cannot_read (the engine row is absent, or the reader read no trade on either the stored or the second source's bars);
    /// reader_differs (the reader on the STORED bars differs from the engine, a stored "no trade" against an engine trade included: rules or implementation); bars_differ (a gross bar problem only, D3);
    /// data_sensitive (the reader on the second source differs, or finds no trade, while the one on the stored bars agrees: the trade depends on the feed); agree_all. Both field lists are kept whatever the verdict, so the counters count every category.
    /// </summary>
    internal static RowVerdict Judge(AssistRow row, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> engine)
    {
        if (row.Unavailable is not null)
        {
            return new RowVerdict(row, Verdicts.SourceUnavailable, []);
        }

        if ((row.Reading is null && row.StoredReading is null) || !engine.TryGetValue(row.Pack.TradeKey, out var values))
        {
            return new RowVerdict(row, Verdicts.CannotRead, []);
        }

        var stored = row.StoredReading is null ? [row.StoredNote ?? "no trade on the stored bars"] : FieldComparison.Differences(row.StoredReading, values, row.Tick);
        var second = row.Reading is null ? [row.SecondNote ?? "no trade on the second source"] : FieldComparison.Differences(row.Reading, values, row.Tick);
        var verdict = stored.Count > 0 ? Verdicts.ReaderDiffers : row.Bars.Any(b => b.Gross) ? Verdicts.BarsDiffer : second.Count > 0 ? Verdicts.DataSensitive : Verdicts.AgreeAll;
        return new RowVerdict(row, verdict, stored, second);
    }

    private static string Manifest(string packId, params (string Name, byte[] Bytes)[] files)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            json.WriteString("format", "xd-assist-1");
            json.WriteString("pack_id", packId);
            json.WriteStartArray("files");
            foreach (var (name, bytes) in files)
            {
                json.WriteStartObject();
                json.WriteString("name", name);
                json.WriteNumber("bytes", bytes.Length);
                json.WriteString("sha256", Hash(bytes));
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    private static string? EngineHashProblem(byte[]? manifest, string engineHash)
    {
        var parsed = manifest is null ? null : ExportManifest.ParseBytes(manifest);
        var entry = parsed is { IsSuccess: true } ? parsed.Value.Entries.FirstOrDefault(e => e.Name == PackFiles.EngineValuesName) : null;
        return entry is not null && string.Equals(entry.Sha256, engineHash, StringComparison.Ordinal) ? null : PackFiles.EngineValuesName + " differs from manifest.json: the pack was changed after it was written";
    }

    private static AssistOutcome Refused(string message) => new(SimulateCommand.UsageError, [message], null, null);

    private static string Utf8(byte[] bytes) => new UTF8Encoding(false, true).GetString(bytes);

    internal static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private sealed record Pack(string PackId, string Seed, IReadOnlyList<PackedTrade> Rows)
    {
        public static Pack? Read(byte[]? bytes)
        {
            if (bytes is null)
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(bytes);
                var root = doc.RootElement;
                if (root.GetProperty("format").GetString() != PackFiles.Format)
                {
                    return null;
                }

                var runs = root.GetProperty("runs").EnumerateArray().ToDictionary(
                    r => r.GetProperty("run_key").GetString()!,
                    r => (Strategy: r.GetProperty("strategy").GetString()!, Parameters: r.GetProperty("parameters")));
                // the seam date the run admitted pre-seam bars from, per instrument and run (the run record's seam line); absent for a run that admitted none and for a pack drawn before it was recorded
                var seams = new Dictionary<(string, string), DateOnly>();
                if (root.TryGetProperty("instruments", out var instruments))
                {
                    foreach (var i in instruments.EnumerateArray().Where(i => i.TryGetProperty("pre_seam", out _)))
                    {
                        seams[(i.GetProperty("symbol").GetString()!, i.GetProperty("run_key").GetString()!)] = DateOnly.ParseExact(i.GetProperty("pre_seam").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                    }
                }

                var rows = new List<PackedTrade>();
                foreach (var r in root.GetProperty("rows").EnumerateArray())
                {
                    var (strategy, parameters) = runs[r.GetProperty("run_key").GetString()!];
                    var orMin = parameters.TryGetProperty("or_min", out var o) ? int.Parse(o.GetString()!, CultureInfo.InvariantCulture) : 15;
                    var rr = parameters.TryGetProperty("rr", out var q) ? decimal.Parse(q.GetString()!, CultureInfo.InvariantCulture) : 2m;
                    var tradeKey = r.GetProperty("trade_key").GetString()!;
                    rows.Add(new PackedTrade(
                        r.GetProperty("seq").GetInt32(),
                        tradeKey,
                        r.GetProperty("symbol").GetString()!,
                        string.Equals(r.GetProperty("side").GetString(), "Sell", StringComparison.OrdinalIgnoreCase) ? ReaderSide.Sell : ReaderSide.Buy,
                        DateOnly.ParseExact(r.GetProperty("date").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                        strategy,
                        orMin,
                        rr,
                        parameters.EnumerateObject().Where(o => o.Value.ValueKind == JsonValueKind.String && decimal.TryParse(o.Value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                            .ToDictionary(o => o.Name, o => decimal.Parse(o.Value.GetString()!, NumberStyles.Float, CultureInfo.InvariantCulture), StringComparer.Ordinal),
                        DateOnly.TryParseExact(tradeKey[(tradeKey.LastIndexOf('|') + 1)..], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var decision) ? decision : null,
                        seams.TryGetValue((r.GetProperty("symbol").GetString()!, r.GetProperty("run_key").GetString()!), out var seam) ? seam : null));
                }

                return new Pack(root.GetProperty("pack_id").GetString()!, root.GetProperty("seed").GetString()!, rows);
            }
            catch (Exception bad) when (bad is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException)
            {
                return null;
            }
        }
    }
}
