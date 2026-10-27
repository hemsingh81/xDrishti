#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using NSubstitute;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Ai.Drafting;
using XDrishti.Application.Lab;
using XDrishti.Domain.Assistant.Drafting;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Tests.Ai;

/// <summary>Spec P9-07 AC 13 (slice 07b): the auto option is off by default and runs only for a clean owner-typed job draft that names exactly one basket, within 1 per hour and 3 per day, as a QUICK test, recorded as auto. Any doubt leaves the draft for the owner.</summary>
public sealed class AutoDraftTests
{
    private const string Named = "On MyChoice buy when the 15-min close is above VWAP with RSI(14) above 55 and exit at 15:15 with a target of 1:2.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DraftRig On(params string[] baskets)
    {
        var rig = new DraftRig();
        rig.Settings = rig.Settings with { AutoLabQuickTest = true };
        rig.Baskets.ListAsync(false, Arg.Any<CancellationToken>()).Returns(baskets.Select(b => DraftRig.Basket(b)).ToList());
        rig.Start.HandleAsync(Arg.Any<StartLabRunCommand>(), Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult(Result.Success(new StartedLabRun(LabRunView.Of(Run()), true))));
        return rig;
    }

    private static XDrishti.Application.Abstractions.Lab.LabRun Run() => new(
        Guid.NewGuid(), new string('1', 64), "DRAFT_X", 1, new string('a', 64), new LabUniverse("basket", null, "basket", 3, [new LabInstrument(Guid.NewGuid(), "AAA")], false, null), new DateOnly(2025, 5, 1), new DateOnly(2026, 4, 30), "quick", new string('c', 64), "sim-5",
        XDrishti.Domain.Lab.LabRunState.Queued, null, 0, 0, [], null, [], [], [], null, false, DateTimeOffset.UnixEpoch, null, null);

    private static async Task<AutoDraftOutcome> AskAsync(DraftRig rig, long id) => (await rig.AutoHandler().HandleAsync(new AutoDraftCommand(id), Ct)).Value;

    private static async Task NothingRanAsync(DraftRig rig)
    {
        await rig.Create.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
        await rig.Start.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
        rig.Store.Decisions.ShouldBeEmpty();
    }

    [Fact]
    public async Task By_default_the_option_is_off_and_nothing_is_stored_or_run()
    {
        var rig = On("MyChoice");
        rig.Settings = rig.Settings with { AutoLabQuickTest = false };
        var draft = rig.Seed(idea: Named);

        (await AskAsync(rig, draft.Id)).ShouldBe(new AutoDraftOutcome(false, "off"));
        await NothingRanAsync(rig);
    }

    [Theory]
    [InlineData("drafting")]
    [InlineData("assistant")]
    [InlineData("nosnapshot")]
    public async Task Each_master_switch_and_the_setting_row_are_needed(string which)
    {
        var rig = On("MyChoice");
        if (which == "drafting")
        {
            rig.Options.Enabled = false;
        }
        else if (which == "assistant")
        {
            rig.Settings = rig.Settings with { Enabled = false };
        }
        else
        {
            rig.Settings = rig.Settings with { SnapshotHash = null };
        }

        (await AskAsync(rig, rig.Seed(idea: Named).Id)).Started.ShouldBeFalse();
        await NothingRanAsync(rig);
    }

    [Fact]
    public async Task A_clean_owner_job_draft_naming_one_basket_is_stored_and_gets_a_quick_test_recorded_as_auto_with_the_setting_snapshot()
    {
        var rig = On("MyChoice", "Other");
        var draft = rig.Seed(idea: Named);

        var outcome = await AskAsync(rig, draft.Id);

        outcome.ShouldBe(new AutoDraftOutcome(true, "started"));
        await rig.Create.Received(1).HandleAsync(Arg.Is<CreateStrategyCommand>(c => c.Text == DraftRig.Yaml && c.Note!.Contains("auto-confirmed")), Arg.Any<CancellationToken>());
        await rig.Start.Received(1).HandleAsync(Arg.Is<StartLabRunCommand>(c => c.Method == "quick" && c.BasketId != null && c.Symbols == null && c.From == null), Arg.Any<CancellationToken>());
        var decision = rig.Store.LastDecision!;
        (decision.Auto, decision.Decision, decision.Actor, decision.SettingsId, decision.SnapshotHash, decision.Edited).ShouldBe((true, "confirmed", "system:auto-draft", 1L, new string('e', 64), false));
    }

    [Fact]
    public async Task A_draft_of_the_chat_tool_or_the_cli_or_a_non_owner_is_never_auto()
    {
        var rig = On("MyChoice");
        var chat = rig.Seed(idea: Named, via: "chat_tool");
        var cli = rig.Seed(idea: Named, requestedBy: "cli:xd");
        var service = rig.Seed(idea: Named, requestedBy: "service:eval");

        foreach (var draft in new[] { chat, cli, service })
        {
            (await AskAsync(rig, draft.Id)).Reason.ShouldBe("not_an_owner_job_draft");
        }

        await NothingRanAsync(rig);
    }

    [Fact]
    public async Task A_draft_with_a_finding_that_is_not_valid_or_already_decided_is_left_for_the_owner()
    {
        var rig = On("MyChoice");
        var finding = rig.Seed(idea: Named, findings: [new DraftFinding("idea_number_dropped", "2022")]);
        var invalid = rig.Seed("invalid", idea: Named);
        var decided = rig.Seed(idea: Named);
        rig.Store.Decisions[decided.Id] = new Application.Abstractions.Ai.StoredDraftDecision(decided.Id, "discarded", null, null, null, false, false, DraftRig.Owner, DateTimeOffset.UnixEpoch);

        (await AskAsync(rig, finding.Id)).Reason.ShouldBe("not_clean");
        (await AskAsync(rig, invalid.Id)).Reason.ShouldBe("not_clean");
        (await AskAsync(rig, decided.Id)).Reason.ShouldBe("decided");
        await rig.Create.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
    }

    [Theory]
    [InlineData("Buy when the close is above VWAP on any basket.", "no_basket_named")]
    [InlineData("On MyChoice and Other buy when the close is above VWAP with RSI(14).", "many_baskets_named")]
    [InlineData("On MyChoices buy when the close is above VWAP with RSI(14) above 55.", "no_basket_named")]
    [InlineData("On mychoice buy when the close is above VWAP with RSI(14) above 55.", "no_basket_named")]
    public async Task The_idea_must_name_exactly_one_basket_by_its_exact_name(string idea, string reason)
    {
        var rig = On("MyChoice", "Other");

        (await AskAsync(rig, rig.Seed(idea: idea).Id)).Reason.ShouldBe(reason);
        await NothingRanAsync(rig);
    }

    [Fact]
    public async Task An_archived_basket_does_not_count_and_an_unreadable_basket_list_is_no_pass()
    {
        var archived = new DraftRig();
        archived.Settings = archived.Settings with { AutoLabQuickTest = true };
        archived.Baskets.ListAsync(false, Arg.Any<CancellationToken>()).Returns([DraftRig.Basket("MyChoice", archived: true)]);
        var broken = On("MyChoice");
        broken.Baskets.ListAsync(false, Arg.Any<CancellationToken>()).Returns<IReadOnlyList<XDrishti.Application.Market.Baskets.BasketView>>(_ => throw new InvalidOperationException("unavailable"));

        (await AskAsync(archived, archived.Seed(idea: Named).Id)).Reason.ShouldBe("no_basket_named");
        (await AskAsync(broken, broken.Seed(idea: Named).Id)).Reason.ShouldBe("basket_unavailable");
        await NothingRanAsync(archived);
        await NothingRanAsync(broken);
    }

    [Fact]
    public async Task A_taken_id_is_left_for_the_owner()
    {
        var rig = On("MyChoice");
        rig.Strategies.GetAsync("DRAFT_X", Arg.Any<CancellationToken>()).Returns(new Application.Abstractions.Research.StoredStrategy("DRAFT_X", "intraday", false, DateTimeOffset.UnixEpoch, 1, 1));

        (await AskAsync(rig, rig.Seed(idea: Named).Id)).Reason.ShouldBe("id_taken");
        await NothingRanAsync(rig);
    }

    [Fact]
    public async Task At_most_one_auto_run_in_an_hour_and_three_on_an_india_date()
    {
        var rig = On("MyChoice");
        rig.Store.Now = rig.Time.GetUtcNow();
        var first = rig.Seed(idea: Named);
        var second = rig.Seed(idea: Named);
        (await AskAsync(rig, first.Id)).Started.ShouldBeTrue();
        (await AskAsync(rig, second.Id)).Reason.ShouldBe("limit_hour");

        // three earlier today (India), none within the hour: the day limit holds
        var day = On("MyChoice");
        day.Time.SetUtcNow(new DateTimeOffset(2026, 10, 28, 15, 0, 0, TimeSpan.FromHours(5.5)));
        for (var h = 0; h < 3; h++)
        {
            day.Store.Now = new DateTimeOffset(2026, 10, 28, 8 + h, 0, 0, TimeSpan.FromHours(5.5));
            await day.Store.InsertDecisionAsync(new Application.Abstractions.Ai.NewDraftDecision(100 + h, "confirmed", "S" + h, 1, null, false, true, 1, new string('e', 64), "system:auto-draft"), Ct);
        }

        (await AskAsync(day, day.Seed(idea: Named).Id)).Reason.ShouldBe("limit_day");
        await day.Create.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
    }

    [Fact]
    public async Task Concurrent_auto_candidates_cannot_pass_the_limits_together()
    {
        var rig = On("MyChoice");
        rig.Store.Now = rig.Time.GetUtcNow();
        var ids = Enumerable.Range(0, 6).Select(_ => rig.Seed(idea: Named).Id).ToList();

        var outcomes = await Task.WhenAll(ids.Select(id => AskAsync(rig, id)));

        outcomes.Count(o => o.Started).ShouldBe(1);
        await rig.Create.Received(1).HandleAsync(Arg.Any<CreateStrategyCommand>(), Arg.Any<CancellationToken>());
        await rig.Start.Received(1).HandleAsync(Arg.Any<StartLabRunCommand>(), Arg.Any<CancellationToken>());
        rig.Store.Decisions.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_quick_test_that_cannot_start_is_recorded_with_its_reason_and_is_never_a_pass_for_the_next()
    {
        var rig = On("MyChoice");
        rig.Start.HandleAsync(Arg.Any<StartLabRunCommand>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Failure<StartedLabRun>(LabErrors.NoData(new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1)))));
        var draft = rig.Seed(idea: Named);

        var outcome = await AskAsync(rig, draft.Id);

        outcome.ShouldBe(new AutoDraftOutcome(true, "run_not_started"));
        rig.Store.Decisions[draft.Id].RunError.ShouldBe("lab.no_data");
        rig.Store.Decisions[draft.Id].LabRunId.ShouldBeNull();
    }

    [Fact]
    public async Task The_job_asks_the_auto_option_only_for_a_valid_draft()
    {
        var rig = new DraftRig();
        rig.Answers.Enqueue(() => DraftRig.Reply(DraftRig.Json()));
        rig.Answers.Enqueue(() => DraftRig.Reply(DraftRig.Json(null, decline: true)));

        await rig.Job().HandleAsync(new DraftStrategyCommand(1, DraftRig.Idea, null, DraftRig.Owner), Ct);
        await rig.Job().HandleAsync(new DraftStrategyCommand(2, "buy on news and results", null, DraftRig.Owner), Ct);

        rig.Auto.Asked.ShouldBe([1L]);
    }
}
