using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Ai;
using XDrishti.Application.Ai.Briefing;
using XDrishti.Domain.Assistant.Evaluation;
using XDrishti.Domain.Assistant.Text;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Assistant.Evaluation;

/// <summary>
/// Asks the model to phrase a plan (spec P9-09 AC 5) with the prompt and the guard of P9-06, switch forced on: attempt one, and when the guard refuses it a second attempt that carries the findings. One run of at most two calls therefore measures the raw and the repaired reading.
/// It stores no briefing and applies nothing; the model is asked even for a plan with no ticket (the pipeline would write the fixed text there, but the question is what the model writes).
/// </summary>
internal sealed class PlanTextRunner(IBriefingWriter writer, BriefingOptions options)
{
    public const int MaxAttempts = 2;

    private const int MaxOutputDaily = 700;

    public async ValueTask<Result<PlanRun>> RunAsync(PlanCase plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var maxWords = options.DailyMaxWords;
        var texts = new List<string>();
        IReadOnlyList<GuardFinding>? refused = null;
        var latency = 0;
        int? prompt = null;
        int? completion = null;
        for (var n = 1; n <= MaxAttempts; n++)
        {
            BriefingWrite written;
            try
            {
                written = await writer.WriteAsync(new BriefingRequest(PromptRegistry.Briefing.Text, BriefingPrompt.User(plan.Facts, maxWords, refused), MaxOutputDaily, options.Seed), cancellationToken);
            }
            catch (ModelUnavailableException)
            {
                return Result.Failure<PlanRun>(EvalErrors.ModelUnavailable());
            }

            var text = written.Text.Trim();
            texts.Add(text);
            latency += written.LatencyMs;
            prompt = (prompt ?? 0) + (written.PromptTokens ?? 0);
            completion = (completion ?? 0) + (written.CompletionTokens ?? 0);
            var findings = TextGuard.Check(text, plan.Facts, maxWords).Findings.ToList();
            if (written.FinishReason == "length")
            {
                findings.Add(new GuardFinding(GuardCodes.FormTooLong, "cut", text.Length));
            }

            if (findings.Count == 0)
            {
                break;
            }

            refused = findings;
        }

        return Result.Success(new PlanRun(texts, latency, prompt, completion, maxWords));
    }
}

/// <summary>What the model wrote for one plan: the texts of the attempts (one, or two when the first was refused) and what they cost.</summary>
internal sealed record PlanRun(IReadOnlyList<string> Texts, int LatencyMs, int? PromptTokens, int? CompletionTokens, int MaxWords);
