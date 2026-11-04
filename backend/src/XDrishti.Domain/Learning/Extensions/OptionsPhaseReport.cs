using System.Globalization;
using System.Text;
using System.Text.Json;

namespace XDrishti.Domain.Learning.Extensions;

/// <summary>The P14 phase check as the verb prints it (P14-17). Text and JSON carry the same fields; a value that is not a value is the word <c>unavailable</c>, never a digit.</summary>
public sealed record OptionsPhaseReport(
    string Verdict,
    string Summary,
    IReadOnlyList<OptionsFamilyLine> Families,
    IReadOnlyList<string> Open,
    IReadOnlyList<OptionsViolation> Violations,
    bool CellsReadable,
    bool CellsTruncated,
    double? YearsHeld,
    double YearsNeeded,
    IReadOnlyList<OptionsUnderlyingFact>? Underlyings,
    int SessionsNeeded,
    bool? RecorderOn,
    bool? BackfillOn,
    string? ArchiveNote,
    IReadOnlyList<string> Cut,
    int StaleAfterDays,
    IReadOnlyList<string> Limits)
{
    private const string Unavailable = "unavailable";

    /// <summary>The printable report; the limits block is always there.</summary>
    public string ToText()
    {
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append("P14 phase check (an option expression beats cash OOS after costs, else stays disabled)\n");
        sb.Append(c, $"overall: {Verdict}\n  {Summary}\n");
        foreach (var open in Open)
        {
            sb.Append(c, $"  open: {open}\n");
        }

        sb.Append("families:\n");
        foreach (var f in Families)
        {
            sb.Append(c, $"  {f.Family}: {f.Status}{(f.Resolved ? " (resolved)" : string.Empty)}; record {f.RecordId ?? Unavailable}; N {Show(f.Trials)}; costs {f.Basis}; hold-out {f.HoldOut}; expiry derived {ShowShare(f.DerivedExpiryShare)}; model-price fallback {ShowShare(f.FallbackShare)}; {f.Detail}\n");
        }

        sb.Append("violations:\n");
        if (!CellsReadable)
        {
            sb.Append("  unavailable: the stored plan items and paper entries could not be read\n");
        }
        else if (Violations.Count == 0)
        {
            sb.Append("  none\n");
        }

        foreach (var v in Violations)
        {
            sb.Append(c, $"  {v.Source} row {v.Id.ToString(c)}: {v.Text}\n");
        }

        if (CellsTruncated)
        {
            sb.Append("  (more rows exist; the first are listed)\n");
        }

        sb.Append("evidence:\n");
        sb.Append(c, $"  years of underlying bars held: {ShowYears(YearsHeld)} of {YearsNeeded.ToString("F0", c)} needed\n");
        if (Underlyings is null)
        {
            sb.Append(c, $"  usable chain sessions and backfill progress: {Unavailable}\n");
        }
        else if (Underlyings.Count == 0)
        {
            sb.Append("  usable chain sessions and backfill progress: no option underlying recorded\n");
        }
        else
        {
            foreach (var u in Underlyings)
            {
                sb.Append(c, $"  {u.Underlying}: usable chain sessions {Show(u.UsableSessions)} of {SessionsNeeded.ToString(c)} needed; backfill keys done {Show(u.BackfillDone)}, given up {Show(u.BackfillGivenUp)}\n");
            }
        }

        sb.Append(c, $"  chain recorder switch: {Switch(RecorderOn)}; backfill switch: {Switch(BackfillOn)}\n");
        if (ArchiveNote is not null)
        {
            sb.Append(c, $"  {ArchiveNote}\n");
        }

        sb.Append(c, $"cut: {(Cut.Count == 0 ? "none" : string.Join(", ", Cut))}\n");
        sb.Append(c, $"stale after: {StaleAfterDays.ToString(c)} days\n");
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
            Strings(w, "open", Open);
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
                w.WriteString("costs", f.Basis);
                w.WriteString("hold_out", f.HoldOut);
                Share(w, "expiry_derived_share", f.DerivedExpiryShare);
                Share(w, "model_price_fallback_share", f.FallbackShare);
                w.WriteString("detail", f.Detail);
                w.WriteEndObject();
            }

            w.WriteEndArray();
            if (CellsReadable)
            {
                w.WriteStartArray("violations");
                foreach (var v in Violations)
                {
                    w.WriteStartObject();
                    w.WriteString("source", v.Source);
                    w.WriteNumber("row_id", v.Id);
                    w.WriteString("text", v.Text);
                    w.WriteEndObject();
                }

                w.WriteEndArray();
            }
            else
            {
                w.WriteString("violations", Unavailable);
            }

            w.WriteBoolean("violations_truncated", CellsTruncated);
            Share(w, "years_held", YearsHeld);
            w.WriteNumber("years_needed", YearsNeeded);
            if (Underlyings is null)
            {
                w.WriteString("underlyings", Unavailable);
            }
            else
            {
                w.WriteStartArray("underlyings");
                foreach (var u in Underlyings)
                {
                    w.WriteStartObject();
                    w.WriteString("underlying", u.Underlying);
                    Count(w, "usable_sessions", u.UsableSessions);
                    Count(w, "backfill_done", u.BackfillDone);
                    Count(w, "backfill_given_up", u.BackfillGivenUp);
                    w.WriteEndObject();
                }

                w.WriteEndArray();
            }

            w.WriteNumber("sessions_needed", SessionsNeeded);
            w.WriteString("chain_recorder_switch", Switch(RecorderOn));
            w.WriteString("backfill_switch", Switch(BackfillOn));
            w.WriteString("archive_note", ArchiveNote ?? "none");
            Strings(w, "cut", Cut);
            w.WriteNumber("stale_after_days", StaleAfterDays);
            Strings(w, "limits", Limits);
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan) + "\n";
    }

    private static void Strings(Utf8JsonWriter w, string name, IReadOnlyList<string> values)
    {
        w.WriteStartArray(name);
        foreach (var value in values)
        {
            w.WriteStringValue(value);
        }

        w.WriteEndArray();
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

    private static void Share(Utf8JsonWriter w, string name, double? value)
    {
        if (value is { } v && double.IsFinite(v))
        {
            w.WriteNumber(name, v);
        }
        else
        {
            w.WriteString(name, Unavailable);
        }
    }

    private static string Switch(bool? on) => on is { } v ? (v ? "ON" : "OFF") : Unavailable;

    private static string Show(int? value) => value is { } v ? v.ToString(CultureInfo.InvariantCulture) : Unavailable;

    private static string ShowYears(double? years) => years is { } y && double.IsFinite(y) ? y.ToString("F1", CultureInfo.InvariantCulture) : Unavailable;

    private static string ShowShare(double? share) => share is { } s && double.IsFinite(s) ? (100 * s).ToString("F1", CultureInfo.InvariantCulture) + " %" : Unavailable;
}
