using System.Globalization;
using System.Text;
using System.Text.Json;

namespace XDrishti.Application.Assistant.Evaluation;

/// <summary>
/// The three files of a run's report folder (spec P9-09 AC 14), as text: <c>report.json</c>, <c>report.md</c> and <c>answers.jsonl</c>. Built from the stored run, so that any run can be reported again. They hold fixture data and numbers only: the answers are the assistant's
/// answers to fixture questions. Every rate is printed with its Wilson interval and the small-sample sentence.
/// </summary>
public static class EvalReports
{
    private static readonly JsonSerializerOptions Pretty = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string Json(EvalDetailView detail)
    {
        ArgumentNullException.ThrowIfNull(detail);
        return JsonSerializer.Serialize(new { run = detail.Run, parameters = ParseOrText(detail.Parameters), prompts = ParseOrText(detail.PromptVersions) }, Pretty);
    }

    public static string Answers(EvalDetailView detail)
    {
        ArgumentNullException.ThrowIfNull(detail);
        var builder = new StringBuilder();
        foreach (var c in detail.Cases)
        {
            builder.Append(JsonSerializer.Serialize(new { set = c.Set, case_id = c.CaseId, repeat = c.Repeat, passed = c.Passed, grounded = c.Grounded, session_id = c.SessionId, answer = c.Answer })).Append('\n');
        }

        return builder.ToString();
    }

    public static string Markdown(EvalDetailView detail)
    {
        ArgumentNullException.ThrowIfNull(detail);
        var run = detail.Run;
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"# Eval run {run.Id}\n\n");
        text.Append(CultureInfo.InvariantCulture, $"- Verdict: **{run.Verdict}**{(run.Gating ? string.Empty : " (non-gating: it can never be accepted)")}\n");
        text.Append(CultureInfo.InvariantCulture, $"- Status: {run.Status}; target {run.Target}; profile {run.Profile}; repeats {run.Repeats}; sets {string.Join(", ", run.Sets)}\n");
        text.Append(CultureInfo.InvariantCulture, $"- Model {run.ModelId} ({Short(run.ModelSha256)}); runtime {Short(run.RuntimeDigest)}; fingerprint {Short(run.Fingerprint)}\n");
        text.Append(CultureInfo.InvariantCulture, $"- Started {run.StartedAt:u}, finished {run.FinishedAt:u}; accepted as baseline: {(run.IsBaseline ? "yes" : "no")}\n\n");
        text.Append(CultureInfo.InvariantCulture, $"> {run.Summary.SmallSampleLine}\n\n");
        foreach (var set in run.Summary.Sets)
        {
            text.Append(CultureInfo.InvariantCulture, $"## {set.Set}: {set.Outcome.State}\n\n");
            foreach (var reason in set.Outcome.Reasons)
            {
                text.Append(CultureInfo.InvariantCulture, $"- {reason}\n");
            }

            text.Append("\n| rate | k of n | share | 95 % interval |\n|---|---|---|---|\n");
            foreach (var rate in set.Rates)
            {
                var share = rate.N == 0 ? "n/a" : ((double)rate.K / rate.N).ToString("0.000", CultureInfo.InvariantCulture);
                text.Append(CultureInfo.InvariantCulture, $"| {rate.Name} | {rate.K} of {rate.N} | {share} | {rate.Low.ToString("0.000", CultureInfo.InvariantCulture)} to {rate.High.ToString("0.000", CultureInfo.InvariantCulture)} |\n");
            }

            if (set.Stability is { } stability)
            {
                text.Append(CultureInfo.InvariantCulture, $"\nStability (cases whose repeats gave the same answer): {stability.ToString("0.000", CultureInfo.InvariantCulture)}\n");
            }

            if (set.IntentMean is { } mean)
            {
                text.Append(CultureInfo.InvariantCulture, $"\nIntent: mean {mean.ToString("0.000", CultureInfo.InvariantCulture)}, lowest idea {(set.IntentLowest ?? 0).ToString("0.000", CultureInfo.InvariantCulture)}\n");
            }

            text.Append('\n');
        }

        text.Append("## Failed cases\n\n");
        var failed = detail.Cases.Where(c => !c.Passed).ToList();
        text.Append(failed.Count == 0 ? "None.\n" : string.Join('\n', failed.Select(c => $"- {c.Set} {c.CaseId} repeat {c.Repeat}: {c.Detail}")) + "\n");
        return text.ToString();
    }

    private static string Short(string hash) => hash.Length > 12 ? hash[..12] : hash.Length == 0 ? "n/a" : hash;

    private static object ParseOrText(string json)
    {
        try
        {
            return JsonDocument.Parse(json).RootElement.Clone();
        }
        catch (JsonException)
        {
            return json;
        }
    }
}
