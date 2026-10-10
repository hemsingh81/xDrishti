using System.Globalization;
using System.Text;

namespace XDrishti.LlmBench;

internal sealed record ThresholdCheck(string Measure, string Target, string Actual, bool? Pass);

/// <summary>The decision thresholds from the P0-15 spec (owner may change them).</summary>
internal sealed record Thresholds(double MinGenTpsAt8K = 15, double MaxBriefingTtftSeconds = 15, double MaxBriefingTotalSeconds = 45, double MaxLoadSeconds = 60)
{
    public IReadOnlyList<ThresholdCheck> Evaluate(BenchResults r)
    {
        var inv = CultureInfo.InvariantCulture;
        var at8k = r.Throughput.Where(t => t.ContextTokens is >= 6000 and <= 12000).OrderBy(t => t.ContextTokens).FirstOrDefault();
        var tools = r.Smoke.FirstOrDefault(s => s.Name == "tool calls valid");
        return
        [
            new("Generation speed at ~8K context", $"≥ {MinGenTpsAt8K:F0} tok/s", at8k is null ? "not measured" : $"{at8k.MedianGenTps.ToString("F1", inv)} tok/s", at8k is null ? null : at8k.MedianGenTps >= MinGenTpsAt8K),
            new("Time-to-first-token, ~4K prompt", $"≤ {MaxBriefingTtftSeconds:F0} s", r.Briefing is null ? "not measured" : $"{(r.Briefing.MedianTtftMs / 1000).ToString("F1", inv)} s", r.Briefing is null ? null : r.Briefing.MedianTtftMs / 1000 <= MaxBriefingTtftSeconds),
            new("300-word briefing, end to end", $"≤ {MaxBriefingTotalSeconds:F0} s", r.Briefing is null ? "not measured" : $"{(r.Briefing.MedianTotalMs / 1000).ToString("F1", inv)} s", r.Briefing is null ? null : r.Briefing.MedianTotalMs / 1000 <= MaxBriefingTotalSeconds),
            new("Load from warm disk", $"≤ {MaxLoadSeconds:F0} s", r.LoadMs is null ? "not measured" : $"{(r.LoadMs.Value / 1000).ToString("F1", inv)} s", r.LoadMs is null ? null : r.LoadMs.Value / 1000 <= MaxLoadSeconds),
            new("Tool-call validity", "≥ 9 / 10", tools?.Detail ?? "not measured", tools?.Passed),
        ];
    }
}

internal static class ReportBuilder
{
    public static string Render(BenchResults r, Thresholds thresholds)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine(inv, $"# LLM benchmark — {r.Label}").AppendLine();
        sb.AppendLine(inv, $"Model `{r.Model}` · {r.At:yyyy-MM-dd HH:mm zzz}").AppendLine();
        sb.AppendLine("## Throughput").AppendLine();
        sb.AppendLine("| Context (target) | Prompt tokens | Prompt tok/s | Generation tok/s | TTFT s | Total s | Runs |").AppendLine("|---|---|---|---|---|---|---|");
        foreach (var t in r.Throughput.OrderBy(t => t.ContextTokens).Concat(r.Briefing is null ? [] : [r.Briefing]))
        {
            var label = ReferenceEquals(t, r.Briefing) ? "briefing (~4K → 300 words)" : t.ContextTokens.ToString("N0", inv);
            sb.AppendLine(inv, $"| {label} | {t.ActualPromptTokens} | {t.MedianPromptTps:F1} | {t.MedianGenTps:F1} | {t.MedianTtftMs / 1000:F1} | {t.MedianTotalMs / 1000:F1} | {string.Join(" / ", t.Runs.Select(x => x.GenTps.ToString("F1", inv)))} tok/s |");
        }

        sb.AppendLine().AppendLine("## Capability smoke").AppendLine().AppendLine("| Check | Result | Detail |").AppendLine("|---|---|---|");
        foreach (var s in r.Smoke)
        {
            sb.AppendLine(inv, $"| {s.Name} | {(s.Passed ? "pass" : "**fail**")} | {s.Detail} |");
        }

        sb.AppendLine().AppendLine("## Thresholds").AppendLine().AppendLine("| Measure | Target | Actual | |").AppendLine("|---|---|---|---|");
        foreach (var c in thresholds.Evaluate(r))
        {
            sb.AppendLine(inv, $"| {c.Measure} | {c.Target} | {c.Actual} | {(c.Pass is null ? "—" : c.Pass.Value ? "pass" : "**fail**")} |");
        }

        return sb.ToString();
    }
}
