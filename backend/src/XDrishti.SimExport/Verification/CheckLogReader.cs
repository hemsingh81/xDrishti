using System.Globalization;
using XDrishti.SimExport.Cards;

namespace XDrishti.SimExport.Verification;

/// <summary>
/// The check mode's comparison (spec P3-09, V8), pure and on text the host has already read and capped: the owner's filled <c>check-log.csv</c> against the engine's <c>engine-values.csv</c>, joined by <c>trade_key</c>. For each row it reports, per
/// field of the column map, <c>ok</c>, <c>ok within tolerance</c> (one tick for a price, 0.01 for R, the minute for a time), <c>beyond tolerance</c>, <c>mismatch</c> (the exit reason is exact), <c>missing</c> or <c>unreadable</c>; a row not signed is
/// <c>unsigned</c>; a row signed <c>agree</c> with a field beyond tolerance is <c>inconsistent</c>; a <c>disagree</c> without a finding is unexplained. Only then, and last, come the lines of the pack. It prints no price, no R value and no
/// statistic, strips Control and Format characters from every owner text it echoes, and <b>never says that P3 passes</b>: that is the owner's to say in chat.
/// </summary>
public static class CheckLogReader
{
    /// <summary>The fewest trade rows against an external chart (protocol step 6).</summary>
    public const int MinExternal = 5;

    /// <summary>The tolerance of an R, in R.</summary>
    public const decimal RTolerance = 0.01m;

    /// <summary>The tick used when the engine has none for the instrument (the NSE default).</summary>
    public const decimal DefaultTick = 0.05m;

    /// <summary>The most rows that may be <c>not_decidable</c> in a complete pack (the order inside a minute that 1-minute data cannot tell).</summary>
    public const int MaxNotDecidable = 3;

    private const int MaxEcho = 120;

    private static readonly string[] Verdicts = ["agree", "disagree", "not_decidable", "skipped"];

    private static readonly string[] ExitReasons = ["stop", "target", "square_off", "max_hold"];

    private static readonly string[] ExternalColumns = ["ext_open", "ext_high", "ext_low", "ext_close"];

    private static readonly Field[] Map =
    [
        new("seen_entry_time", "entry_ist", Kind.Time, false),
        new("seen_entry_price", "entry_price", Kind.Price, false),
        new("seen_stop", "stop_price", Kind.Price, false),
        new("seen_target", "target_price", Kind.Price, true),
        new("seen_exit_time", "exit_ist", Kind.Time, false),
        new("seen_exit_price", "exit_price", Kind.Price, false),
        new("seen_exit_reason", "exit_reason", Kind.Reason, false),
        new("seen_r", "r_exact", Kind.R, false),
    ];

    private static readonly Field[] Calculated =
    [
        new("calc_entry_filled", "entry_filled_price", Kind.Price, false),
        new("calc_exit_filled", "exit_filled_price", Kind.Price, false),
        new("calc_r_filled", "r_filled", Kind.R, false),
    ];

    private enum Kind
    {
        Time,
        Price,
        Reason,
        R,
    }

    private enum Status
    {
        Ok,
        Within,
        Beyond,
        Mismatch,
        Missing,
        Unreadable,
    }

    /// <summary>Compares the log with the engine's values.</summary>
    /// <param name="logText">The text of <c>check-log.csv</c>.</param>
    /// <param name="engineText">The text of <c>engine-values.csv</c> of the same folder.</param>
    public static CheckReport Read(string logText, string engineText)
    {
        ArgumentNullException.ThrowIfNull(logText);
        ArgumentNullException.ThrowIfNull(engineText);

        var problems = new List<string>();
        var log = Table(logText, "the log", PackFiles.CheckLogColumns, problems);
        var engine = Table(engineText, "the engine values", PackFiles.EngineValueColumns, problems);
        if (problems.Count > 0 || log is null || engine is null)
        {
            return Failed(problems);
        }

        var engineKinds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var e in engine.Where(e => !engineKinds.TryAdd(e["trade_key"], e["source"] == PackPlanner.SpotSource ? "spot" : "trade")))
        {
            problems.Add("the engine values hold a trade_key twice: " + Echo(e["trade_key"]));
        }

        var byKey = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var row in log)
        {
            var key = row["trade_key"];
            if (!engineKinds.TryGetValue(key, out var kind))
            {
                problems.Add("the log has a trade_key that is not in the pack: " + Echo(key));
            }
            else if (!byKey.TryAdd(key, row))
            {
                problems.Add("the log has a row twice: " + Echo(key));
            }
            else if (!string.Equals(row["kind"], kind, StringComparison.Ordinal))
            {
                problems.Add("the kind of a row does not match the pack: " + Echo(key));
            }
        }

        return problems.Count > 0 ? Failed(problems) : Compare(engine, byKey, log.Count > 0 && log[0].ContainsKey("calc_entry_filled"));
    }

    private static CheckReport Failed(List<string> problems) => new(problems, [], 0, 0, 0, 0, false, []);

    private static List<Dictionary<string, string>>? Table(string text, string name, IReadOnlyList<string> required, List<string> problems)
    {
        var (rows, problem) = CsvText.Read(text);
        if (problem is not null)
        {
            problems.Add(name + " is not CSV: " + problem);
            return null;
        }

        rows = [.. rows.Where(r => !(r.Count == 1 && r[0].Length == 0))];
        if (rows.Count == 0)
        {
            problems.Add(name + " is empty");
            return null;
        }

        var header = rows[0];
        foreach (var column in required.Where(c => !header.Contains(c, StringComparer.Ordinal)))
        {
            problems.Add(name + " has no column " + column);
        }

        if (problems.Count > 0)
        {
            return null;
        }

        var table = new List<Dictionary<string, string>>();
        foreach (var row in rows.Skip(1))
        {
            if (row.Count != header.Count)
            {
                problems.Add(name + " has a row with " + row.Count.ToString(CultureInfo.InvariantCulture) + " cells, the header has " + header.Count.ToString(CultureInfo.InvariantCulture));
                return null;
            }

            var cells = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < header.Count; i++)
            {
                cells[header[i]] = row[i];
            }

            table.Add(cells);
        }

        return table;
    }

    private static CheckReport Compare(List<Dictionary<string, string>> engine, Dictionary<string, Dictionary<string, string>> byKey, bool hasCalculated)
    {
        var lines = new List<string>();
        var signed = 0;
        var external = 0;
        var unexplained = 0;
        var unsigned = 0;
        var notInLog = 0;
        var incompleteFields = 0;
        var badSkips = 0;
        var openFindings = 0;
        var notDecidable = 0;
        var classes = new Dictionary<string, int> { ["data"] = 0, ["model"] = 0, ["implementation"] = 0, ["unclassified"] = 0 };

        foreach (var e in engine)
        {
            var key = e["trade_key"];
            var label = "row " + Echo(e["seq"]) + " " + Echo(key);
            if (!byKey.TryGetValue(key, out var row))
            {
                lines.Add(label + ": not in the log");
                notInLog++;
                continue;
            }

            var verdict = row["verdict"].Trim().ToLowerInvariant();
            var finding = row["finding"].Trim();
            var isSpot = e["source"] == PackPlanner.SpotSource;
            if (!Verdicts.Contains(verdict, StringComparer.Ordinal))
            {
                lines.Add(label + ": unsigned" + (verdict.Length == 0 ? string.Empty : " (the verdict '" + Echo(verdict) + "' is not one of agree, disagree, not_decidable, skipped)"));
                unsigned++;
                continue;
            }

            if (finding.Length > 0)
            {
                var kind = FindingClass(finding);
                classes[kind]++;
                if (kind is "model" or "implementation" && !IsClosed(finding))
                {
                    openFindings++;
                }
            }

            if (verdict == "not_decidable")
            {
                notDecidable++;
                if (row["notes"].Trim().Length > 0)
                {
                    lines.Add(label + ": explained (not decidable from 1-minute data; note: " + Echo(row["notes"]) + ")");
                }
                else
                {
                    lines.Add(label + ": unexplained (not_decidable needs a note)");
                    unexplained++;
                }

                continue;
            }

            if (verdict == "skipped")
            {
                if (isSpot && row["notes"].Trim().Length > 0)
                {
                    lines.Add(label + ": skipped (reason: " + Echo(row["notes"]) + ")");
                }
                else
                {
                    lines.Add(label + ": " + (isSpot ? "skipped without a reason" : "a trade row cannot be skipped"));
                    badSkips++;
                }

                continue;
            }

            signed++;
            var fields = isSpot ? [] : Fields(e, row, hasCalculated);
            var chart = row["chart"].Trim();
            if (!isSpot && chart.StartsWith("external:", StringComparison.OrdinalIgnoreCase) && chart.Length > "external:".Length)
            {
                external++;
            }

            var beyond = fields.Any(f => f.Status is Status.Beyond or Status.Mismatch);
            var incomplete = fields.Any(f => f.Status is Status.Missing or Status.Unreadable);
            incompleteFields += incomplete ? 1 : 0;
            lines.Add(label + ": " + RowStatus(fields, isSpot) + Suffix(verdict, beyond, finding, ref unexplained));
            lines.AddRange(fields.Select(f => "  " + f.Name + ": " + Text(f.Status) + (f.Unit.Length > 0 ? " (" + f.Unit + ")" : string.Empty)));
        }

        return Finish(lines, engine.Count, signed, external, unexplained, unsigned, notInLog, incompleteFields, badSkips, openFindings, notDecidable, classes);
    }

    private static string Suffix(string verdict, bool beyond, string finding, ref int unexplained)
    {
        var explained = finding.Length > 0;
        if (verdict == "agree" && beyond)
        {
            if (!explained)
            {
                unexplained++;
            }

            return "; inconsistent: signed agree with a field beyond tolerance" + (explained ? ", explained by a finding (" + Echo(finding) + ")" : ", unexplained until changed or given a finding");
        }

        if (verdict == "disagree")
        {
            if (!explained)
            {
                unexplained++;
            }

            return explained ? "; disagree, explained by a finding (" + Echo(finding) + ")" : "; disagree, unexplained: no finding";
        }

        return "; agree";
    }

    private static string RowStatus(List<Result> fields, bool isSpot)
    {
        if (isSpot)
        {
            return "spot check";
        }

        var parts = new List<string>();
        foreach (var (status, word) in new[] { (Status.Beyond, "beyond tolerance"), (Status.Mismatch, "mismatch"), (Status.Missing, "missing"), (Status.Unreadable, "unreadable") })
        {
            var names = fields.Where(f => f.Status == status).Select(f => f.Name).ToList();
            if (names.Count > 0)
            {
                parts.Add(word + ": " + string.Join(", ", names));
            }
        }

        return parts.Count > 0 ? string.Join("; ", parts) : fields.Any(f => f.Status == Status.Within) ? "ok within tolerance" : "ok";
    }

    private static CheckReport Finish(
        List<string> lines, int rows, int signed, int external, int unexplained, int unsigned, int notInLog, int incompleteFields, int badSkips, int openFindings, int notDecidable, Dictionary<string, int> classes)
    {
        var reasons = new List<string>();
        Reason(reasons, unsigned, "unsigned");
        Reason(reasons, notInLog, "not in the log");
        Reason(reasons, unexplained, "unexplained");
        Reason(reasons, incompleteFields, "with a field missing or unreadable");
        Reason(reasons, badSkips, "skipped without a reason");
        if (external < MinExternal)
        {
            reasons.Add("fewer than " + MinExternal.ToString(CultureInfo.InvariantCulture) + " rows against an external chart");
        }

        if (openFindings > 0)
        {
            reasons.Add(openFindings.ToString(CultureInfo.InvariantCulture) + " model or implementation findings not marked closed");
        }

        if (classes["unclassified"] > 0)
        {
            reasons.Add(classes["unclassified"].ToString(CultureInfo.InvariantCulture) + " findings without a class (write data:, model: or implementation:)");
        }

        if (notDecidable > MaxNotDecidable)
        {
            reasons.Add(notDecidable.ToString(CultureInfo.InvariantCulture) + " rows not decidable, more than " + MaxNotDecidable.ToString(CultureInfo.InvariantCulture) + " are allowed");
        }

        var n = (int v) => v.ToString(CultureInfo.InvariantCulture);
        lines.Add(string.Empty);
        lines.Add("rows in the pack: " + n(rows));
        lines.Add("rows signed: " + n(signed));
        lines.Add("rows against an external chart: " + n(external) + " (at least " + n(MinExternal) + " are needed)");
        lines.Add("rows unexplained: " + n(unexplained));
        lines.Add("rows not decidable: " + n(notDecidable) + " (at most " + n(MaxNotDecidable) + " are allowed)");
        lines.Add("findings by class: data " + n(classes["data"]) + ", model " + n(classes["model"]) + ", implementation " + n(classes["implementation"]) + ", unclassified " + n(classes["unclassified"]));
        lines.Add(reasons.Count == 0 ? "pack: complete" : "pack: incomplete (" + string.Join("; ", reasons) + ")");
        return new CheckReport([], lines, rows, signed, external, unexplained, reasons.Count == 0, reasons);
    }

    private static void Reason(List<string> reasons, int count, string what)
    {
        if (count > 0)
        {
            reasons.Add(count.ToString(CultureInfo.InvariantCulture) + " rows " + what);
        }
    }

    private static string FindingClass(string finding)
    {
        var colon = finding.IndexOf(':', StringComparison.Ordinal);
        var word = (colon < 0 ? string.Empty : finding[..colon]).Trim().ToLowerInvariant();
        return word is "data" or "model" or "implementation" ? word : "unclassified";
    }

    private static List<Result> Fields(Dictionary<string, string> engine, Dictionary<string, string> row, bool hasCalculated)
    {
        var tick = decimal.TryParse(engine["tick"], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed > 0 ? parsed : DefaultTick;
        var results = new List<Result>();
        var chart = row["chart"].Trim();
        results.Add(new Result("chart", chart.Length == 0 ? Status.Missing : chart.Equals("card", StringComparison.OrdinalIgnoreCase) || (chart.StartsWith("external:", StringComparison.OrdinalIgnoreCase) && chart.Length > "external:".Length) ? Status.Ok : Status.Unreadable));
        results.AddRange(Map.Select(f => Judged(f, row[f.Seen], engine[f.Engine], tick)));
        if (hasCalculated)
        {
            results.AddRange(Calculated.Where(f => row.TryGetValue(f.Seen, out var value) && value.Trim().Length > 0).Select(f => Judged(f, row[f.Seen], engine[f.Engine], tick)));
        }

        if (chart.StartsWith("external:", StringComparison.OrdinalIgnoreCase))
        {
            var ext = ExternalColumns.ToDictionary(c => c, c => Number(row[c], out var value) && value > 0 ? (decimal?)value : null, StringComparer.Ordinal);
            var consistent = ext.Values.All(v => v is not null) && ext["ext_high"] >= ext["ext_low"] && ext["ext_open"] >= ext["ext_low"] && ext["ext_open"] <= ext["ext_high"] && ext["ext_close"] >= ext["ext_low"] && ext["ext_close"] <= ext["ext_high"];
            results.AddRange(ExternalColumns.Select(c => new Result(c, row[c].Trim().Length == 0 ? Status.Missing : ext[c] is null || !consistent ? Status.Unreadable : Status.Ok)));
        }

        return results;
    }

    private static Result Judged(Field field, string seen, string wanted, decimal tick)
    {
        var status = Judge(field, seen, wanted, tick);
        return new Result(field.Seen, status, status == Status.Within ? (field.Kind == Kind.R ? "the R tolerance" : "one tick") : string.Empty);
    }

    /// <summary>Whether a finding says <c>closed</c> as a whole word, not negated by the word before it, followed by a commit (7 to 40 hex) or an ADR reference.</summary>
    private static bool IsClosed(string finding)
    {
        var words = finding.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i + 1 < words.Length; i++)
        {
            if (!string.Equals(words[i], "closed", StringComparison.OrdinalIgnoreCase) || (i > 0 && words[i - 1].ToLowerInvariant() is "not" or "no" or "never" or "un"))
            {
                continue;
            }

            var next = words[i + 1].TrimEnd('.', ',', ';', ')');
            if (next.Length is >= 7 and <= 40 && next.All(char.IsAsciiHexDigit) || next.StartsWith("ADR", StringComparison.OrdinalIgnoreCase) && next.Length >= 3)
            {
                return true;
            }
        }

        return false;
    }

    private static Status Judge(Field field, string seenText, string engineText, decimal tick)
    {
        var seen = seenText.Trim();
        var wanted = engineText.Trim();
        if (field.Optional && wanted.Length == 0)
        {
            return seen.Length == 0 ? Status.Ok : Status.Mismatch;
        }

        if (seen.Length == 0)
        {
            return Status.Missing;
        }

        switch (field.Kind)
        {
            case Kind.Reason:
                return string.Equals(seen, wanted, StringComparison.Ordinal) ? Status.Ok : ExitReasons.Contains(seen, StringComparer.Ordinal) ? Status.Mismatch : Status.Unreadable;
            case Kind.Time:
                return Time(seen, wanted);
            default:
                if (!Number(seen, out var a) || !Number(wanted, out var b))
                {
                    return Status.Unreadable;
                }

                var difference = Math.Abs(a - b);
                return difference == 0 ? Status.Ok : difference <= (field.Kind == Kind.R ? RTolerance : tick) ? Status.Within : Status.Beyond;
        }
    }

    private static Status Time(string seen, string wanted)
    {
        if (seen.Length == 5 && seen[2] == ':' && wanted.Length == 16)
        {
            return string.Equals(seen, wanted[11..], StringComparison.Ordinal) ? Status.Ok : Status.Beyond;
        }

        return seen.Length == 16 && seen[10] == ' ' ? string.Equals(seen, wanted, StringComparison.Ordinal) ? Status.Ok : Status.Beyond : Status.Unreadable;
    }

    private static bool Number(string text, out decimal value)
    {
        var clean = text.Trim().TrimStart('\'');
        return decimal.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string Text(Status status) => status switch
    {
        Status.Ok => "ok",
        Status.Within => "ok within tolerance",
        Status.Beyond => "beyond tolerance",
        Status.Mismatch => "mismatch",
        Status.Missing => "missing",
        _ => "unreadable",
    };

    /// <summary>An owner's text as it is echoed to the console: Control and Format characters removed (a terminal escape sequence, a bidi override), cut.</summary>
    private static string Echo(string text)
    {
        var clean = Html.Strip(text).Trim();
        return clean.Length > MaxEcho ? clean[..MaxEcho] + "..." : clean;
    }

    private sealed record Field(string Seen, string Engine, Kind Kind, bool Optional);

    private sealed record Result(string Name, Status Status, string Unit = "");
}
