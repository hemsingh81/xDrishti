using System.Globalization;
using System.Text.Json;
using XDrishti.Domain.Common;

namespace XDrishti.Domain.Learning;

/// <summary>
/// Reads the canonical fill-rules JSON of two stored runs (<c>FillRules.ToCanonicalJson</c>) and says how their slippage compares (P6-11, ADR 0072). Only the <c>slippage.*</c> fields may differ: the entry limit and the gap rule must be equal. The
/// direction comes from the stored rules; the values typed on the command line are only a label. Pure.
/// </summary>
public static class FillCostComparison
{
    /// <summary>The code of every failure here.</summary>
    public const string Code = "experiments.assumptions_differ";

    private static readonly string[] Components = ["ticks", "bar_range_frac", "stop_exit_extra_ticks"];

    /// <summary>The direction of the candidate's slippage against the baseline's, or a conflict when the rules cannot be read, differ in more than the slippage, or do not differ at all.</summary>
    public static Result<FillCostChange> Compare(string baselineFills, string candidateFills)
    {
        if (!TryRead(baselineFills, out var a, out var aGap, out var aEntry) || !TryRead(candidateFills, out var b, out var bGap, out var bEntry))
        {
            return Fail("the fill rules of a run cannot be read, so the slippage cannot be compared");
        }

        if (aEntry != bEntry || !string.Equals(aGap, bGap, StringComparison.Ordinal))
        {
            return Fail("more than the slippage (the entry limit or the gap rule)");
        }

        var lower = 0;
        var higher = 0;
        for (var i = 0; i < Components.Length; i++)
        {
            lower += b[i] < a[i] ? 1 : 0;
            higher += b[i] > a[i] ? 1 : 0;
        }

        if (lower == 0 && higher == 0)
        {
            return Fail("nothing: the two runs have the same slippage, so the knob did not change");
        }

        return Result.Success(lower == 0 ? FillCostChange.Dearer : higher == 0 ? FillCostChange.Cheaper : FillCostChange.Mixed);
    }

    private static Result<FillCostChange> Fail(string what) =>
        Result.Failure<FillCostChange>(Error.Conflict(Code, string.Create(CultureInfo.InvariantCulture, $"The two runs differ in {what}; an experiment changes one knob, and a changed cost assumption could win unseen.")));

    private static bool TryRead(string json, out double[] components, out string gap, out double entry)
    {
        components = new double[Components.Length];
        gap = string.Empty;
        entry = 0;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("entry_limit_percent", out var e) || !e.TryGetDouble(out entry)
                || !root.TryGetProperty("slippage", out var s) || s.ValueKind != JsonValueKind.Object
                || !s.TryGetProperty("gap_fill", out var g) || g.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            gap = g.GetString()!;
            for (var i = 0; i < Components.Length; i++)
            {
                if (!s.TryGetProperty(Components[i], out var c) || !c.TryGetDouble(out components[i]) || !double.IsFinite(components[i]))
                {
                    return false;
                }
            }

            return double.IsFinite(entry);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return false;
        }
    }
}
