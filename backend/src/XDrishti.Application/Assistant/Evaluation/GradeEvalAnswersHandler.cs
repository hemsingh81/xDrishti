using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Assistant.Evaluation;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Assistant.Evaluation;

/// <summary>
/// Grades answers another assistant gave, from a file (spec P9-09 Contract, <c>xd eval grade</c>; the Hermes PoC of P9-10). No model is called and nothing is asked: each answer is graded by the same code as a built-in run. Grounding comes from the tool-call audit of the answer's
/// <c>session_id</c> (the conversation); a missing audit makes grounding not evaluable and the answer cannot pass. The run is stored with target <c>hermes</c> (or the named target), is never gating and so can never be accepted.
/// </summary>
internal sealed class GradeEvalAnswersHandler(IEvalFiles files, IConversationReader conversations, IEvalStore store, IPromptVersions prompts, TimeProvider time, ILogger<GradeEvalAnswersHandler> logger)
    : ICommandHandler<GradeEvalAnswersCommand, EvalRunView>
{
    public async ValueTask<Result<EvalRunView>> HandleAsync(GradeEvalAnswersCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.Target) || command.Target == RunEvalCommand.Builtin || command.Target.Length > 16)
        {
            return Result.Failure<EvalRunView>(EvalErrors.Invalid("Name the assistant that gave the answers with --target (for example hermes); builtin runs are made with xd eval run."));
        }

        var read = await files.ReadAsync(cancellationToken);
        if (read.IsFailure)
        {
            return Result.Failure<EvalRunView>(read.Error);
        }

        var world = EvalWorld.Load(read.Value);
        if (world.IsFailure)
        {
            return Result.Failure<EvalRunView>(world.Error);
        }

        var parsed = ParseAnswers(command.AnswersJsonl);
        if (parsed.IsFailure)
        {
            return Result.Failure<EvalRunView>(parsed.Error);
        }

        var clock = Stopwatch.StartNew();
        var started = time.GetUtcNow();
        var rows = new List<CaseRepeatResult>();
        var cases = new List<NewEvalCase>();
        foreach (var given in parsed.Value)
        {
            var evalCase = world.Value.Data.FirstOrDefault(c => c.Id == given.CaseId);
            if (given.Set != "data" || evalCase is null)
            {
                return Result.Failure<EvalRunView>(EvalErrors.Invalid($"Only data questions are graded from a file; '{given.Set}' '{given.CaseId}' is not one."));
            }

            var results = given.SessionId is { } session ? await ToolResultsAsync(session, cancellationToken) : null;
            var grade = AnswerGrader.Grade(evalCase, given.Answer, results);
            var flags = new Dictionary<string, bool> { ["hard_fail"] = grade.HardFail };
            rows.Add(new CaseRepeatResult("data", evalCase.Id, given.Repeat, grade.Passed, grade.Grounded, null, flags, EvalText.Key(given.Answer)));
            cases.Add(new NewEvalCase("data", evalCase.Id, given.Repeat, grade.Passed, grade.Grounded, null, EvalJson.Detail(grade, evalCase.ToolsExpected), given.Answer, given.SessionId, results?.Count ?? 0, given.LatencyMs, null, null));
        }

        var repeats = rows.Count == 0 ? 1 : rows.Max(r => r.Repeat);
        var summary = EvalSummarizer.Build(rows, world.Value.Thresholds, repeats, null);
        var inputs = new FingerprintInputs(command.Target, command.Target, prompts.All, world.Value.SetsHash, world.Value.ThresholdsHash, world.Value.FixtureHash, command.Target);
        var runId = Guid.CreateVersion7(time.GetUtcNow());
        var run = new NewEvalRun(
            runId, started, time.GetUtcNow(), command.Target, command.Target, ["data"], repeats, false, command.Target, string.Empty, string.Empty, JsonSerializer.Serialize(new { graded = "from a file", repeats }),
            EvalPromptJson.Write(prompts.All), world.Value.SetsHash, world.Value.ThresholdsHash, world.Value.FixtureHash, EvalFingerprint.Of(inputs), "completed", summary.Verdict, EvalJson.Serialize(summary), command.Actor);
        await store.InsertRunAsync(run, cases, CancellationToken.None);
        var passed = rows.Count(r => r.Passed);
        EvalLog.Graded(logger, runId, rows.Count, passed);
        EvalLog.Finished(logger, runId, "completed", summary.Verdict, false, clock.ElapsedMilliseconds);
        return Result.Success(EvalMapping.Run(run, summary, false));
    }

    /// <summary>The result texts of the tool calls of a conversation; null when the audit holds none (grounding is then not evaluable).</summary>
    private async ValueTask<IReadOnlyList<string>?> ToolResultsAsync(Guid session, CancellationToken cancellationToken)
    {
        var detail = await conversations.GetAsync(session, cancellationToken);
        if (detail is null)
        {
            return null;
        }

        var texts = new List<string>();
        foreach (var call in detail.Messages.SelectMany(m => m.ToolCalls))
        {
            var stored = await conversations.GetToolCallAsync(call.Id, cancellationToken);
            if (stored?.Result is { } result)
            {
                texts.Add(result.GetRawText());
            }
        }

        return texts.Count == 0 ? null : texts;
    }

    private static Result<List<GivenAnswer>> ParseAnswers(string jsonl)
    {
        var list = new List<GivenAnswer>();
        var number = 0;
        foreach (var line in jsonl.Split('\n'))
        {
            number++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var answer = root.GetProperty("answer").GetString();
                var repeat = root.TryGetProperty("repeat", out var r) ? r.GetInt32() : 1;
                var session = root.TryGetProperty("session_id", out var s) && s.ValueKind == JsonValueKind.String && Guid.TryParse(s.GetString(), out var g) ? g : (Guid?)null;
                var latency = root.TryGetProperty("started_at", out var a) && root.TryGetProperty("finished_at", out var b) && DateTimeOffset.TryParse(a.GetString(), out var from) && DateTimeOffset.TryParse(b.GetString(), out var to)
                    ? (int)Math.Clamp((to - from).TotalMilliseconds, 0, int.MaxValue)
                    : 0;
                if (answer is null || repeat is < 1 or > RunEvalCommand.MaxRepeats)
                {
                    return Result.Failure<List<GivenAnswer>>(EvalErrors.Invalid($"line {number} is not an answer."));
                }

                list.Add(new GivenAnswer(root.GetProperty("set").GetString() ?? string.Empty, root.GetProperty("case_id").GetString() ?? string.Empty, repeat, answer, session, latency));
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
            {
                return Result.Failure<List<GivenAnswer>>(EvalErrors.Invalid($"line {number} is not an answer."));
            }
        }

        return list.Count == 0 ? Result.Failure<List<GivenAnswer>>(EvalErrors.Invalid("The answers file holds no answer.")) : Result.Success(list);
    }

    private sealed record GivenAnswer(string Set, string CaseId, int Repeat, string Answer, Guid? SessionId, int LatencyMs);
}
