using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using XDrishti.Domain.Research;
using XDrishti.SimExport.Cards;
using XDrishti.SimExport.Csv;
using XDrishti.SimExport.Text;

namespace XDrishti.SimExport.Verification;

/// <summary>
/// The names, the keys and the file contents of the verification pack (spec P3-09, V5 and V7). The pack holds <c>pack.json</c> (identity and sources, no outcome of a row), <c>README.txt</c>, <c>index.html</c>, the cards,
/// <c>engine-values.csv</c> (the engine's side, with the source and categories of each row, kept apart on purpose), <c>check-log.csv</c> (the owner's log, no engine column, no source, no category) and <c>manifest.json</c>. Writes to a sink
/// the host owns. No statistic: no count of a group, no sum or mean of R, no percentage.
/// </summary>
public static class PackFiles
{
    /// <summary>The format string of the pack.</summary>
    public const string Format = "xd-verify-pack-1";

    /// <summary>The folder below the export root.</summary>
    public const string Folder = "verify";

    public const string PackJsonName = "pack.json";

    public const string ReadmeName = "README.txt";

    public const string EngineValuesName = "engine-values.csv";

    public const string CheckLogName = "check-log.csv";

    private const int MinDigits = 3;

    private static readonly JsonWriterOptions JsonOptions = new() { Indented = false, Encoder = JavaScriptEncoder.Default, SkipValidation = false };

    /// <summary>The columns of the owner's log, in order.</summary>
    public static IReadOnlyList<string> CheckLogColumns { get; } = Array.AsReadOnly(
    [
        "seq", "trade_key", "kind", "chart", "seen_entry_time", "seen_entry_price", "seen_stop", "seen_target", "seen_exit_time", "seen_exit_price", "seen_exit_reason", "seen_r",
        "ext_open", "ext_high", "ext_low", "ext_close", "verdict", "finding", "notes", "signed_on",
    ]);

    /// <summary>The columns of <c>engine-values.csv</c>, in order.</summary>
    public static IReadOnlyList<string> EngineValueColumns { get; } = Array.AsReadOnly(
    [
        "seq", "trade_key", "source", "categories", "tick", "entry_ist", "entry_price", "stop_price", "target_price", "exit_ist", "exit_price", "exit_reason", "r_exact",
        "entry_fill", "entry_filled_price", "exit_filled_price", "slippage_r", "r_filled", "r_net", "cost_total",
    ]);

    /// <summary>The name of the card of a pack row: <c>cards/&lt;nnn&gt;-&lt;ticker&gt;-&lt;side&gt;-&lt;date&gt;.html</c>, with no exit reason (the name of a run's card has it; a pack's name must not say how the trade ended).</summary>
    public static string CardName(int seq, int rows, SimTrade trade)
    {
        ArgumentNullException.ThrowIfNull(trade);
        ArgumentOutOfRangeException.ThrowIfLessThan(seq, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(seq, rows);

        var digits = Math.Max(MinDigits, rows.ToString(CultureInfo.InvariantCulture).Length);
        var name = string.Create(
            CultureInfo.InvariantCulture,
            $"{CardNames.Folder}/{seq.ToString("D" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)}-{CardNames.Ticker(trade.Symbol)}-{ExportSpellings.Of(trade.Side).ToLowerInvariant()}-{trade.TradeDate:yyyy-MM-dd}.html");
        return name.Length <= CardNames.MaxName ? name : throw new InvalidOperationException("A card name is longer than " + CardNames.MaxName.ToString(CultureInfo.InvariantCulture) + " characters.");
    }

    /// <summary>The pack key: the SHA-256 of the seed, the sampler version, the count, the basket id (or <c>none</c>, because the run key does not cover it) and the sorted <c>strategy=run key</c> lines.</summary>
    public static string PackKey(string seed, int count, Guid? basketId, IReadOnlyList<SimulationRun> runs)
    {
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentNullException.ThrowIfNull(runs);

        var text = new StringBuilder();
        text.Append("seed=").Append(seed).Append('\n');
        text.Append("sampler=").Append(VerificationSampler.Version).Append('\n');
        text.Append("count=").Append(count.ToString(CultureInfo.InvariantCulture)).Append('\n');
        text.Append("basket=").Append(basketId is { } basket ? basket.ToString("D") : "none").Append('\n');
        foreach (var line in runs.Select(r => r.Strategy.Id + "=" + r.RunKey).Order(StringComparer.Ordinal))
        {
            text.Append("run=").Append(line).Append('\n');
        }

        return CanonicalText.Sha256Hex(text.ToString());
    }

    /// <summary>
    /// The folder name of a pack: the seed in lower case (a character other than a letter or digit becomes a hyphen, leading hyphens are cut, the writer's run-id rule needs <c>a-z 0-9 -</c> starting with a letter or digit), a hyphen and
    /// the first 12 hex of the pack key. The hex part keeps two seeds that differ only in case, or in a '.' against a '_', apart.
    /// </summary>
    public static string PackId(string seed, string packKey)
    {
        ArgumentNullException.ThrowIfNull(seed);
        ExportText.RequireRunKey(packKey, nameof(packKey));

        var clean = string.Concat(seed.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')).TrimStart('-');
        return (clean.Length == 0 ? "pack" : clean) + "-" + packKey[..12];
    }

    /// <summary>The extra lines of the manifest: the seed, the sampler, the count, one run per strategy and one row per trade (no source, no category).</summary>
    public static IReadOnlyList<string> ExtraLines(string seed, int count, IReadOnlyList<SimulationRun> runs, PackPlan plan)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(plan);

        var lines = new List<string>
        {
            "pack.seed=" + seed,
            "pack.sampler=" + VerificationSampler.Version,
            "pack.count=" + count.ToString(CultureInfo.InvariantCulture),
        };
        lines.AddRange(runs.Select(r => "pack.run=" + r.Strategy.Id + "|" + r.RunKey));
        lines.AddRange(plan.Rows.Select(r => "pack.row=" + r.Seq.ToString(CultureInfo.InvariantCulture) + "|" + r.Trade.Trade.Key));
        lines.AddRange(plan.Spots.Select(s => "pack.spot=" + s.Seq.ToString(CultureInfo.InvariantCulture) + "|" + s.Key));
        return lines;
    }

    /// <summary>The blank log: the header, then one row per trade and per spot check with the sequence number, the key and the kind only.</summary>
    public static void WriteCheckLog(IBufferWriter<byte> sink, PackPlan plan)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(plan);

        var row = new CsvRow();
        Header(row, sink, CheckLogColumns);
        foreach (var r in plan.Rows)
        {
            LogRow(row, sink, r.Seq, r.Trade.Trade.Key, "trade");
        }

        foreach (var s in plan.Spots)
        {
            LogRow(row, sink, s.Seq, s.Key, "spot");
        }
    }

    /// <summary>The engine's side: by sequence number and key, the source, the categories and the values the owner's log is compared with. A spot line has its kind in the categories and nothing else.</summary>
    public static void WriteEngineValues(IBufferWriter<byte> sink, PackPlan plan)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(plan);

        var row = new CsvRow();
        Header(row, sink, EngineValueColumns);
        foreach (var r in plan.Rows)
        {
            var t = r.Trade.Trade;
            row.Begin();
            row.Number(r.Seq);
            row.Text(t.Key);
            row.Raw(r.Source);
            row.Text(string.Join(';', r.Categories));
            if (r.Tick is { } tick)
            {
                row.Number(tick);
            }
            else
            {
                row.Empty();
            }

            row.IndiaTime(t.EntryEpochSeconds);
            row.Number(t.EntryPrice);
            row.Number(t.StopPrice);
            if (t.TargetPrice is { } target)
            {
                row.Number(target);
            }
            else
            {
                row.Empty();
            }

            row.IndiaTime(t.ExitEpochSeconds);
            row.Number(t.ExitPrice);
            row.Raw(ExportSpellings.Of(t.ExitReason));
            row.Number(t.RExact);
            row.Raw(ExportSpellings.Of(t.EntryFill));
            row.Number(t.EntryFilledPrice);
            row.Number(t.ExitFilledPrice);
            row.Number(t.SlippageR);
            row.Number(t.RFilled);
            if (t.Costing is { } costing)
            {
                row.Number(costing.RNet);
                row.Number(costing.TotalCost);
            }
            else
            {
                row.Empty();
                row.Empty();
            }

            row.End(sink);
        }

        foreach (var s in plan.Spots)
        {
            row.Begin();
            row.Number(s.Seq);
            row.Text(s.Key);
            row.Raw(PackPlanner.SpotSource);
            row.Text(s.Kind + (s.Detail.Length == 0 ? string.Empty : ":" + s.Detail));
            for (var i = 4; i < EngineValueColumns.Count; i++)
            {
                row.Empty();
            }

            row.End(sink);
        }
    }

    /// <summary>
    /// <c>pack.json</c>: identity and sources only. The seed, the sampler, the count, the basket id, each run (id, parameters as run, run key, engine version, schema, data range, universe, cost choice, labels), the bar-stream hash of every
    /// instrument that has a row, each row's sequence number, key, symbol, side, date and run key, the spot lines, and the category table with <c>occurs</c>. No source, category or exit reason per row, no count of a group, no R.
    /// </summary>
    public static void WritePackJson(IBufferWriter<byte> sink, string seed, int count, string packId, string packKey, Guid? basketId, IReadOnlyList<PackRun> runs, PackPlan plan)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(plan);

        using var json = new Utf8JsonWriter(sink, JsonOptions);
        json.WriteStartObject();
        json.WriteString("format", Format);
        json.WriteString("pack_id", packId);
        json.WriteString("pack_key", packKey);
        json.WriteString("seed", seed);
        json.WriteString("sampler", VerificationSampler.Version);
        json.WriteNumber("count", count);
        json.WriteBoolean("session_notes_cut", runs.Any(r => r.Run.Instruments.Any(i => i.NotesTruncated)));
        if (basketId is { } basket)
        {
            json.WriteString("basket_id", basket);
        }
        else
        {
            json.WriteNull("basket_id");
        }

        json.WriteStartArray("runs");
        foreach (var (run, labels) in runs.Select(r => (r.Run, r.Labels)))
        {
            WriteRun(json, run, labels);
        }

        json.WriteEndArray();
        WriteInstruments(json, runs, plan);
        json.WriteStartArray("rows");
        foreach (var r in plan.Rows)
        {
            var t = r.Trade.Trade;
            json.WriteStartObject();
            json.WriteNumber("seq", r.Seq);
            json.WriteString("trade_key", t.Key);
            json.WriteString("symbol", t.Symbol);
            json.WriteString("side", ExportSpellings.Of(t.Side));
            json.WriteString("date", t.TradeDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            json.WriteString("run_key", runs[r.RunIndex].Run.RunKey);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteStartArray("spots");
        foreach (var s in plan.Spots)
        {
            json.WriteStartObject();
            json.WriteNumber("seq", s.Seq);
            json.WriteString("key", s.Key);
            json.WriteString("symbol", s.Symbol);
            json.WriteString("date", s.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteStartArray("categories");
        foreach (var code in EdgeCategories.Codes)
        {
            json.WriteStartObject();
            json.WriteString("code", code);
            json.WriteBoolean("occurs", plan.Occurs.GetValueOrDefault(code));
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    /// <summary><c>README.txt</c>: the banner of every run first, then what the folder is and is not, the seed and the protocol in short.</summary>
    public static string ReadmeText(string packId, string seed, IReadOnlyList<PackRun> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);

        var text = new StringBuilder();
        foreach (var run in runs)
        {
            text.Append(RunBanner.Text(run.Labels)).Append('\n');
        }

        text.Append("This folder (").Append(packId).Append(") is a sheet for checking the simulator by hand against charts. Nothing in it is evidence of an edge.\n");
        text.Append("seed: ").Append(seed).Append("  sampler: ").Append(VerificationSampler.Version).Append('\n');
        text.Append('\n');
        text.Append("  index.html          the trades of the pack with a link to the card of each, and the spot checks\n");
        text.Append("  cards/              one page per trade: the day's bars, the rule text and the engine's marks\n");
        text.Append("  check-log.csv       your log, one row per trade and per spot check; this is the one file you edit\n");
        text.Append("  engine-values.csv   the engine's side of the comparison; keep it closed until your row is written\n");
        text.Append("  pack.json           the identity and the sources of the pack\n");
        text.Append("  manifest.json       the size and SHA-256 of every file\n");
        text.Append('\n');
        text.Append("The protocol in short (the full text is docs/research/p3-hand-check.md):\n");
        text.Append("  1. The seed and the window were written down before the draw. Look at no trade of the window first.\n");
        text.Append("  2. A bar is labelled by its start, India time. Use the 1-minute chart of the day, resampled to the timeframe of the strategy with the first bucket at 09:15.\n");
        text.Append("  3. For each trade, read the levels off the chart and write the entry, stop, target and exit into check-log.csv before you open the card's marks or engine-values.csv.\n");
        text.Append("  4. Tolerances: a price one tick, R 0.01, a time one minute, the exit reason exactly.\n");
        text.Append("  5. At least 5 rows against an external chart, one of them a sell; for these write the open, high, low and close of the minute at the entry.\n");
        text.Append("  6. Sign each row: verdict agree or disagree. A disagreement gets a finding: data, model or implementation.\n");
        text.Append("  The index links to cards that show the engine's marks (the exit reason and the result): the blindness is a convention, so write your row before you follow a link.\n");
        text.Append("  7. Run the check mode on your log to compare it with the engine. After any fix draw a new pack with a new seed.\n");
        text.Append('\n');
        text.Append("Keep this folder on this machine.\n");
        return text.ToString();
    }

    private static void Header(CsvRow row, IBufferWriter<byte> sink, IReadOnlyList<string> columns)
    {
        row.Begin();
        foreach (var column in columns)
        {
            row.Raw(column);
        }

        row.End(sink);
    }

    private static void LogRow(CsvRow row, IBufferWriter<byte> sink, int seq, string key, string kind)
    {
        row.Begin();
        row.Number(seq);
        row.Text(key);
        row.Raw(kind);
        for (var i = 3; i < CheckLogColumns.Count; i++)
        {
            row.Empty();
        }

        row.End(sink);
    }

    private static void WriteRun(Utf8JsonWriter json, SimulationRun run, RunLabelSet labels)
    {
        json.WriteStartObject();
        json.WriteString("strategy", run.Strategy.Id);
        json.WriteStartObject("parameters");
        foreach (var parameter in run.Strategy.Parameters.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            json.WriteString(parameter.Key, CanonicalText.Number(parameter.Value));
        }

        json.WriteEndObject();
        json.WriteString("run_key", run.RunKey);
        json.WriteString("engine_version", run.EngineVersion);
        json.WriteNumber("schema", run.SchemaVersion);
        json.WriteString("window_from", run.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        json.WriteString("window_to", run.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        var first = run.Instruments.Where(i => i.FirstBar is not null).Select(i => i.FirstBar!.Value).DefaultIfEmpty().Min();
        var last = run.Instruments.Where(i => i.LastBar is not null).Select(i => i.LastBar!.Value).DefaultIfEmpty().Max();
        json.WriteString("first_bar", first == default ? string.Empty : first.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        json.WriteString("last_bar", last == default ? string.Empty : last.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        json.WriteString("universe_kind", ExportSpellings.Of(run.Universe.Kind));
        json.WriteString("universe_label", run.Universe.Label);
        json.WriteString("costs", run.Costs is { } costs ? CostDescription.Label(costs) : CostDescription.None);
        json.WriteStartArray("labels");
        foreach (var line in labels.Lines)
        {
            json.WriteStringValue(line);
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    private static void WriteInstruments(Utf8JsonWriter json, IReadOnlyList<PackRun> runs, PackPlan plan)
    {
        json.WriteStartArray("instruments");
        var seen = new HashSet<(int, Guid)>();
        foreach (var row in plan.Rows.OrderBy(r => r.Trade.Trade.Symbol, StringComparer.Ordinal).ThenBy(r => r.RunIndex))
        {
            var id = row.Trade.Trade.InstrumentId;
            if (!seen.Add((row.RunIndex, id)))
            {
                continue;
            }

            var instrument = runs[row.RunIndex].Run.Instruments.FirstOrDefault(i => i.InstrumentId == id);
            json.WriteStartObject();
            json.WriteString("symbol", row.Trade.Trade.Symbol);
            json.WriteString("run_key", runs[row.RunIndex].Run.RunKey);
            json.WriteString("bars_sha256_12", instrument?.BarsSha256 is { Length: >= 12 } hash ? hash[..12] : string.Empty);
            json.WriteString("bars_sha256", instrument?.BarsSha256 ?? string.Empty);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }
}
