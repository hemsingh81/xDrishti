using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Ai;
using XDrishti.Domain.Assistant.Evaluation;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Assistant.Evaluation;

/// <summary>
/// Runs the eval once (spec P9-09 AC 8, 13, ADR 0143). It is the owner's act and nothing else starts it. Before any model call it reads the eval files, checks that the master switch is on (it never sets it), that the model is ready, that the nightly run is not going and that the
/// database behind the eval tool endpoint is the fixture database (the marker). It then asks each case <c>repeats</c> times as <c>Origin: eval</c>, grades by code, and stores the run with its cases in one insert-only write. It reads only the fixture; the only rows written
/// outside the eval tables are the assistant's own audit rows of the eval conversations (fixture data). A model that goes down stops the run: the partial run is stored as <c>model_unavailable</c> and is never a pass.
/// </summary>
internal sealed class RunEvalHandler(
    IEvalFiles files,
    IEvalFixtureProbe probe,
    IAssistantSettingsStore settingsStore,
    IEvalEnvironment environment,
    IPromptVersions prompts,
    IEvalAnswerSource answers,
    PlanTextRunner plansRunner,
    IdeaDraftRunner ideasRunner,
    IEvalStore store,
    INightlyRunProbe nightly,
    TurnOptions turn,
    TurnGate gate,
    TimeProvider time,
    ILogger<RunEvalHandler> logger) : ICommandHandler<RunEvalCommand, EvalRunView>
{
    private static readonly string[] AllSets = ["data", "plans", "ideas", "events"];

    public async ValueTask<Result<EvalRunView>> HandleAsync(RunEvalCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var sets = ResolveSets(command.Sets);
        if (sets.IsFailure || command.Repeats is < 1 or > RunEvalCommand.MaxRepeats || command.Target != RunEvalCommand.Builtin || string.IsNullOrWhiteSpace(command.Actor))
        {
            return Refused<EvalRunView>(EvalErrors.Invalid("Sets are data, plans, ideas, events or all; repeats 1 to 5; target builtin."));
        }

        var read = await files.ReadAsync(cancellationToken);
        if (read.IsFailure)
        {
            return Refused<EvalRunView>(read.Error);
        }

        var world = EvalWorld.Load(read.Value);
        if (world.IsFailure)
        {
            return Refused<EvalRunView>(world.Error);
        }

        // Every refusal below comes before the first model call.
        var settings = await settingsStore.CurrentAsync(cancellationToken);
        if (!settings.Enabled)
        {
            return Refused<EvalRunView>(EvalErrors.AssistantDisabled());
        }

        var marker = await probe.ReadFixtureHashAsync(cancellationToken);
        if (marker is null || !string.Equals(marker, world.Value.FixtureHash, StringComparison.Ordinal))
        {
            return Refused<EvalRunView>(EvalErrors.WrongDatabase());
        }

        var model = await environment.ModelAsync(cancellationToken);
        if (!model.Known)
        {
            return Refused<EvalRunView>(EvalErrors.ModelUnavailable());
        }

        if (await nightly.IsRunningAsync(TimeSpan.FromMinutes(turn.NightlyGuardMinutes), cancellationToken))
        {
            return Refused<EvalRunView>(EvalErrors.NightlyRunning());
        }

        var runId = Guid.CreateVersion7(time.GetUtcNow());
        var actor = EvalErrors.RunActor(runId);
        var started = time.GetUtcNow();
        var clock = Stopwatch.StartNew();
        var w = world.Value;
        var rows = new List<CaseRepeatResult>();
        var cases = new List<NewEvalCase>();
        var total = (sets.Value.Contains("data") ? w.Data.Count : 0) + (sets.Value.Contains("plans") ? w.Plans.Count : 0) + (sets.Value.Contains("ideas") ? w.Ideas.Count : 0);
        var setNames = string.Join(',', sets.Value);
        EvalLog.Started(logger, runId, setNames, command.Repeats, total);

        var status = "completed";
        Error? stop = null;
        try
        {
            foreach (var set in sets.Value.Where(s => s != "events"))
            {
                stop = set switch
                {
                    "data" => await RunDataAsync(w, command.Repeats, actor, rows, cases, cancellationToken),
                    "plans" => await RunPlansAsync(w, command.Repeats, rows, cases, cancellationToken),
                    _ => await RunIdeasAsync(w, command.Repeats, rows, cases, cancellationToken),
                };
                if (stop is not null)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            status = "cancelled";
        }

        if (stop is not null)
        {
            status = stop.Code == EvalErrors.ModelUnavailableCode ? "model_unavailable" : stop.Code.Replace("eval.", string.Empty, StringComparison.Ordinal);
        }

        if (rows.Count == 0)
        {
            return stop is null ? Refused<EvalRunView>(EvalErrors.Invalid("Nothing was run.")) : Refused<EvalRunView>(stop);
        }

        var events = sets.Value.Contains("events") ? AnnouncementGrader.Grade([], w.Thresholds) : null;
        var summary = EvalSummarizer.Build(rows, w.Thresholds, command.Repeats, events);
        if (status != "completed")
        {
            summary = summary with { Verdict = EvalVerdict.Incomplete };
        }

        var gating = command.Repeats >= 2 && status == "completed";
        var profile = model.Id.Contains("gpt-oss", StringComparison.OrdinalIgnoreCase) ? "gptoss" : "main";
        var inputs = new FingerprintInputs(model.Sha256, model.RuntimeDigest, prompts.All, w.SetsHash, w.ThresholdsHash, w.FixtureHash, profile);
        var parameters = JsonSerializer.Serialize(new { temperature = 0, seed = turn.Seed ?? 1337, repeats = command.Repeats, maxAnswerTokens = turn.MaxAnswerTokens, thinking = "the model server's setting", cachePrompt = "the server default; the client does not set it", requestsAtATime = 1 });
        var run = new NewEvalRun(
            runId, started, time.GetUtcNow(), command.Target, profile, sets.Value, command.Repeats, gating, model.Id, model.Sha256, model.RuntimeDigest, parameters, EvalPromptJson.Write(prompts.All),
            w.SetsHash, w.ThresholdsHash, w.FixtureHash, EvalFingerprint.Of(inputs), status, summary.Verdict, EvalJson.Serialize(summary), command.Actor);
        await store.InsertRunAsync(run, cases, CancellationToken.None);
        EvalLog.Finished(logger, runId, status, summary.Verdict, gating, clock.ElapsedMilliseconds);
        return Result.Success(EvalMapping.Run(run, summary, false));
    }

    private async ValueTask<Error?> RunDataAsync(EvalWorld w, int repeats, string actor, List<CaseRepeatResult> rows, List<NewEvalCase> cases, CancellationToken cancellationToken)
    {
        foreach (var evalCase in w.Data)
        {
            for (var repeat = 1; repeat <= repeats; repeat++)
            {
                var clock = Stopwatch.StartNew();
                var asked = await answers.AskAsync(evalCase.Question, actor, cancellationToken);
                if (asked.IsFailure)
                {
                    return StopFor(asked.Error);
                }

                var answer = asked.Value;
                var grade = AnswerGrader.Grade(evalCase, answer.Text, answer.ToolResults);
                var flags = new Dictionary<string, bool> { ["hard_fail"] = grade.HardFail };
                rows.Add(new CaseRepeatResult("data", evalCase.Id, repeat, grade.Passed, grade.Grounded, null, flags, EvalText.Key(answer.Text)));
                cases.Add(new NewEvalCase("data", evalCase.Id, repeat, grade.Passed, grade.Grounded, null, EvalJson.Detail(grade, evalCase.ToolsExpected), answer.Text, answer.SessionId, answer.ToolCalls, answer.LatencyMs, answer.PromptTokens, answer.CompletionTokens));
                EvalLog.CaseDone(logger, "data", evalCase.Id, repeat, grade.Passed, (int)clock.ElapsedMilliseconds);
            }
        }

        return null;
    }

    private async ValueTask<Error?> RunPlansAsync(EvalWorld w, int repeats, List<CaseRepeatResult> rows, List<NewEvalCase> cases, CancellationToken cancellationToken)
    {
        foreach (var plan in w.Plans)
        {
            for (var repeat = 1; repeat <= repeats; repeat++)
            {
                using var slot = gate.TryEnter();
                if (slot is null)
                {
                    return EvalErrors.Busy();
                }

                var clock = Stopwatch.StartNew();
                var run = await plansRunner.RunAsync(plan, cancellationToken);
                if (run.IsFailure)
                {
                    return run.Error;
                }

                var grade = BriefingGrader.Grade(plan, run.Value.Texts, run.Value.MaxWords);
                var flags = new Dictionary<string, bool> { ["raw"] = grade.RawPass, ["repaired"] = grade.RepairedPass, ["escape"] = grade.Escape, ["covered"] = grade.Covered };
                var text = run.Value.Texts[^1];
                rows.Add(new CaseRepeatResult("plans", plan.Id, repeat, grade.Passed, null, null, flags, EvalText.Key(text)));
                cases.Add(new NewEvalCase("plans", plan.Id, repeat, grade.Passed, null, null, EvalJson.Detail(grade), text, null, run.Value.Texts.Count, run.Value.LatencyMs, run.Value.PromptTokens, run.Value.CompletionTokens));
                EvalLog.CaseDone(logger, "plans", plan.Id, repeat, grade.Passed, (int)clock.ElapsedMilliseconds);
            }
        }

        return null;
    }

    private async ValueTask<Error?> RunIdeasAsync(EvalWorld w, int repeats, List<CaseRepeatResult> rows, List<NewEvalCase> cases, CancellationToken cancellationToken)
    {
        foreach (var idea in w.Ideas)
        {
            for (var repeat = 1; repeat <= repeats; repeat++)
            {
                using var slot = gate.TryEnter();
                if (slot is null)
                {
                    return EvalErrors.Busy();
                }

                var clock = Stopwatch.StartNew();
                var run = await ideasRunner.RunAsync(idea, cancellationToken);
                if (run.IsFailure)
                {
                    return run.Error;
                }

                var grade = DraftGrader.Grade(idea, run.Value.Outcome);
                var flags = new Dictionary<string, bool>
                {
                    ["first_shot"] = run.Value.FirstShot,
                    ["valid"] = grade.Accepted,
                    ["decline_applicable"] = grade.DeclineCorrect is not null,
                    ["decline_correct"] = grade.DeclineCorrect == true,
                };
                var passed = grade.Accepted && grade.Intent >= 1.0;
                rows.Add(new CaseRepeatResult("ideas", idea.Id, repeat, passed, null, grade.Intent, flags, EvalText.Key(run.Value.Outcome.Yaml ?? run.Value.Outcome.Status)));
                cases.Add(new NewEvalCase("ideas", idea.Id, repeat, passed, null, grade.Intent, EvalJson.Detail(grade, run.Value.FirstShot), run.Value.Outcome.Yaml ?? run.Value.Outcome.Status, null, run.Value.Calls, run.Value.LatencyMs, run.Value.PromptTokens, run.Value.CompletionTokens));
                EvalLog.CaseDone(logger, "ideas", idea.Id, repeat, passed, (int)clock.ElapsedMilliseconds);
            }
        }

        return null;
    }

    /// <summary>The assistant's own refusals, in the eval's words: nothing the model could not do is a pass.</summary>
    private static Error StopFor(Error error) => error.Code switch
    {
        "assistant.model_unavailable" => EvalErrors.ModelUnavailable(),
        "assistant.tools_unavailable" => EvalErrors.ToolsUnavailable(),
        "assistant.disabled" => EvalErrors.AssistantDisabled(),
        "assistant.busy" => EvalErrors.Busy(),
        "assistant.nightly_running" => EvalErrors.NightlyRunning(),
        _ => error,
    };

    private static Result<IReadOnlyList<string>> ResolveSets(IReadOnlyList<string> requested)
    {
        if (requested.Count == 0)
        {
            return Result.Failure<IReadOnlyList<string>>(EvalErrors.Invalid("Name at least one set."));
        }

        var names = requested.Contains("all") ? AllSets : [.. requested];
        return names.All(AllSets.Contains) ? Result.Success<IReadOnlyList<string>>([.. AllSets.Where(names.Contains)]) : Result.Failure<IReadOnlyList<string>>(EvalErrors.Invalid("Unknown set."));
    }

    private Result<T> Refused<T>(Error error)
    {
        EvalLog.Refused(logger, error.Code);
        return Result.Failure<T>(error);
    }
}
