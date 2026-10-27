#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using System.Text.Json;
using XDrishti.Application.Ai;
using XDrishti.Application.Ai.Drafting;
using XDrishti.Domain.Assistant;

namespace XDrishti.Application.Tests.Ai;

/// <summary>Spec P9-07 (slice 07b): lab_propose_run only proposes. It stores one insert-only chat_tool draft through the same core as the job, takes the idea from the owner's message and never from an argument, and is offered only in the owner's own chat turn.</summary>
public sealed class DraftProposalToolTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static TurnRequest Chat(string actor = DraftRig.Owner, string origin = TurnRequest.Chat, string question = DraftRig.Idea) => new(question, null, origin, actor);

    private static DraftProposalTool Tool(DraftRig rig) => new(rig.Submit(), rig.Store, new FakeSheet(), rig.Options, rig.Time);

    private sealed class FakeSheet : Application.Abstractions.Ai.IDraftSheetSource
    {
        public string Sheet => "S";

        public string Hash => new('d', 64);
    }

    [Fact]
    public async Task It_stores_a_chat_tool_draft_with_the_owners_message_as_the_idea_and_answers_id_status_problems_and_findings()
    {
        var rig = new DraftRig();
        var tool = Tool(rig);

        var text = await tool.RunAsync(DraftRig.Json(), Chat(), Ct);

        using var document = JsonDocument.Parse(text);
        var row = document.RootElement.GetProperty("rows")[0];
        document.RootElement.GetProperty("ok").GetBoolean().ShouldBeTrue();
        (row.GetProperty("draftId").GetInt64(), row.GetProperty("status").GetString()).ShouldBe((1L, "valid"));
        row.GetProperty("findings").GetArrayLength().ShouldBe(0);
        var stored = rig.Store.Drafts[1];
        (stored.Via, stored.Idea, stored.Status, stored.RequestedBy, stored.Yaml).ShouldBe(("chat_tool", DraftRig.Idea, "valid", DraftRig.Owner, DraftRig.Yaml));
        rig.Strategies_AreUntouched();
        rig.Store.Decisions.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_refused_text_comes_back_with_its_problems_so_the_model_can_repair_and_is_stored_invalid()
    {
        var rig = new DraftRig { Check = () => new Application.Abstractions.Lab.StrategyCheck(null, null, [new Application.Abstractions.Lab.StrategyIssue("trigger", "strategy.bad", "bad", "error", 1, 1, 1, 2, 0, 1, null)]) };

        var text = await Tool(rig).RunAsync(DraftRig.Json(), Chat(), Ct);

        using var document = JsonDocument.Parse(text);
        var row = document.RootElement.GetProperty("rows")[0];
        row.GetProperty("status").GetString().ShouldBe("invalid");
        row.GetProperty("problems")[0].GetProperty("code").GetString().ShouldBe("strategy.bad");
        rig.Store.Drafts[1].Status.ShouldBe("invalid");
    }

    [Fact]
    public async Task An_idea_argument_or_any_field_outside_the_schema_is_refused_and_nothing_is_stored()
    {
        var rig = new DraftRig();

        var text = await Tool(rig).RunAsync("""{"idea":"buy everything","yaml":"id: X"}""", Chat(), Ct);

        JsonDocument.Parse(text).RootElement.GetProperty("error").GetProperty("code").GetString().ShouldBe("draft.answer_shape");
        rig.Store.Drafts.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_day_limit_holds()
    {
        var rig = new DraftRig();
        rig.Store.TodayCount = rig.Options.MaxPerDay;

        var text = await Tool(rig).RunAsync(DraftRig.Json(), Chat(), Ct);

        JsonDocument.Parse(text).RootElement.GetProperty("error").GetProperty("code").GetString().ShouldBe("assistant.limit");
        rig.Store.Drafts.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true, DraftRig.Owner, TurnRequest.Chat, true, true)]
    [InlineData(false, DraftRig.Owner, TurnRequest.Chat, true, false)]
    [InlineData(true, DraftRig.Owner, TurnRequest.Chat, false, false)]
    [InlineData(true, DraftRig.Owner, TurnRequest.Eval, true, false)]
    [InlineData(true, DraftRig.Owner, TurnRequest.Job, true, false)]
    [InlineData(true, "service:hermes", TurnRequest.Chat, true, false)]
    [InlineData(true, "cli:xd", TurnRequest.Chat, true, false)]
    public void It_is_offered_only_to_the_owners_chat_turn_while_drafting_and_the_assistant_are_on(bool drafting, string actor, string origin, bool assistant, bool offered)
    {
        var rig = new DraftRig();
        rig.Options.Enabled = drafting;

        Tool(rig).OfferedTo(Chat(actor, origin), AssistantSettings.Defaults with { Enabled = assistant }).ShouldBe(offered);
    }
}
