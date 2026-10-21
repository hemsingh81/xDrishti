using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using XDrishti.SimExport.Manifest;
using XDrishti.SimExport.Verification;
using XDrishti.SimReader;

namespace XDrishti.Api.Simulate.Assist;

/// <summary>What the assisted check did: the exit code, the lines for the console, and the hashes it printed.</summary>
internal sealed record AssistOutcome(int Exit, IReadOnlyList<string> Lines, string? LogHash, string? EngineHash);

/// <summary>
/// The assisted check (spec P3-09c), in the order that keeps it blind (D5): the second source's bars (cached in <c>second-source-bars.csv</c>), the stored bars, the reader on the second source's day, the bar comparison, then <c>assist-log.csv</c> is written
/// and hashed, and only after that is <c>engine-values.csv</c> read, for the field comparison and the verdicts. It never writes <c>check-log.csv</c> and never a verdict: the <c>verdict</c> column stays empty. Same inputs, same bytes (no clock, no random source).
/// </summary>
internal static class AssistRunner
{
    private const string Orb = "ORB";

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

        // the cache is never invalidated: a day already in it is used whatever source this run names (move the file away to fetch again)
        var cacheBytes = folder.Read(AssistBarsCsv.CacheName);
        var days = cacheBytes is null ? [] : AssistBarsCsv.ReadCache(Utf8(cacheBytes));
        if (days is null)
        {
            return Refused(AssistBarsCsv.CacheName + " is not a second-source cache; move it away to fetch again");
        }

        var missing = new Dictionary<(string, DateOnly), string>();
        foreach (var key in pack.Rows.Select(r => (r.Symbol, r.Date)).Distinct())
        {
            if (days.ContainsKey(key))
            {
                continue;
            }

            var day = await source.GetAsync(key.Symbol, key.Date, cancellationToken);
            if (day.Bars is { } bars)
            {
                days[key] = new AssistBarsCsv.CachedDay(source.Name, bars);
            }
            else
            {
                missing[key] = day.Unavailable ?? "unavailable";
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
        lines.Add("this is an assisted read, not a signed check: check-log.csv was not touched and no verdict was written");
        return new AssistOutcome(SimulateCommand.Done, lines, logHash, engineHash);
    }

    private static async Task<AssistRow> ReadRowAsync(
        PackedTrade row, Dictionary<(string, DateOnly), AssistBarsCsv.CachedDay> days, Dictionary<(string, DateOnly), string> missing, ISecondSource source, IStoredDays stored, CancellationToken cancellationToken)
    {
        var storedDay = await stored.GetAsync(row.Symbol, row.Date, cancellationToken);
        var label = "auto:" + source.Name;
        if (!days.TryGetValue((row.Symbol, row.Date), out var second))
        {
            return new AssistRow(row, missing.GetValueOrDefault((row.Symbol, row.Date), "unavailable"), label, null, storedDay.Tick, []);
        }

        label = "auto:" + second.Source;
        if (!string.Equals(row.Strategy, Orb, StringComparison.Ordinal))
        {
            return new AssistRow(row, null, label, null, storedDay.Tick, []);
        }

        var strategy = ReaderStrategy.OrbV1(storedDay.Tick) with { OrMinutes = row.OrMinutes, RewardRisk = row.RewardRisk };
        ReaderReading? reading;
        try
        {
            reading = ChartReader.Read(second.Bars, strategy, row.Side);
        }
        catch (ArgumentException)
        {
            return new AssistRow(row, "the second source's day is not usable by the reader", label, null, storedDay.Tick, []);
        }

        var open = new DateTimeOffset(row.Date.ToDateTime(new TimeOnly(9, 15)), TimeSpan.FromMinutes(330)).ToUnixTimeSeconds();
        var outcome = BarComparison.Analyse(storedDay.Bars, second.Bars, BarComparison.Minutes(open, strategy, reading), storedDay.Tick);
        return new AssistRow(row, null, label, reading, storedDay.Tick, outcome.Minutes, storedDay.TickFallback, outcome.OffsetNote);
    }

    /// <summary>The verdict of the assistant for one row (D6). A bar difference is the data candidate and comes before a reader difference; the fields are listed either way.</summary>
    internal static RowVerdict Judge(AssistRow row, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> engine)
    {
        if (row.Unavailable is not null)
        {
            return new RowVerdict(row, Verdicts.SourceUnavailable, []);
        }

        if (row.Reading is null || !engine.TryGetValue(row.Pack.TradeKey, out var values))
        {
            return new RowVerdict(row, Verdicts.CannotRead, []);
        }

        var fields = FieldComparison.Differences(row.Reading, values, row.Tick);
        var verdict = row.Bars.Any(b => !b.Matches) ? Verdicts.BarsDiffer : fields.Count > 0 ? Verdicts.ReaderDiffers : Verdicts.AgreeAll;
        return new RowVerdict(row, verdict, fields);
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
                var rows = new List<PackedTrade>();
                foreach (var r in root.GetProperty("rows").EnumerateArray())
                {
                    var (strategy, parameters) = runs[r.GetProperty("run_key").GetString()!];
                    var orMin = parameters.TryGetProperty("or_min", out var o) ? int.Parse(o.GetString()!, CultureInfo.InvariantCulture) : 15;
                    var rr = parameters.TryGetProperty("rr", out var q) ? decimal.Parse(q.GetString()!, CultureInfo.InvariantCulture) : 2m;
                    rows.Add(new PackedTrade(
                        r.GetProperty("seq").GetInt32(),
                        r.GetProperty("trade_key").GetString()!,
                        r.GetProperty("symbol").GetString()!,
                        string.Equals(r.GetProperty("side").GetString(), "Sell", StringComparison.OrdinalIgnoreCase) ? ReaderSide.Sell : ReaderSide.Buy,
                        DateOnly.ParseExact(r.GetProperty("date").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                        strategy,
                        orMin,
                        rr));
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
