using XDrishti.Application.Ai;
using XDrishti.Application.Assistant.Evaluation;
using XDrishti.Application.Tests.Filings;
using XDrishti.Domain.Assistant.Evaluation;
using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Application.Tests.Ai;

/// <summary>
/// P15-08 criteria 7 and 8 end to end: protocol L reads the set D grade of the newest ACCEPTED run with the stored model and prompt hashes (else NOT_EVALUABLE, never a pass), and the two new threshold keys make an accepted baseline stale with reason <c>thresholds</c>. A scripted flagger; no model.
/// </summary>
public sealed class AnnouncementGradeTests
{
    private const string Baseline = """{"version":1,"categories":{"Results":"material"}}""";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Jsonl() => AnnouncementSet.Write(Enumerable.Range(1, 200).Select(n => new LabelledAnnouncement($"a{n:000}", "TCS", "2026-08-03T05:30:00.000000Z", "nse-announcement", "Text " + n, n % 3 == 0, "Results")));

    private static EvalRig GradedRig(bool perfect)
    {
        var rig = new EvalRig { SetD = (Jsonl(), "{}", Baseline) };
        rig.Flagger.Script = (input, _) => perfect && input.Text.StartsWith("Text ", StringComparison.Ordinal) ? FakeFlagger.Ok(int.Parse(input.Text.AsSpan(5), System.Globalization.CultureInfo.InvariantCulture) % 3 == 0) : FakeFlagger.Ok(false);
        return rig;
    }

    private static async Task<Guid> RunAllAsync(EvalRig rig)
    {
        var result = await rig.Run().HandleAsync(new RunEvalCommand(["all"], 3, RunEvalCommand.Builtin, EvalErrors.CliActor), Ct);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        return result.Value.Id;
    }

    [Fact]
    public async Task With_nothing_accepted_there_is_no_grade_and_protocol_L_is_not_evaluable()
    {
        var rig = GradedRig(true);
        await RunAllAsync(rig);

        var grade = await new EvalFlagGradeSource(rig.Store).NewestAsync(Ct);

        grade.ShouldBeNull();
        ProtocolL.Evaluate(grade).Verdict.ShouldBe(ExtensionVerdict.NotEvaluable);
    }

    [Fact]
    public async Task An_accepted_run_with_set_D_pass_gives_the_grade_with_the_stored_model_and_prompt_hashes_and_protocol_L_keeps()
    {
        var rig = GradedRig(true);
        var id = await RunAllAsync(rig);
        (await rig.Accept().HandleAsync(new AcceptEvalCommand(id, null, EvalErrors.CliActor), Ct)).IsSuccess.ShouldBeTrue();

        var grade = await new EvalFlagGradeSource(rig.Store).NewestAsync(Ct);

        grade.ShouldNotBeNull();
        (grade.EvalRunId, grade.Labels, grade.Gating, grade.Passed, grade.ModelHash, grade.PromptHash).ShouldBe((id.ToString("D"), 200, true, true, rig.Model.Sha256, PromptRegistry.AnnouncementFlag.Sha256));
        ProtocolL.Evaluate(grade).Verdict.ShouldBe(ExtensionVerdict.Keep);
    }

    [Fact]
    public async Task A_run_whose_set_D_is_not_evaluable_or_absent_gives_no_grade_even_when_accepted()
    {
        var rig = new EvalRig();
        var id = await RunAllAsync(rig);
        await rig.Accept().HandleAsync(new AcceptEvalCommand(id, null, EvalErrors.CliActor), Ct);

        var grade = await new EvalFlagGradeSource(rig.Store).NewestAsync(Ct);

        grade.ShouldBeNull();
    }

    [Fact]
    public async Task An_accepted_run_that_stored_no_announcement_flag_prompt_or_no_model_hash_gives_no_grade()
    {
        var rig = GradedRig(true);
        var id = await RunAllAsync(rig);
        await rig.Accept().HandleAsync(new AcceptEvalCommand(id, null, EvalErrors.CliActor), Ct);
        var run = rig.Store.Runs.Single();
        var source = new EvalFlagGradeSource(rig.Store);

        rig.Store.Runs[0] = run with { PromptVersionsJson = EvalPromptJson.Write([.. rig.Prompts.Where(p => p.Name != "announcement-flag")]) };
        var withoutPrompt = await source.NewestAsync(Ct);
        rig.Store.Runs[0] = run with { ModelSha256 = string.Empty };
        var withoutModel = await source.NewestAsync(Ct);

        withoutPrompt.ShouldBeNull();
        withoutModel.ShouldBeNull();
    }

    [Fact]
    public async Task A_set_D_that_fails_makes_the_whole_run_fail_so_it_cannot_be_accepted_and_no_grade_exists()
    {
        var rig = GradedRig(false);
        var id = await RunAllAsync(rig);

        var accepted = await rig.Accept().HandleAsync(new AcceptEvalCommand(id, null, EvalErrors.CliActor), Ct);

        accepted.IsFailure.ShouldBeTrue();
        (await new EvalFlagGradeSource(rig.Store).NewestAsync(Ct)).ShouldBeNull();
        rig.Store.Runs.Single().Verdict.ShouldBe(EvalVerdict.Fail);
    }

    [Fact]
    public async Task The_new_threshold_keys_make_a_baseline_accepted_before_them_stale_with_reason_thresholds()
    {
        var rig = new EvalRig();
        var id = await RunAllAsync(rig);
        await rig.Accept().HandleAsync(new AcceptEvalCommand(id, null, EvalErrors.CliActor), Ct);
        var status = rig.Status();
        (await status.HandleAsync(new GetEvalStatusQuery(), Ct)).Stale.ShouldBeFalse();

        // The run as it was stored when thresholds.json had no min_predicted_material and no baseline_sign_p.
        var run = rig.Store.Runs.Single();
        var oldHash = EvalHash(OldThresholds);
        var world = EvalWorld.Load(DiskEvalFiles.Read()).Value;
        world.ThresholdsHash.ShouldNotBe(oldHash);
        var inputs = new FingerprintInputs(run.ModelSha256, run.RuntimeDigest, rig.Prompts, run.SetsHash, oldHash, run.FixtureHash, run.Profile);
        rig.Store.Runs[0] = run with { ThresholdsHash = oldHash, Fingerprint = EvalFingerprint.Of(inputs) };

        var stale = await status.HandleAsync(new GetEvalStatusQuery(), Ct);

        stale.Stale.ShouldBeTrue();
        stale.Reasons.ShouldBe(["thresholds"]);
        stale.State.ShouldBe("not_yet_evaluated");
    }

    private const string OldThresholds = """
        {
          "version": 1,
          "data": { "of": 30, "pass_min": 27 },
          "plans": { "of": 10, "raw_pass_min": 8, "repaired_pass_min": 9, "escape_max": 0, "covered_all": true },
          "ideas": { "of": 10, "first_shot_valid_min": 8, "repaired_valid_min": 9, "intent_min": 0.85, "intent_floor": 0.60, "decline_correct_min": 2 },
          "events": { "precision_material_min": 0.80, "min_rows": 200 }
        }
        """;

    private static string EvalHash(string text) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text.ReplaceLineEndings("\n"))));
}
