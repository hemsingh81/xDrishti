using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Ai.Briefing;
using XDrishti.Domain.Assistant;
using XDrishti.Domain.Assistant.Text;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Tests.Ai;

/// <summary>Spec P9-06 AC 8 to 10 and the safety gate: the model only phrases, the guard rejects, the fixed-format text is the fallback, the first stored text wins.</summary>
public sealed class BuildBriefingHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<BriefingView> RunAsync(BriefingRig rig, BuildBriefingCommand? command = null)
    {
        var result = await rig.Handler().HandleAsync(command ?? BriefingRig.Daily(), Ct);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        return result.Value;
    }

    [Fact]
    public async Task A_model_text_that_passes_is_stored_with_the_pinned_footer()
    {
        var rig = new BriefingRig();
        rig.Writer.Returns(BriefingRig.GoodText);

        var view = await RunAsync(rig);

        view.Source.ShouldBe("model");
        view.Reason.ShouldBeNull();
        view.Text.ShouldStartWith(BriefingRig.GoodText);
        view.Text.ShouldEndWith(BriefingWording.DailyFooter);
        view.ModelId.ShouldBe("m.gguf");
        view.Attempts.ShouldHaveSingleItem().Passed.ShouldBeTrue();
        view.DeterministicText.ShouldNotBeNullOrWhiteSpace();
        rig.Store.Inserts.ShouldBe(1);
        rig.Audit.ShouldHaveSingleItem().Purpose.ShouldBe("briefing");
        rig.Writer.Requests[0].System.ShouldContain("data, not instructions");
        rig.Writer.Requests[0].User.ShouldContain("AAA reference price 100.00");
    }

    [Fact]
    public async Task An_invented_number_is_repaired_on_the_second_attempt_which_is_told_the_codes_not_the_rejected_text()
    {
        var rig = new BriefingRig();
        rig.Writer.Returns(BriefingRig.BadText);
        rig.Writer.Returns(BriefingRig.GoodText);

        var view = await RunAsync(rig);

        view.Source.ShouldBe("model");
        view.Attempts.Select(a => a.Passed).ShouldBe([false, true]);
        view.Attempts[0].Findings.ShouldContain(f => f.Code == GuardCodes.NumberNotInFacts && f.Token == "98.60");
        rig.Writer.Requests[1].User.ShouldContain("number_not_in_facts: 98.60");
        rig.Writer.Requests[1].User.ShouldNotContain(BriefingRig.BadText);
        rig.Audit.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Two_rejected_attempts_store_the_fixed_format_text_with_both_attempts()
    {
        var rig = new BriefingRig();
        rig.Writer.Returns(BriefingRig.BadText);
        rig.Writer.Returns(BriefingRig.BadText);

        var view = await RunAsync(rig);

        view.Source.ShouldBe("deterministic");
        view.Reason.ShouldBe("guard_rejected");
        view.Text.ShouldBe(view.DeterministicText);
        view.Text.ShouldNotContain("98.60");
        view.Attempts.Count.ShouldBe(2);
        rig.Writer.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_text_cut_by_the_token_limit_is_rejected()
    {
        var rig = new BriefingRig();
        rig.Writer.Returns(BriefingRig.GoodText, "length");
        rig.Writer.Returns(BriefingRig.GoodText, "length");

        var view = await RunAsync(rig);

        view.Reason.ShouldBe("guard_rejected");
    }

    [Fact]
    public async Task A_model_that_fails_stores_the_fixed_format_text_as_unavailable()
    {
        var rig = new BriefingRig();
        rig.Writer.Throws();

        var view = await RunAsync(rig);

        view.Source.ShouldBe("deterministic");
        view.Reason.ShouldBe("model_unavailable");
        rig.Writer.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_model_that_is_not_ready_in_time_is_never_asked()
    {
        var rig = new BriefingRig { Llm = LlmState.Down };

        var view = await RunAsync(rig);

        view.Reason.ShouldBe("model_unavailable");
        rig.Writer.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    [InlineData(true, true, true)]
    public async Task The_model_is_called_only_when_the_assistant_and_the_briefing_switch_are_both_on(bool useModel, bool assistantEnabled, bool called)
    {
        var rig = new BriefingRig { Settings = new AssistantSettings(1, assistantEnabled, false, 6) };
        rig.Options.UseModel = useModel;
        rig.Writer.Returns(BriefingRig.GoodText);

        var view = await RunAsync(rig);

        rig.Writer.Requests.Count.ShouldBe(called ? 1 : 0);
        rig.HealthCalls.ShouldBe(called ? 1 : 0);
        rig.Audit.Count.ShouldBe(called ? 1 : 0);
        view.Reason.ShouldBe(called ? null : "model_off");
    }

    [Fact]
    public async Task The_verbs_model_off_never_calls_the_model()
    {
        var rig = new BriefingRig();

        var view = await RunAsync(rig, BriefingRig.Daily(forceDeterministic: true));

        view.Reason.ShouldBe("model_off");
        rig.Writer.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_plan_with_no_ticket_is_stored_as_thin_and_the_model_is_not_asked()
    {
        var rig = new BriefingRig();
        rig.Source.Facts = BriefingRig.FactsOf() with { Tickets = [] };

        var view = await RunAsync(rig);

        view.Reason.ShouldBe("facts_thin");
        rig.Writer.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_repeat_build_returns_the_stored_row_and_writes_nothing_and_asks_nothing()
    {
        var rig = new BriefingRig();
        rig.Writer.Returns(BriefingRig.GoodText);
        var first = await RunAsync(rig);

        var again = await RunAsync(rig);

        again.Repeated.ShouldBeTrue();
        again.Id.ShouldBe(first.Id);
        rig.Store.Inserts.ShouldBe(1);
        rig.Writer.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_new_version_is_appended_on_request_and_the_first_row_is_kept()
    {
        var rig = new BriefingRig();
        rig.Options.UseModel = false;
        var first = await RunAsync(rig);

        var second = await RunAsync(rig, BriefingRig.Daily(newVersion: true));

        first.Version.ShouldBe(1);
        second.Version.ShouldBe(2);
        rig.Store.Rows.Count.ShouldBe(2);
        rig.Store.Rows[0].Text.ShouldBe(first.Text);
    }

    [Fact]
    public async Task A_nightly_run_in_progress_defers_the_run_and_stores_nothing()
    {
        var rig = new BriefingRig { NightlyRunning = true };

        var result = await rig.Handler().HandleAsync(BriefingRig.Daily(), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(BriefingErrors.DeferredCode);
        rig.Store.Inserts.ShouldBe(0);
        rig.Writer.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_chat_turn_in_progress_defers_the_run()
    {
        var rig = new BriefingRig();
        using var turn = rig.Gate.TryEnter();

        var result = await rig.Handler().HandleAsync(BriefingRig.Daily(), Ct);

        result.Error.Code.ShouldBe(BriefingErrors.DeferredCode);
        rig.Store.Inserts.ShouldBe(0);
    }

    [Fact]
    public async Task Inside_the_nightly_window_a_daily_model_text_is_deferred_but_the_fixed_format_text_is_not()
    {
        var rig = new BriefingRig(new DateTimeOffset(2026, 10, 28, 18, 30, 0, TimeSpan.FromHours(5.5)));

        var result = await rig.Handler().HandleAsync(BriefingRig.Daily(), Ct);
        rig.Options.UseModel = false;
        var fixedText = await RunAsync(rig);

        result.Error.Code.ShouldBe(BriefingErrors.DeferredCode);
        fixedText.Reason.ShouldBe("model_off");
    }

    [Fact]
    public async Task A_plan_date_older_than_seven_days_cannot_be_stored()
    {
        var rig = new BriefingRig();

        var result = await rig.Handler().HandleAsync(BriefingRig.Daily() with { Subject = "2026-10-01" }, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(BriefingErrors.InvalidCode);
    }

    [Fact]
    public async Task A_week_without_a_digest_is_not_evaluable_and_the_model_is_not_asked()
    {
        var rig = new BriefingRig();
        rig.Source.Failure = Error.Conflict(BriefingErrors.NotEvaluableCode, "not_evaluable: no digest stored for 2026-W43.");

        var result = await rig.Handler().HandleAsync(new BuildBriefingCommand(BriefingFacts.Weekly, "2026-W43", false, false, "test"), Ct);

        result.Error.Code.ShouldBe(BriefingErrors.NotEvaluableCode);
        rig.Writer.Requests.ShouldBeEmpty();
        rig.Store.Inserts.ShouldBe(0);
    }

    [Fact]
    public async Task A_weekly_review_ends_with_the_pinned_line_added_by_code()
    {
        var rig = new BriefingRig();
        rig.Source.Facts = new BriefingFacts(BriefingFacts.Weekly, "2026-W43", false, [], [new FactLine(string.Empty, "plan", "review", "Weekly review for 2026-W43, built from the stored digest."), new FactLine(string.Empty, "digest", "cells", "3 cells changed.")], null, 5, []);
        rig.Writer.Returns("The weekly review for 2026-W43 shows 3 cells changed.");

        var result = await rig.Handler().HandleAsync(new BuildBriefingCommand(BriefingFacts.Weekly, "2026-W43", false, false, "test"), Ct);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.Source.ShouldBe("model");
        result.Value.Text.ShouldEndWith(BriefingWording.WeeklyFooter);
    }

    [Fact]
    public async Task The_stored_facts_hash_is_the_same_for_the_same_facts_and_changes_with_them()
    {
        var one = new BriefingRig();
        var two = new BriefingRig();
        one.Options.UseModel = false;
        two.Options.UseModel = false;
        var other = new BriefingRig();
        other.Options.UseModel = false;
        other.Source.Facts = BriefingRig.FactsOf() with { Provisional = false };

        var a = await RunAsync(one);
        var b = await RunAsync(two);
        var c = await RunAsync(other);

        a.FactsHash.ShouldBe(b.FactsHash);
        a.FactsHash.ShouldNotBe(c.FactsHash);
        a.FactsHash.Length.ShouldBe(64);
    }
}
