using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Api.IntegrationTests.Learning;

/// <summary>
/// BK-11b on a real database (ADR 0266): the owner act that proposes one hypothesis from a stable tier observation (capability off, password on every request, a non-observation refused with the reason, a duplicate refused, the row has source universe and kind Scope,
/// one account event, nothing applied), the auto option row of the universe settings (OFF by default, a row only through the password), and that a scope hypothesis cannot be started.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class UniverseProposerApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private const string TierKey = "ORB|15m|BUY|intraday|FIXED_RR|*|liq_high";

    private const string ParentKey = "ORB|15m|BUY|intraday|FIXED_RR|*";

    private static readonly DateOnly Day = new(2026, 3, 10);

    private static readonly CellStatsOptions Options = new(RecencyHalfLifeDays: 30, PriorStrength: 20, MinTrades: 30);

    private static readonly CellId Orb = new("ORB", "15m", "BUY", "intraday", "FIXED_RR");

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        foreach (var table in new[] { "research.cell_stats", "research.lessons", "research.hypotheses", "research.universe_settings_changes" })
        {
            await ExecuteAsync(connection, "DELETE FROM " + table);
        }

        await ExecuteAsync(connection, "DELETE FROM platform.capability_changes WHERE capability = 'learning_moves_ui'");
        await ExecuteAsync(connection, "INSERT INTO platform.capability_changes (capability, enabled, reason, actor) VALUES ('learning_moves_ui', true, 'test', 'owner:test@example.test')");
    }

    private async Task<TResult> WithAsync<TResult>(Func<IServiceProvider, Task<TResult>> work)
    {
        using var scope = Factory.Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    private Task<Result<TOut>> SendAsync<TCommand, TOut>(TCommand command)
        where TCommand : ICommand =>
        WithAsync(p => p.GetRequiredService<ICommandHandler<TCommand, TOut>>().HandleAsync(command, Ct).AsTask());

    private async Task<string> StoreRunAsync()
    {
        var run = StoredRunFixtures.Run(to: RunStoreRoundTripTests.NextTo());
        (await SendAsync<StoreRunCommand, XDrishti.Application.Abstractions.Research.StoreOutcome>(new StoreRunCommand(run, StoreKit.Record(run), SimulateStore.Actor))).IsSuccess.ShouldBeTrue();
        return run.RunKey;
    }

    private async Task StoreCellsAsync(int childWins, int childLosses)
    {
        CellTrade T(DateOnly day, bool win) => new(day, win ? 1 : -1, 0, 1);
        var child = Enumerable.Range(0, childWins + childLosses).Select(i => T(Day.AddDays(-(i % 10)), i < childWins)).ToList();
        var rest = Enumerable.Range(0, 80).Select(i => T(Day.AddDays(-(i % 10)), i < 12)).ToList();
        var rows = CellStatsCalculator.Compute([new CellInput(Orb, false, [.. child, .. rest])], Day, Options, universeCells: [new CellInput(Orb with { Universe = "liq_high" }, false, child)]);
        using var scope = Factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<ICellStatsStore>().StoreAsync(new CellSnapshotToStore(new string('d', 64), Day, Options, "run:intraday:FIXED_RR", rows, "test", DateTimeOffset.UnixEpoch), Ct)).ShouldBeTrue();
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> ProposeAsync(string? password, string baseline, string key = TierKey)
    {
        using var response = await Client.PostAsJsonAsync(new Uri("/api/cells/universe-links/" + Uri.EscapeDataString(key) + "/propose", UriKind.Relative), new { baselineRunKey = baseline, reason = "looks real", password }, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private Task<long> HypothesesAsync() => ScalarAsync<long>("SELECT count(*) FROM research.hypotheses");

    [Fact]
    public async Task A_stable_observation_is_proposed_by_the_owner_act_as_one_universe_scope_hypothesis_with_one_account_event_and_nothing_applied()
    {
        var baseline = await StoreRunAsync();
        await StoreCellsAsync(88, 32);   // 88 of 120 in a parent of 200: stable in both halves
        var cellsBefore = await ScalarAsync<long>("SELECT count(*) FROM research.cell_stats");

        var wrong = await ProposeAsync("not-the-password", baseline);
        var missing = await ProposeAsync(null, baseline);
        var made = await ProposeAsync(Factory.OwnerPassword, baseline);
        var again = await ProposeAsync(Factory.OwnerPassword, baseline);

        wrong.Status.ShouldBe(HttpStatusCode.Unauthorized);
        missing.Status.ShouldBe(HttpStatusCode.Unauthorized);
        made.Status.ShouldBe(HttpStatusCode.OK, made.Body.ToString());
        made.Body.GetProperty("state").GetString().ShouldBe("proposed");
        again.Status.ShouldBe(HttpStatusCode.Conflict);
        again.Body.GetProperty("code").GetString().ShouldBe("learning.hypothesis_exists");
        (await HypothesesAsync()).ShouldBe(1);
        (await ScalarAsync<string>("SELECT source || '|' || knob_kind || '|' || knob_name || '|' || cell_key || '|' || baseline_value || '|' || candidate_value || '|' || state FROM research.hypotheses")).ShouldBe($"universe|Scope|scan_scope|{ParentKey}|*|liq_high|proposed");
        (await ScalarAsync<string>("SELECT actor FROM research.hypotheses")).ShouldStartWith("owner:");
        (await ScalarAsync<long>("SELECT count(*) FROM accounts.account_events WHERE event = 'HypothesisProposed'")).ShouldBe(1);
        (await ScalarAsync<string>("SELECT detail FROM accounts.account_events WHERE event = 'HypothesisProposed'")).ShouldBe($"hypothesis={await ScalarAsync<long>("SELECT max(id) FROM research.hypotheses")} source=universe");
        (await ScalarAsync<long>("SELECT count(*) FROM research.cell_stats")).ShouldBe(cellsBefore, "nothing is applied to a cell");
    }

    [Fact]
    public async Task A_thin_tier_is_refused_with_the_reason_and_a_scope_hypothesis_cannot_be_started()
    {
        var baseline = await StoreRunAsync();
        await StoreCellsAsync(22, 8);

        var thin = await ProposeAsync(Factory.OwnerPassword, baseline);

        thin.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        thin.Body.GetProperty("code").GetString().ShouldBe("learning.not_an_observation");
        (await HypothesesAsync()).ShouldBe(0);

        // the command-line route writes the same row, and starting it is refused whatever the candidate
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "DELETE FROM research.cell_stats");
        }

        await StoreCellsAsync(88, 32);
        var proposed = await SendAsync<ProposeFromUniverseLinkCommand, HypothesisMoveOutcome>(new ProposeFromUniverseLinkCommand(TierKey, baseline, "from the verb", SimulateStore.Actor));
        proposed.IsSuccess.ShouldBeTrue(proposed.IsFailure ? proposed.Error.Message : string.Empty);
        var started = await SendAsync<StartHypothesisCommand, HypothesisMoveOutcome>(new StartHypothesisCommand(proposed.Value.Id, new string('b', 64), SimulateStore.Actor));
        started.Error.Code.ShouldBe("learning.knob_not_runnable");
        (await ScalarAsync<string>("SELECT state FROM research.hypotheses WHERE id = " + proposed.Value.Id)).ShouldBe("proposed");
    }

    [Fact]
    public async Task The_auto_option_is_off_by_default_and_changes_only_through_the_password_as_one_row_of_the_universe_settings()
    {
        var read = await Client.GetFromJsonAsync<JsonElement>(new Uri("/api/universe/settings", UriKind.Relative), Ct);
        using var wrong = await Client.PutAsJsonAsync(new Uri("/api/universe/settings", UriKind.Relative), new { autoPropose = true, password = "nope", expectedVersion = 0 }, Ct);
        using var right = await Client.PutAsJsonAsync(new Uri("/api/universe/settings", UriKind.Relative), new { autoPropose = true, password = Factory.OwnerPassword, expectedVersion = 0 }, Ct);
        var after = await right.Content.ReadFromJsonAsync<JsonElement>(Ct);

        read.GetProperty("autoPropose").GetBoolean().ShouldBeFalse();
        wrong.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        right.StatusCode.ShouldBe(HttpStatusCode.OK);
        after.GetProperty("autoPropose").GetBoolean().ShouldBeTrue();
        (await ScalarAsync<string>("SELECT key || '=' || new_value || ' by ' || changed_by FROM research.universe_settings_changes")).ShouldStartWith("learning.universe_link.auto_propose=true by owner:");
    }

    [Fact]
    public async Task With_the_auto_option_on_the_cells_command_proposes_as_auto_and_with_it_off_it_proposes_nothing()
    {
        var baseline = await StoreRunAsync();
        await StoreCellsAsync(88, 32);
        var command = new AutoProposeUniverseLinksCommand([new CellSource(baseline, "intraday", "FIXED_RR")]);

        var off = await SendAsync<AutoProposeUniverseLinksCommand, AutoProposeOutcome>(command);
        using (var on = await Client.PutAsJsonAsync(new Uri("/api/universe/settings", UriKind.Relative), new { autoPropose = true, password = Factory.OwnerPassword, expectedVersion = 0 }, Ct))
        {
            on.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var proposed = await SendAsync<AutoProposeUniverseLinksCommand, AutoProposeOutcome>(command);

        off.Value.ShouldBe(new AutoProposeOutcome(false, 0, 0, 0, false));
        proposed.Value.Proposed.ShouldBe(1);
        (await ScalarAsync<string>("SELECT actor || '|' || source FROM research.hypotheses")).ShouldBe("auto|universe");
        (await ScalarAsync<string>("SELECT rationale FROM research.hypotheses")).ShouldContain("auto: learning.universe_link.auto_propose ON (change ");
    }
}
