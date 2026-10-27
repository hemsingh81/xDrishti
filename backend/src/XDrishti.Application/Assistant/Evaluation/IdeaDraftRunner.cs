using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Ai.Drafting;
using XDrishti.Domain.Assistant.Drafting;
using XDrishti.Domain.Assistant.Evaluation;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Assistant.Evaluation;

/// <summary>
/// Asks the model for a strategy draft of an idea (spec P9-09 AC 6) through the very parts of the draft job of P9-07: the prompt and the language sheet, the model port, the Lab validator and the four checks, at most <c>MaxRepairs</c> repair turns. It stores no draft and never reaches
/// the confirm, the auto option or the decision writer: the text is only graded. <see cref="IdeaRun.FirstShot"/> is a valid (or rightly declined) answer before any repair.
/// </summary>
internal sealed class IdeaDraftRunner(IDraftSheetSource sheet, IStrategyDrafter drafter, IQueryHandler<SubmitDraftQuery, DraftEvaluation> submit, IStrategyTextReader reader, DraftOptions options)
{
    private const int MaxOutput = 1800;

    public async ValueTask<Result<IdeaRun>> RunAsync(IdeaCase idea, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(idea);
        var system = DraftPrompt.System(sheet.Sheet);
        var user = DraftPrompt.First(idea.Idea);
        DraftAnswer? answer = null;
        DraftEvaluation? last = null;
        var shapeOk = true;
        var firstShot = false;
        var latency = 0;
        int? prompt = null;
        int? completion = null;
        var calls = 0;
        for (var n = 1; n <= 1 + options.MaxRepairs; n++)
        {
            DraftModelAnswer given;
            try
            {
                given = await drafter.DraftAsync(new DraftModelRequest(system, user, MaxOutput, options.Seed), cancellationToken);
            }
            catch (ModelUnavailableException)
            {
                return Result.Failure<IdeaRun>(EvalErrors.ModelUnavailable());
            }

            calls++;
            latency += given.LatencyMs;
            prompt = (prompt ?? 0) + (given.PromptTokens ?? 0);
            completion = (completion ?? 0) + (given.CompletionTokens ?? 0);
            var read = given.FinishReason == "length" ? Result.Failure<DraftAnswer>(Error.Validation(DraftCodes.AnswerShape, "The answer was cut.")) : DraftAnswerReader.Read(given.Text);
            shapeOk = read.IsSuccess;
            if (read.IsFailure)
            {
                user = DraftPrompt.Repair(idea.Idea, null, [], [new DraftFinding(DraftCodes.AnswerShape, string.Empty)]);
                continue;
            }

            answer = read.Value;
            last = await submit.HandleAsync(new SubmitDraftQuery(idea.Idea, answer), cancellationToken);
            if (n == 1)
            {
                firstShot = !last.NeedsRepair && DraftGrader.Accepted(idea, new IdeaOutcome(last.Status, last.Yaml, null, answer));
            }

            if (!last.NeedsRepair)
            {
                break;
            }

            user = DraftPrompt.Repair(idea.Idea, last.Yaml, last.Problems, last.Findings);
        }

        var outcome = !shapeOk || last is null
            ? new IdeaOutcome(DraftStatus.Failed, null, null, answer)
            : new IdeaOutcome(last.Status, last.Yaml, last.Yaml is null ? null : reader.ToJson(last.Yaml), answer);
        return Result.Success(new IdeaRun(outcome, firstShot, calls, latency, prompt, completion));
    }
}

/// <summary>What the draft job made of one idea: the outcome, whether the first answer needed no repair, and what the calls cost.</summary>
internal sealed record IdeaRun(IdeaOutcome Outcome, bool FirstShot, int Calls, int LatencyMs, int? PromptTokens, int? CompletionTokens);
