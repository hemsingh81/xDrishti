using System.Buffers;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using XDrishti.Domain.Research;

namespace XDrishti.SimExport.Json;

/// <summary>
/// <c>exit-selection.json</c> and the record the store keeps (spec P4-06, D9, D10): the inputs, the folds with their per-candidate tables, the final choice, the pooled out-of-sample figures, the verdict and the banner lines, in compact JSON with fixed
/// member order. Numbers are printed to six decimals, so the same inputs give the same bytes. No clock and no machine value. Streams to a sink the host owns.
/// </summary>
public static class ExitSelectionJson
{
    /// <summary>The schema of the record.</summary>
    public const int Schema = ExitSelection.Schema;

    private static readonly JsonWriterOptions WriterOptions = new() { Indented = false, Encoder = JavaScriptEncoder.Default, SkipValidation = false };

    /// <summary>Writes the record.</summary>
    /// <param name="sink">Where the UTF-8 bytes go; the host owns it.</param>
    /// <param name="selection">The record.</param>
    /// <param name="format">The format string of the export; a string the caller gives, so that no assembly version is written.</param>
    public static void Write(IBufferWriter<byte> sink, ExitSelection selection, string format)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        var input = selection.Input;
        var o = input.Options;
        using var w = new Utf8JsonWriter(sink, WriterOptions);
        w.WriteStartObject();
        w.WriteString("format", format);
        w.WriteNumber("schema", Schema);
        w.WriteString("cell", input.Cell);
        w.WriteString("unit", input.Unit);
        w.WriteBoolean("overlap_not_gated", input.OverlapNotGated);
        w.WriteString("entries_key", input.EntriesKey);
        w.WriteStartObject("inputs");
        w.WriteNumber("min_train_months", o.MinTrainMonths);
        w.WriteNumber("test_months", o.TestMonths);
        w.WriteNumber("embargo_sessions", o.EmbargoSessions);
        Number(w, "lambda", o.Lambda);
        w.WriteNumber("min_trades", o.MinTrades);
        w.WriteBoolean("prefer_plateau", o.PreferPlateau);
        Number(w, "stable_folds_pct", o.StableFoldsPct);
        w.WriteEndObject();
        w.WriteStartArray("candidates");
        foreach (var candidate in input.Candidates)
        {
            w.WriteStringValue(candidate.Id);
        }

        w.WriteEndArray();
        w.WriteString("default", selection.Default);
        Date(w, "first_entry", selection.FirstEntry);
        Date(w, "last_entry", selection.LastEntry);
        w.WriteNumber("dropped_windows", selection.DroppedWindows);
        w.WriteStartArray("folds");
        foreach (var fold in selection.Folds)
        {
            WriteFold(w, fold);
        }

        w.WriteEndArray();
        w.WriteString("final_candidate", selection.FinalCandidate);
        w.WriteBoolean("final_on_plateau", selection.FinalPlateau);
        w.WriteBoolean("final_lone_peak", selection.FinalLonePeak);
        w.WriteString("chosen", selection.Chosen);
        w.WriteString("no_choice_reason", selection.NoChoiceReason);
        w.WriteStartArray("pooled");
        foreach (var figure in selection.Pooled)
        {
            w.WriteStartObject();
            w.WriteString("name", figure.Name);
            w.WriteNumber("trades", figure.Trades);
            Number(w, "mean_r", figure.MeanR);
            Number(w, "score", figure.Score);
            w.WriteEndObject();
        }

        w.WriteEndArray();
        var s = selection.Stability;
        w.WriteStartObject("stability");
        if (s.Stable is { } stable)
        {
            w.WriteBoolean("stable", stable);
        }
        else
        {
            w.WriteNull("stable");
        }

        w.WriteString("reason", s.Reason);
        w.WriteNumber("folds", s.Folds);
        w.WriteString("modal", s.Modal);
        w.WriteNumber("agreeing", s.Agreeing);
        w.WriteNumber("positive_out_of_sample", s.PositiveOutOfSample);
        Number(w, "chance_pass", s.ChancePass);
        w.WriteNumber("lone_peaks", s.LonePeaks);
        w.WriteEndObject();
        w.WriteStartArray("banner");
        foreach (var line in selection.Banner)
        {
            w.WriteStringValue(line);
        }

        w.WriteEndArray();
        w.WriteEndObject();
        w.Flush();
    }

    private static void WriteFold(Utf8JsonWriter w, SelectionFold fold)
    {
        w.WriteStartObject();
        w.WriteNumber("index", fold.Index);
        w.WriteString("test_start", fold.TestStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        w.WriteString("test_end", fold.TestEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Date(w, "train_cutoff", fold.TrainCutoff);
        w.WriteNumber("train_trades", fold.TrainTrades);
        w.WriteNumber("test_trades", fold.TestTrades);
        w.WriteNumber("embargoed", fold.Embargoed);
        w.WriteNumber("purged", fold.Purged);
        w.WriteString("winner", fold.Winner);
        w.WriteBoolean("on_plateau", fold.OnPlateau);
        w.WriteBoolean("isolated", fold.Isolated);
        w.WriteBoolean("lone_peak", fold.LonePeak);
        Number(w, "winner_train_score", fold.WinnerTrainScore);
        w.WriteNumber("winner_test_trades", fold.WinnerTestTrades);
        Number(w, "winner_test_score", fold.WinnerTestScore);
        w.WriteBoolean("thin", fold.Thin);
        w.WriteStartArray("rows");
        foreach (var row in fold.Rows)
        {
            w.WriteStartObject();
            w.WriteString("candidate", row.Id);
            w.WriteBoolean("qualified", row.Qualified);
            w.WriteNumber("train_trades", row.TrainTrades);
            Number(w, "train_mean_r", row.TrainMeanR);
            Number(w, "train_score", row.TrainScore);
            w.WriteNumber("test_trades", row.TestTrades);
            Number(w, "test_score", row.TestScore);
            w.WriteEndObject();
        }

        w.WriteEndArray();
        w.WriteEndObject();
    }

    private static void Date(Utf8JsonWriter w, string name, DateOnly? date) => w.WriteString(name, date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

    private static void Number(Utf8JsonWriter w, string name, double? value)
    {
        if (value is not { } number)
        {
            w.WriteNull(name);
            return;
        }

        if (!double.IsFinite(number))
        {
            throw new ArgumentException("A NaN or an infinity cannot be written.", nameof(value));
        }

        var text = number.ToString("0.000000", CultureInfo.InvariantCulture);
        w.WritePropertyName(name);
        w.WriteRawValue(text == "-0.000000" ? "0.000000" : text);
    }
}
