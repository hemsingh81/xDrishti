using XDrishti.Application.Assistant.Evaluation;
using XDrishti.Domain.Assistant.Evaluation;

namespace XDrishti.Application.Tests.Ai;

/// <summary>P15-07 criterion 7: set D reads data/eval/sets/announcements.jsonl when it is there (no classifier yet, so not evaluable), keeps the old state when it is not, fails with the line named when it is invalid; the sets hash follows the files.</summary>
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

    [Fact]
    public async Task A_valid_file_of_200_rows_is_loaded_and_the_set_is_not_evaluable_for_want_of_a_classifier()
    {
        var rig = new EvalRig { SetD = (Jsonl(200), "{}", "{}") };

        var events = await EventsAsync(rig);

        events.Outcome.State.ShouldBe(SetState.NotEvaluable);
        events.Outcome.Reasons.ShouldBe(["not_evaluable: no classifier yet (P15-08)"]);
        events.Cases.ShouldBe(200);
        rig.Answers.Calls.ShouldBe(0);
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
