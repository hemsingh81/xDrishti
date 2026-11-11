using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Assistant;

namespace XDrishti.Application.Ai;

/// <summary>
/// Cuts the BK-07 views down for the model (spec BK-12 AC 2, 3, 4, 7): the same fields as the screen's view, a figure only as <c>{state, number, reason}</c> (no number for a figure that is not a value), the series down-sampled (<see cref="SeriesSampler"/>, first, last and extremes kept),
/// long member lists cut at 50 with their total, no rank when the ranking is not separated. It computes no figure and adds none: every number in the answer is a number of the view. Pure.
/// </summary>
internal static class BasketReportShaper
{
    public const int MaxListRows = 50;

    /// <summary>The characters the data object may take, so the envelope (evidence, notes) stays within <see cref="ToolLimits.MaxModelChars"/>.</summary>
    public const int DataBudgetChars = 5000;

    private const int MinPoints = 8;

    private static readonly string[] CutLists = ["excluded", "shortMembers", "noBar"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public static AiToolData Profile(BasketProfileView view, int maxPoints = SeriesSampler.MaxPoints)
    {
        ArgumentNullException.ThrowIfNull(view);
        // the envelope holds 6,000 characters: the series are cut to at most 120 points and the lists to at most 50 rows, and when the answer is still too long both are cut further, in steps, never past 8 points and 10 rows
        (int Points, int Rows)[] steps = [(maxPoints, MaxListRows), (Math.Min(maxPoints, 60), MaxListRows), (Math.Min(maxPoints, 30), MaxListRows), (Math.Min(maxPoints, 16), 25), (MinPoints, 10)];
        JsonObject data = [];
        foreach (var (points, rows) in steps)
        {
            data = ProfileData(view, points, rows);
            if (data.ToJsonString().Length <= DataBudgetChars)
            {
                break;
            }
        }

        return AiToolData.OfData(data, Evidence(view.Evidence, $"basket profile ({view.Basket.Kind} basket), price returns of its members"), ProfileSummary(view));
    }

    public static AiToolData Ranking(BasketRankingView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var data = JsonSerializer.SerializeToNode(view, Json)!.AsObject();
        data.Remove("evidence");
        var notes = new List<string> { "figures are price returns of the baskets' own members against the index, not trading results" };
        if (!view.Separated)
        {
            // BK-07: when a gap is within its noise the order is not shown, so the model has no rank to quote
            data.Remove("rankWindow");
            foreach (var row in data["rows"]!.AsArray())
            {
                row!.AsObject().Remove("rsRank");
            }

            notes.Insert(0, "not separated: the gaps between baskets are within their noise, so no rank is given; quote the returns and n, not an order");
        }

        foreach (var row in view.Rows.Where(r => r.RsRank.State != "value" && view.Separated))
        {
            notes.Add($"a basket has no rank: {row.RsRank.State}" + (row.RsRank.Reason is { } r ? $" ({r})" : string.Empty));
        }

        return AiToolData.OfData(data, Evidence(view.Evidence, "baskets side by side on relative strength"), notes);
    }

    private static JsonObject ProfileData(BasketProfileView view, int maxPoints, int maxRows)
    {
        var data = JsonSerializer.SerializeToNode(view, Json)!.AsObject();
        data.Remove("evidence");
        var s = view.Series;
        IReadOnlyList<double?> basket = [.. s.Basket.Select(v => (double?)v)];
        IReadOnlyList<double?> drawdown = [.. s.Drawdown.Select(v => (double?)v)];
        var sample = SeriesSampler.Sample(s.Dates.Count, maxPoints, basket, s.RelativeStrength, drawdown);
        data["series"] = new JsonObject
        {
            ["sampled"] = sample.Sampled,
            ["step"] = sample.Step,
            ["points"] = sample.Indices.Count,
            ["total"] = s.Dates.Count,
            ["dates"] = new JsonArray([.. sample.Indices.Select(i => (JsonNode?)JsonValue.Create(s.Dates[i].ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)))]),
            ["basket"] = Pick(sample, basket),
            ["relativeStrength"] = Pick(sample, s.RelativeStrength),
            ["drawdown"] = Pick(sample, drawdown),
        };

        // the rest of the screen's series and the correlation matrix are on the screen; the model gets the figures behind them
        data["breadth"]!.AsObject().Remove("seriesDates");
        data["breadth"]!.AsObject().Remove("seriesB20");
        data["correlation"]!.AsObject().Remove("matrix");
        data["contribution"]!.AsObject().Remove("rows"); // contributions: only the top and bottom 5 (AC 7)
        Cut(data, maxRows);
        return data;
    }

    private static JsonArray Pick(SeriesSample sample, IReadOnlyList<double?> values) =>
        new([.. sample.Indices.Select(i => (JsonNode?)JsonValue.Create(values[i]))]);

    private static void Cut(JsonNode? node, int maxRows)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, child) in obj.ToList())
                {
                    if (child is JsonArray list && CutLists.Contains(name, StringComparer.Ordinal) && list.Count > maxRows)
                    {
                        var total = list.Count;
                        obj[name] = new JsonArray([.. list.Take(maxRows).Select(n => n?.DeepClone())]);
                        obj[name + "Truncated"] = true;
                        obj[name + "Total"] = total;
                    }
                    else
                    {
                        Cut(child, maxRows);
                    }
                }

                break;
            case JsonArray array:
                foreach (var child in array)
                {
                    Cut(child, maxRows);
                }

                break;
        }
    }

    private static ToolEvidence Evidence(BenchmarkEvidenceView e, string basis)
    {
        var range = $"{e.Range.From:yyyy-MM-dd}..{e.Range.To:yyyy-MM-dd}";
        return new ToolEvidence(basis + "; " + e.Basis, [], range, e.N, e.Limits, [.. e.UnavailableInputs.Select(u => new ToolUnavailable("input", u))]);
    }

    /// <summary>One line for every headline figure that is not a value: the state in words and the engine's own reason (it holds the n and the minimum). No figure is written for it.</summary>
    private static List<string> ProfileSummary(BasketProfileView v)
    {
        var notes = new List<string> { "figures are price returns of the basket's own members, not trading results" };
        void Add(string label, MetricView m)
        {
            if (m.State == "value")
            {
                return;
            }

            var words = m.State == "insufficient" ? "not enough data" : m.State.Replace('_', ' ');
            notes.Add($"{label}: {words}" + (m.Reason is { } r ? $" ({r})" : string.Empty));
        }

        Add("breadth above 20-day average", v.Breadth.B20.Share);
        Add("breadth above 50-day average", v.Breadth.B50.Share);
        Add("breadth above 200-day average", v.Breadth.B200.Share);
        Add("advancing share", v.Breadth.AdShare.Share);
        Add("new-high share", v.Breadth.NewHighShare.Share);
        Add("dispersion", v.Dispersion.Today);
        Add("average correlation", v.Correlation.RhoBar);
        Add("hold return", v.Hold);
        return notes;
    }
}
