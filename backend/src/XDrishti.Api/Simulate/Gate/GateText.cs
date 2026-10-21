using System.Globalization;
using System.Text;
using System.Text.Json;

namespace XDrishti.Api.Simulate.Gate;

/// <summary>
/// <c>gate-decision.json</c> and <c>gate-decision.md</c> (spec P3-09d D5): the status, the counts, every finding, every spot result, the numbered next actions and the SHA-256 of every input. No time and no machine fact, so the same inputs give the same bytes.
/// The honest limit is printed in both files.
/// </summary>
internal static class GateText
{
    public const string Rules = "gate-1";

    public const string Limit = "This gate shows that the engine follows its written rules on an independent reading of the bars (stored and second source). It is not a chart-by-eye check, and a misreading shared by the same model family stays possible. "
        + "A third independent source or an owner's chart check can be added later as extra evidence.";

    /// <summary>Statements printed in both files about what a result does and does not mean.</summary>
    public static IReadOnlyList<string> Notes { get; } =
    [
        "A confirmed flagged day means only that the raw stored jump exceeds 20 %; the replay judges adjusted prices, so this is not a check of the replay's own flag.",
        "The pack holds only trades the engine made. A trade the engine skipped but the reader would have taken cannot appear as a row, so the gate cannot see that direction; it is covered by the oracle of P3-08 (ADR 0047), not by the gate.",
    ];

    public static string Json(string packId, string seed, GateDecision decision, IReadOnlyList<(string Name, string Sha256)> inputs)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(inputs);
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, NewLine = "\n" }))
        {
            json.WriteStartObject();
            json.WriteString("format", "xd-gate-1");
            json.WriteString("rules", Rules);
            json.WriteString("pack_id", packId);
            json.WriteString("seed", seed);
            json.WriteString("status", GateStatuses.Code(decision.Status));
            json.WriteNumber("exit_code", GateStatuses.ExitCode(decision.Status));
            json.WriteString("limit", Limit);
            json.WriteStartArray("notes");
            foreach (var n in Notes)
            {
                json.WriteStringValue(n);
            }

            json.WriteEndArray();
            json.WriteStartObject("inputs");
            foreach (var (name, hash) in inputs)
            {
                json.WriteString(name, hash);
            }

            json.WriteEndObject();
            var c = decision.Counts;
            json.WriteStartObject("counts");
            json.WriteNumber("rows", c.Rows);
            json.WriteNumber("defects", c.Defects);
            json.WriteNumber("data_repairs", c.Repairs);
            json.WriteNumber("reruns", c.Reruns);
            json.WriteNumber("data_findings", c.DataFindings);
            json.WriteNumber("spots_confirmed", c.SpotsConfirmed);
            json.WriteNumber("spots_contradicted", c.SpotsContradicted);
            json.WriteNumber("spots_not_machine_checkable", c.SpotsNotCheckable);
            json.WriteEndObject();
            Findings(json, "defects", decision.Defects);
            Findings(json, "data_repairs", decision.Repairs);
            Findings(json, "reruns", decision.Reruns);
            Findings(json, "data_findings", decision.DataFindings);
            json.WriteStartArray("spots");
            foreach (var s in decision.Spots)
            {
                json.WriteStartObject();
                json.WriteNumber("seq", s.Line.Seq);
                json.WriteString("key", s.Line.Key);
                json.WriteString("outcome", Outcome(s.Outcome));
                json.WriteString("checked", s.Checked);
                json.WriteString("not_checked", s.NotChecked);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteStartArray("next_actions");
            foreach (var a in decision.NextActions)
            {
                json.WriteStringValue(a);
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    public static string Markdown(string packId, string seed, GateDecision decision, IReadOnlyList<(string Name, string Sha256)> inputs)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(inputs);
        var c = decision.Counts;
        var text = new StringBuilder();
        text.Append("# Gate decision: ").Append(GateStatuses.Code(decision.Status)).Append("\n\nPack `").Append(packId).Append("`, seed `").Append(seed).Append("`, rules `").Append(Rules).Append("`, exit code ").Append(GateStatuses.ExitCode(decision.Status).ToString(CultureInfo.InvariantCulture)).Append("\n\n")
            .Append("> ").Append(Limit).Append("\n\n## Next actions\n\n");
        for (var i = 0; i < decision.NextActions.Count; i++)
        {
            text.Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append(". ").Append(decision.NextActions[i]).Append('\n');
        }

        text.Append("\n## Counts (independent: none is hidden by the status)\n\n")
            .Append("- rows: ").Append(c.Rows.ToString(CultureInfo.InvariantCulture)).Append('\n')
            .Append("- defects: ").Append(c.Defects.ToString(CultureInfo.InvariantCulture)).Append('\n')
            .Append("- data repairs: ").Append(c.Repairs.ToString(CultureInfo.InvariantCulture)).Append('\n')
            .Append("- rerun rows: ").Append(c.Reruns.ToString(CultureInfo.InvariantCulture)).Append('\n')
            .Append("- data findings: ").Append(c.DataFindings.ToString(CultureInfo.InvariantCulture)).Append('\n')
            .Append("- spot lines confirmed / contradicted / not machine-checkable: ").Append(c.SpotsConfirmed.ToString(CultureInfo.InvariantCulture)).Append(" / ").Append(c.SpotsContradicted.ToString(CultureInfo.InvariantCulture)).Append(" / ").Append(c.SpotsNotCheckable.ToString(CultureInfo.InvariantCulture)).Append('\n');
        Section(text, "Defects (engine or rules; a minimal repro per row)", decision.Defects);
        Section(text, "Data repair needed (instrument and day)", decision.Repairs);
        Section(text, "Rerun needed (no second source)", decision.Reruns);
        Section(text, "Data findings (open data-quality findings, not P3 blockers)", decision.DataFindings);
        text.Append("\n## Spot checks\n\n");
        if (decision.Spots.Count == 0)
        {
            text.Append("none in the pack\n");
        }

        foreach (var s in decision.Spots)
        {
            text.Append("- ").Append(s.Line.Seq.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(s.Line.Key).Append(": ").Append(Outcome(s.Outcome));
            if (s.Checked.Length > 0)
            {
                text.Append(". Checked: ").Append(s.Checked);
            }

            if (s.NotChecked.Length > 0)
            {
                text.Append(". Not checked by the machine: ").Append(s.NotChecked);
            }

            text.Append('\n');
        }

        text.Append("\n## Notes\n\n");
        foreach (var n in Notes)
        {
            text.Append("- ").Append(n).Append('\n');
        }

        text.Append("\n## Inputs (SHA-256)\n\n");
        foreach (var (name, hash) in inputs)
        {
            text.Append("- ").Append(name).Append(": `").Append(hash).Append("`\n");
        }

        return text.ToString();
    }

    private static void Section(StringBuilder text, string title, IReadOnlyList<GateFinding> findings)
    {
        text.Append("\n## ").Append(title).Append("\n\n");
        if (findings.Count == 0)
        {
            text.Append("none\n");
        }

        foreach (var f in findings)
        {
            text.Append("- ").Append(f.Seq.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(f.TradeKey).Append(": ").Append(f.Detail).Append('\n');
        }
    }

    private static void Findings(Utf8JsonWriter json, string name, IReadOnlyList<GateFinding> findings)
    {
        json.WriteStartArray(name);
        foreach (var f in findings)
        {
            json.WriteStartObject();
            json.WriteNumber("seq", f.Seq);
            json.WriteString("trade_key", f.TradeKey);
            json.WriteString("symbol", f.Symbol);
            json.WriteString("date", f.Date == default ? string.Empty : f.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            json.WriteString("detail", f.Detail);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    private static string Outcome(SpotOutcome outcome) => outcome switch
    {
        SpotOutcome.Confirmed => "confirmed",
        SpotOutcome.Contradicted => "contradicted",
        _ => "not_machine_checkable",
    };
}
