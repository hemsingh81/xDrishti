using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Assistant.Evaluation;
using XDrishti.Application.Tests.Filings;
using XDrishti.Domain.Assistant.Evaluation;
using XDrishti.Domain.Filings;
using XDrishti.Domain.Filings.Extraction;

namespace XDrishti.Application.Tests.Ai;

/// <summary>P15-07 criterion 7: set D reads data/eval/sets/announcements.jsonl when it is there and (P15-08) classifies every row, keeps the old state when it is not, fails with the line named when it is invalid; the sets hash follows the files.</summary>
public sealed class RunEvalAnnouncementsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static RunEvalCommand EventsOnly() => new(["events"], 3, RunEvalCommand.Builtin, EvalErrors.CliActor);

    private static string Jsonl(int rows) => AnnouncementSet.Write(Enumerable.Range(1, rows).Select(n => new LabelledAnnouncement($"a{n:000}", "TCS", "2026-08-03T05:30:00.000000Z", "nse-announcement", "Text " + n, n % 3 == 0, "Results")));

    private static async Task<SetSummary> EventsAsync(EvalRig rig)
    {
        var result = await rig.Run().HandleAsync(EventsOnly(), Ct);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        return result.Value.Summary.Sets.Single(s => s.Set == "events");
    }

    private const string Baseline = """{"version":1,"categories":{"Results":"material"}}""";

    private static EvalRig Graded(int rows = 200) => new() { SetD = (Jsonl(rows), "{}", Baseline) };

    // The rows of Jsonl() are labelled material when the number is a multiple of 3; the script answers from the text ("Text 3" is row 3).
    private static AnnouncementFlag Perfect(AnnouncementFlagInput input) => FakeFlagger.Ok(int.Parse(input.Text.AsSpan(5), System.Globalization.CultureInfo.InvariantCulture) % 3 == 0);

    [Fact]
    public async Task A_valid_file_of_200_rows_with_the_baseline_is_graded_through_the_flagger_with_every_repeat_and_the_hostile_set()
    {
        var rig = Graded();
        rig.Flagger.Script = (input, _) => input.Text.StartsWith("Text ", StringComparison.Ordinal) ? Perfect(input) : FakeFlagger.Ok(false);

        var events = await EventsAsync(rig);

        events.Outcome.State.ShouldBe(SetState.Pass);
        events.Cases.ShouldBe(200);
        rig.Flagger.Calls.Count.ShouldBe((200 * 3) + HostileAnnouncements.All.Count);
        rig.Flagger.Calls.Take(3).ShouldAllBe(c => c.Subject == string.Empty && c.Category == "Results");
        events.Outcome.Reasons.ShouldContain(r => r.StartsWith("paired with the category baseline", StringComparison.Ordinal));
        events.Outcome.Reasons.ShouldContain(r => r.StartsWith($"hostile_set: {HostileAnnouncements.All.Count} injected announcements", StringComparison.Ordinal));
        events.Rates.Select(r => r.Name).ShouldBe(["precision_material", "recall_material", "baseline_precision_material", "baseline_recall_material"]);
        rig.Answers.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Repeats_that_disagree_are_unstable_count_as_not_material_and_are_reported()
    {
        var rig = Graded();
        var seen = new Dictionary<string, int>();
        rig.Flagger.Script = (input, _) =>
        {
            seen[input.Text] = seen.GetValueOrDefault(input.Text) + 1;
            if (!input.Text.StartsWith("Text ", StringComparison.Ordinal))
            {
                return FakeFlagger.Ok(false);
            }

            var perfect = Perfect(input);
            return input.Text is "Text 3" or "Text 6" && seen[input.Text] == 2 ? FakeFlagger.Ok(!perfect.Answer!.Material) : perfect;
        };

        var events = await EventsAsync(rig);

        events.Outcome.Reasons.ShouldContain("unstable: 2 cases changed their answer between repeats and count as not material");
        events.Stability.ShouldNotBeNull().ShouldBe(1.0 - (2.0 / 200), 1e-9);
        events.Repeats.ShouldBe(3);
    }

    [Fact]
    public async Task An_answer_that_is_not_ok_is_not_a_material_prediction_so_too_few_are_predicted_and_the_set_fails()
    {
        var rig = Graded();
        rig.Flagger.Script = (_, _) => FakeFlagger.Invalid();

        var events = await EventsAsync(rig);

        events.Outcome.State.ShouldBe(SetState.Fail);
        events.Outcome.Reasons[0].ShouldContain("the model called no announcement material");
        events.Outcome.Reasons[0].ShouldContain("too_few_predicted: 0 predicted material, 30 needed");
    }

    [Fact]
    public async Task A_model_that_goes_down_stops_the_run_and_no_run_is_stored()
    {
        var rig = Graded();
        rig.Flagger.DownAfter = 10;

        var result = await rig.Run().HandleAsync(EventsOnly(), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(EvalErrors.ModelUnavailableCode);
        rig.Store.Runs.ShouldBeEmpty();
    }

    [Fact]
    public async Task Enough_rows_without_a_valid_baseline_are_not_evaluable_and_the_model_is_not_asked()
    {
        var rig = new EvalRig { SetD = (Jsonl(200), "{}", "{}") };

        var events = await EventsAsync(rig);

        events.Outcome.State.ShouldBe(SetState.NotEvaluable);
        events.Outcome.Reasons.Single().ShouldContain("announcement-baseline.json");
        rig.Flagger.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_weights_come_from_the_sample_file_and_the_report_prints_the_weighted_precision()
    {
        var rig = Graded();
        var rows = Enumerable.Range(1, 200).Select(n => new SampleFileRow($"a{n:000}", Guid.NewGuid(), "material", false, 2000, 200, 10.0, new string('0', 64))).ToList();
        rig.AnnouncementFiles.Files[AnnouncementFile.Sample] = new AnnouncementSampleFile("1", 1337, "2026-01-01", "2026-12-31", new string('b', 64), 2000, 2000, 0, 0, 200, 40, [new SampleStratum("material", "Results", 2026, 2000, 200)], rows).ToJson();
        rig.Flagger.Script = (input, _) => input.Text.StartsWith("Text ", StringComparison.Ordinal) ? Perfect(input) : FakeFlagger.Ok(false);

        var events = await EventsAsync(rig);

        events.Outcome.Reasons.ShouldContain(r => r.StartsWith("population-weighted precision of material (weights recomputed from the labelled rows per stratum): 1.000", StringComparison.Ordinal));
    }

    [Fact]
    public async Task No_file_keeps_the_old_state()
    {
        var events = await EventsAsync(new EvalRig());

        events.Outcome.State.ShouldBe(SetState.NotEvaluable);
        events.Outcome.Reasons.ShouldBe(["not_evaluable: 200 labelled announcements arrive with P15"]);
    }

    [Fact]
    public async Task A_file_with_fewer_than_200_rows_is_not_evaluable_and_the_note_names_the_count()
    {
        var events = await EventsAsync(new EvalRig { SetD = (Jsonl(199), "{}", "{}") });

        events.Outcome.State.ShouldBe(SetState.NotEvaluable);
        events.Outcome.Reasons.ShouldBe(["not_evaluable: the labelled file holds 199 announcements; 200 are needed"]);
    }

    [Theory]
    [InlineData("""{"id":"a","symbol":"S","published_at":"2026-08-03T05:30:00Z","source":"x","text":"SECRET","label":"unsure"}""")]
    [InlineData("""{"id":"a","symbol":"S","text":"SECRET"}""")]
    [InlineData("nonsense SECRET")]
    public async Task An_invalid_file_fails_the_set_naming_the_line_and_never_the_text(string bad)
    {
        var events = await EventsAsync(new EvalRig { SetD = (Jsonl(2).TrimEnd() + "\n" + bad + "\n", "{}", "{}") });

        events.Outcome.State.ShouldBe(SetState.Fail);
        events.Outcome.Reasons.Single().ShouldContain("line 3");
        events.Outcome.Reasons.Single().ShouldNotContain("SECRET");
    }

    [Fact]
    public void The_sets_hash_follows_the_label_file_the_manifest_and_the_baseline_and_is_unchanged_when_there_is_no_file()
    {
        var none = EvalWorld.Load(DiskEvalFiles.Read()).Value.SetsHash;
        string Hash(string? labels, string? manifest, string? baseline) => EvalWorld.Load(DiskEvalFiles.Read(labels, manifest, baseline)).Value.SetsHash;
        var labels = Jsonl(200);

        var with = Hash(labels, "m", "b");

        with.ShouldNotBe(none);
        Hash(labels + "\n", "m", "b").ShouldNotBe(with);
        Hash(labels, "m2", "b").ShouldNotBe(with);
        Hash(labels, "m", "b2").ShouldNotBe(with);
        Hash(labels, "m", "b").ShouldBe(with);
        Hash(null, "m", "b").ShouldBe(none);
    }
}
