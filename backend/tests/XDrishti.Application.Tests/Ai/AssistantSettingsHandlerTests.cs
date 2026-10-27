using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Ai;
using XDrishti.Domain.Assistant;

namespace XDrishti.Application.Tests.Ai;

/// <summary>Spec P9-03 AC 8 and 9 at the handler: a raising change is refused without the verified password, a reducing one is not; stale is a conflict; the status reads the settings and does not ask the tool server while the assistant is off.</summary>
public sealed class AssistantSettingsHandlerTests
{
    private const string Owner = "owner:owner@example.invalid";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (ChangeAssistantSettingsHandler Handler, Store Store) Build(AssistantSettings current, AssistantSettings? appended)
    {
        var store = new Store(current, appended);
        return (new ChangeAssistantSettingsHandler(store, new StatusStub(), NullLogger<ChangeAssistantSettingsHandler>.Instance), store);
    }

    private sealed class Store(AssistantSettings current, AssistantSettings? appended) : IAssistantSettingsStore
    {
        public List<(AssistantSettingsChange Change, long? Expected)> Appends { get; } = [];

        public ValueTask<AssistantSettings> CurrentAsync(CancellationToken cancellationToken) => ValueTask.FromResult(current);

        public ValueTask<AssistantSettings?> AppendAsync(AssistantSettingsChange change, long? expectedCurrentId, CancellationToken cancellationToken)
        {
            Appends.Add((change, expectedCurrentId));
            return ValueTask.FromResult(appended);
        }
    }

    private sealed class StatusStub : IQueryHandler<GetAssistantStatusQuery, AssistantStatusView>
    {
        public ValueTask<AssistantStatusView> HandleAsync(GetAssistantStatusQuery query, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new AssistantStatusView(true, false, 6, 8, new LlmStatusView("down", null, null, null, null, DateTimeOffset.UtcNow, "x", "y"), new ToolsStatusView("ready", 4, 100), false, false, new EvaluationView("not_yet_evaluated", null, null)));
    }

    private sealed class LlmStub(string state) : IQueryHandler<GetLlmStatusQuery, LlmStatusView>
    {
        public ValueTask<LlmStatusView> HandleAsync(GetLlmStatusQuery query, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new LlmStatusView(state, "xd-main", "m.gguf", null, "b1", DateTimeOffset.UtcNow, "x", "scripts/xd-ai.sh start"));
    }

    private sealed class Tools(ToolCatalogSnapshot snapshot) : IToolCatalog
    {
        public int Listed { get; private set; }

        public ValueTask<ToolCatalogSnapshot> ListAsync(CancellationToken cancellationToken)
        {
            Listed++;
            return ValueTask.FromResult(snapshot);
        }

        public ValueTask<ToolInvocation> CallAsync(string name, string argumentsJson, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Nightly(bool running) : INightlyRunProbe
    {
        public ValueTask<bool> IsRunningAsync(TimeSpan window, CancellationToken cancellationToken) => ValueTask.FromResult(running);
    }

    [Fact]
    public async Task Turning_it_on_without_a_verified_password_is_refused_and_writes_nothing()
    {
        var (handler, store) = Build(AssistantSettings.Defaults, new AssistantSettings(8, true, false, 6));

        var result = await handler.HandleAsync(new ChangeAssistantSettingsCommand(Owner, "why", true, false, 6, 0, PasswordVerified: false), Ct);

        result.Error.Code.ShouldBe("auth.invalid_credentials");
        store.Appends.ShouldBeEmpty();
    }

    [Fact]
    public async Task Turning_it_on_with_a_verified_password_appends_one_row_and_answers_the_status()
    {
        var (handler, store) = Build(AssistantSettings.Defaults, new AssistantSettings(8, true, false, 6));

        var result = await handler.HandleAsync(new ChangeAssistantSettingsCommand(Owner, "why", true, false, 6, 0, PasswordVerified: true), Ct);

        result.IsSuccess.ShouldBeTrue();
        var append = store.Appends.ShouldHaveSingleItem();
        (append.Change.Actor, append.Change.Enabled, append.Change.Reason, append.Expected).ShouldBe((Owner, true, "why", null));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task The_proposal_switches_need_the_password_to_turn_on_and_not_to_turn_off(int which)
    {
        var (handler, store) = Build(AssistantSettings.Defaults, new AssistantSettings(8, true, false, 6));
        ChangeAssistantSettingsCommand On(bool password) => new(Owner, "why", true, false, 6, 0, PasswordVerified: password, AllowProposalAccept: which == 2 ? true : null, AutoAcceptHypothesis: which == 3 ? true : null);

        var refused = await handler.HandleAsync(On(false), Ct);
        var allowed = await handler.HandleAsync(On(true), Ct);

        refused.Error.Code.ShouldBe("auth.invalid_credentials");
        allowed.IsSuccess.ShouldBeTrue();
        store.Appends.ShouldHaveSingleItem();
        var (handler2, store2) = Build(new AssistantSettings(8, true, false, 6, AutoLabQuickTest: true, AllowProposalAccept: true, AutoAcceptHypothesis: true), new AssistantSettings(9, true, false, 6));
        (await handler2.HandleAsync(new ChangeAssistantSettingsCommand(Owner, "off", true, false, 6, null, PasswordVerified: false, AutoLabQuickTest: false, AllowProposalAccept: false, AutoAcceptHypothesis: false), Ct)).IsSuccess.ShouldBeTrue();
        store2.Appends.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task The_auto_quick_test_switch_can_be_turned_on_only_with_a_verified_password_and_off_without()
    {
        var (handler, store) = Build(AssistantSettings.Defaults, new AssistantSettings(8, true, false, 6, AutoLabQuickTest: true));
        var on = new ChangeAssistantSettingsCommand(Owner, "why", false, false, 6, 0, PasswordVerified: false, AutoLabQuickTest: true);

        (await handler.HandleAsync(on, Ct)).Error.Code.ShouldBe("auth.invalid_credentials");
        store.Appends.ShouldBeEmpty();
        (await handler.HandleAsync(on with { PasswordVerified = true }, Ct)).IsSuccess.ShouldBeTrue();
        store.Appends.ShouldHaveSingleItem().Change.AutoLabQuickTest.ShouldBeTrue();
        var (off, offStore) = Build(new AssistantSettings(8, true, false, 6, AutoLabQuickTest: true), new AssistantSettings(9, true, false, 6));
        (await off.HandleAsync(new ChangeAssistantSettingsCommand(Owner, "off", true, false, 6, null, PasswordVerified: false, AutoLabQuickTest: false), Ct)).IsSuccess.ShouldBeTrue();
        offStore.Appends.ShouldHaveSingleItem().Change.AutoLabQuickTest.ShouldBeFalse();
    }

    [Fact]
    public async Task Switching_it_off_needs_no_password()
    {
        var (handler, store) = Build(new AssistantSettings(8, true, true, 6), new AssistantSettings(9, false, false, 6));

        var result = await handler.HandleAsync(new ChangeAssistantSettingsCommand(Owner, "off", false, false, 6, 8, PasswordVerified: false), Ct);

        result.IsSuccess.ShouldBeTrue();
        store.Appends.Single().Expected.ShouldBe(8);
    }

    [Fact]
    public async Task A_stale_form_is_a_conflict_before_anything_is_written()
    {
        var (handler, store) = Build(new AssistantSettings(8, true, false, 6), null);

        var result = await handler.HandleAsync(new ChangeAssistantSettingsCommand(Owner, "off", false, false, 6, 5, PasswordVerified: false), Ct);

        result.Error.Code.ShouldBe("assistant.stale");
        store.Appends.ShouldBeEmpty();
    }

    [Fact]
    public async Task When_another_change_wins_the_race_the_append_says_stale()
    {
        var (handler, _) = Build(new AssistantSettings(8, true, false, 6), null);

        var result = await handler.HandleAsync(new ChangeAssistantSettingsCommand(Owner, "off", false, false, 6, null, PasswordVerified: false), Ct);

        result.Error.Code.ShouldBe("assistant.stale");
    }

    [Fact]
    public async Task A_service_actor_is_refused_by_the_domain()
    {
        var (handler, _) = Build(AssistantSettings.Defaults, null);

        var result = await handler.HandleAsync(new ChangeAssistantSettingsCommand("service:eval", "why", true, false, 6, null, PasswordVerified: true), Ct);

        result.Error.Code.ShouldBe("assistant.owner_only");
    }

    [Fact]
    public async Task The_status_does_not_ask_the_tool_server_while_the_assistant_is_off()
    {
        var tools = new Tools(ToolCatalogSnapshot.Unavailable(ToolCatalogState.Down));
        var handler = new GetAssistantStatusHandler(new Store(AssistantSettings.Defaults, null), new LlmStub("down"), tools, new Nightly(false), new TurnGate(), new TurnOptions());

        var view = await handler.HandleAsync(new GetAssistantStatusQuery(), Ct);

        (view.Enabled, view.PortfolioContext, view.MaxToolCalls, view.SettingsId).ShouldBe((false, false, 6, null));
        view.Tools.ShouldBe(new ToolsStatusView("disabled", 0, 0));
        view.Evaluation.State.ShouldBe("not_yet_evaluated");
        tools.Listed.ShouldBe(0);
    }

    [Fact]
    public async Task With_the_assistant_on_the_status_lists_the_tools_and_their_schema_estimate()
    {
        var tools = new Tools(new ToolCatalogSnapshot(ToolCatalogState.Ready, [new ToolDescriptor("list_tables", new string('d', 35), new string('s', 70))]));
        var handler = new GetAssistantStatusHandler(new Store(new AssistantSettings(4, true, true, 8), null), new LlmStub("ready"), tools, new Nightly(true), new TurnGate(), new TurnOptions());

        var view = await handler.HandleAsync(new GetAssistantStatusQuery(), Ct);

        (view.Enabled, view.PortfolioContext, view.MaxToolCalls, view.SettingsId, view.NightlyRunning, view.Busy).ShouldBe((true, true, 8, 4, true, false));
        view.Tools.State.ShouldBe("ready");
        view.Tools.Count.ShouldBe(1);
        view.Tools.SchemaTokensEstimate.ShouldBeGreaterThan(0);
        view.Llm.State.ShouldBe("ready");
    }
}
