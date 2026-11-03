using System.Globalization;
using System.Text;
using System.Text.Json;

namespace XDrishti.Domain.Learning.Extensions;

/// <summary>The P15 phase check as the verb prints it (P15-12). Text and JSON carry the same fields; a value that is not a value is the word <c>unavailable</c>, never a digit.</summary>
public sealed record EventsPhaseReport(
    string Verdict,
    string Summary,
    IReadOnlyList<EventsFamilyLine> Families,
    IReadOnlyList<string> Open,
    IReadOnlyList<EventsViolation> Violations,
    int? SeasonsHeld,
    int SeasonsNeeded,
    IReadOnlyList<EventsKindFact>? Filings,
    int? TrackedInstruments,
    int? InstrumentsWithEntry,
    double? NoUpcomingShare,
    string? NoUpcomingRecord,
    bool FilterOn,
    IReadOnlyList<string> Cut,
    IReadOnlyList<string> Limits)
{
    private const string Unavailable = "unavailable";

    /// <summary>The printable report; the limits block is always there.</summary>
    public string ToText()
    {
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append("P15 phase check (features and filters improve OOS or are dropped)\n");
        sb.Append(c, $"overall: {Verdict}\n  {Summary}\n");
        foreach (var open in Open)
        {
            sb.Append(c, $"  open: {open}\n");
        }

        sb.Append("families:\n");
        foreach (var f in Families)
        {
            sb.Append(c, $"  {f.Family}: {f.Status}{(f.Resolved ? " (resolved)" : string.Empty)}; record {f.RecordId ?? Unavailable}; trials {Show(f.Trials)}; hold-out {f.HoldOut}; {f.Detail}\n");
        }

        sb.Append("violations:\n");
        if (Violations.Count == 0)
        {
            sb.Append("  none\n");
        }

        foreach (var v in Violations)
        {
            sb.Append(c, $"  {v.Family}: {v.Text}\n");
        }

        sb.Append("evidence:\n");
        sb.Append(c, $"  seasons of bars held: {Show(SeasonsHeld)} of {SeasonsNeeded} needed\n");
        if (Filings is null)
        {
            sb.Append(c, $"  filings coverage: {Unavailable}\n");
        }
        else if (Filings.Count == 0)
        {
            sb.Append("  filings coverage: no filing stored\n");
        }
        else
        {
            foreach (var k in Filings)
            {
                sb.Append(c, $"  filings {k.Kind}: {k.Stored} stored in {k.Quarters} quarters; {k.InstrumentsWithout} tracked instruments without\n");
            }
        }

        sb.Append(c, $"  calendar coverage: {Show(InstrumentsWithEntry)} of {Show(TrackedInstruments)} tracked instruments with an entry\n");
        sb.Append(c, $"  share of entries lost to no_upcoming_entry (newest F record {NoUpcomingRecord ?? Unavailable}): {ShowShare(NoUpcomingShare)}\n");
        sb.Append("  feature sets in scoring: not built\n");
        sb.Append(c, $"  event filter switch: {(FilterOn ? "ON" : "OFF")}\n");
        sb.Append(c, $"cut: {(Cut.Count == 0 ? "none" : string.Join(", ", Cut))}\n");
        sb.Append("limits:\n");
        foreach (var limit in Limits)
        {
            sb.Append(c, $"  - {limit}\n");
        }

        return sb.ToString();
    }

    /// <summary>The same fields as JSON; the order is fixed so equal facts give equal bytes.</summary>
    public string ToJson()
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteString("verdict", Verdict);
            w.WriteString("summary", Summary);
            w.WriteStartArray("open");
            foreach (var open in Open)
            {
                w.WriteStringValue(open);
            }

            w.WriteEndArray();
            w.WriteStartArray("families");
            foreach (var f in Families)
            {
                w.WriteStartObject();
                w.WriteString("family", f.Family);
                w.WriteString("status", f.Status);
                w.WriteBoolean("resolved", f.Resolved);
                w.WriteBoolean("cut", f.Cut);
                w.WriteString("record", f.RecordId ?? Unavailable);
                Count(w, "trials", f.Trials);
                w.WriteString("hold_out", f.HoldOut);
                w.WriteString("detail", f.Detail);
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteStartArray("violations");
            foreach (var v in Violations)
            {
                w.WriteStartObject();
                w.WriteString("family", v.Family);
                w.WriteString("text", v.Text);
                if (v.DecisionId is { } id)
                {
                    w.WriteNumber("decision_id", id);
                }
                else
                {
                    w.WriteString("decision_id", Unavailable);
                }

                w.WriteString("decided_by", v.DecidedBy ?? Unavailable);
                w.WriteEndObject();
            }

            w.WriteEndArray();
            Count(w, "seasons_held", SeasonsHeld);
            w.WriteNumber("seasons_needed", SeasonsNeeded);
            if (Filings is null)
            {
                w.WriteString("filings", Unavailable);
            }
            else
            {
                w.WriteStartArray("filings");
                foreach (var k in Filings)
                {
                    w.WriteStartObject();
                    w.WriteString("kind", k.Kind);
                    w.WriteNumber("stored", k.Stored);
                    w.WriteNumber("quarters", k.Quarters);
                    w.WriteNumber("instruments_without", k.InstrumentsWithout);
                    w.WriteEndObject();
                }

                w.WriteEndArray();
            }

            Count(w, "tracked_instruments", TrackedInstruments);
            Count(w, "instruments_with_entry", InstrumentsWithEntry);
            if (NoUpcomingShare is { } share && double.IsFinite(share))
            {
                w.WriteNumber("share_lost_to_no_upcoming_entry", share);
            }
            else
            {
                w.WriteString("share_lost_to_no_upcoming_entry", Unavailable);
            }

            w.WriteString("share_record", NoUpcomingRecord ?? Unavailable);
            w.WriteString("feature_sets_in_scoring", "not built");
            w.WriteBoolean("event_filter_on", FilterOn);
            w.WriteStartArray("cut");
            foreach (var cut in Cut)
            {
                w.WriteStringValue(cut);
            }

            w.WriteEndArray();
            w.WriteStartArray("limits");
            foreach (var limit in Limits)
            {
                w.WriteStringValue(limit);
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan) + "\n";
    }

    private static void Count(Utf8JsonWriter w, string name, int? value)
    {
        if (value is { } v)
        {
            w.WriteNumber(name, v);
        }
        else
        {
            w.WriteString(name, Unavailable);
        }
    }

    private static string Show(int? value) => value is { } v ? v.ToString(CultureInfo.InvariantCulture) : Unavailable;

    private static string ShowShare(double? share) => share is { } s && double.IsFinite(s) ? (100 * s).ToString("F1", CultureInfo.InvariantCulture) + " %" : Unavailable;
}
