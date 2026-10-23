using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>Spec P6-10 on a real database: the log is insert-only, the open limit holds under a lock, the whole path propose to judged runs through the real experiment and the one-variable check, and the two endpoints read the log.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class HypothesisLogApiTests(ApiFactory factory) : DataTestBase(factory)
{
    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "DELETE FROM research.hypotheses");
        await ExecuteAsync(connection, "DELETE FROM research.experiments");
    }

    private async Task<(string Base, string Cand)> StoreRunsAsync()
    {
        var to = RunStoreRoundTripTests.NextTo();
        var a = StoredRunFixtures.Run(to: to);
        var b = StoredRunFixtures.Run(to: to, fills: XDrishti.Domain.Research.FillRules.Create(0.1, XDrishti.Domain.Research.SlippageConfig.Create(2, 0.05, 2).Value).Value);
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ICommandHandler<StoreRunCommand, XDrishti.Application.Abstractions.Research.StoreOutcome>>();
        foreach (var run in new[] { a, b })
        {
            (await store.HandleAsync(new StoreRunCommand(run, StoreKit.Record(run), SimulateStore.Actor), Ct)).IsSuccess.ShouldBeTrue();
        }

        return (a.RunKey, b.RunKey);
    }

    private async Task<Result<HypothesisMoveOutcome>> SendAsync<TCommand>(TCommand command)
        where TCommand : ICommand
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<TCommand, HypothesisMoveOutcome>>().HandleAsync(command, Ct);
    }

    private static ProposeHypothesisCommand Slippage(string baseKey) =>
        new(HypothesisSource.Owner, null, new ExperimentKnob("slippage", ExperimentKnobKind.Slippage, "1", "2"), "fills may be dearer than modelled", baseKey, new HypothesisGrid(), "cli:xd");

    [Fact]
    public async Task Before_any_hypothesis_the_list_is_empty_and_one_is_not_found()
    {
        using var list = await Client.GetAsync(new Uri("/api/hypotheses", UriKind.Relative), Ct);
        using var one = await Client.GetAsync(new Uri("/api/hypotheses/1", UriKind.Relative), Ct);
        using var bad = await Client.GetAsync(new Uri("/api/hypotheses?state=nope", UriKind.Relative), Ct);

        (await list.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetArrayLength().ShouldBe(0);
        one.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        bad.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_hypothesis_walks_from_proposed_to_judged_through_the_real_experiment_and_is_read_back()
    {
        var (b, c) = await StoreRunsAsync();

        var proposed = await SendAsync(Slippage(b));
        var id = proposed.Value.Id;
        var approved = await SendAsync(new MoveHypothesisCommand(id, HypothesisState.Approved, "worth a look", "cli:xd"));
        var started = await SendAsync(new StartHypothesisCommand(id, c, "cli:xd"));
        var judged = await SendAsync(new JudgeHypothesisCommand(id, new ExperimentOptions(), 0, "cli:xd"));

        new[] { proposed, approved, started, judged }.ShouldAllBe(r => r.IsSuccess);
        using var one = await Client.GetAsync(new Uri($"/api/hypotheses/{id}", UriKind.Relative), Ct);
        var detail = await one.Content.ReadFromJsonAsync<JsonElement>(Ct);
        detail.GetProperty("state").GetString().ShouldBe("judged");
        detail.GetProperty("candidateRunKey").GetString().ShouldBe(c);
        detail.GetProperty("experimentId").GetInt64().ShouldBeGreaterThan(0);
        detail.GetProperty("history").EnumerateArray().Select(h => h.GetProperty("state").GetString()).ShouldBe(["proposed", "approved", "running", "judged"]);
        using var experiment = await Client.GetAsync(new Uri($"/api/experiments/{detail.GetProperty("experimentId").GetInt64()}", UriKind.Relative), Ct);
        experiment.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var list = await Client.GetAsync(new Uri("/api/hypotheses?state=judged", UriKind.Relative), Ct);
        (await list.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task A_judged_hypothesis_without_a_Better_experiment_cannot_be_accepted_and_can_be_rejected()
    {
        var (b, c) = await StoreRunsAsync();
        var id = (await SendAsync(Slippage(b))).Value.Id;
        await SendAsync(new MoveHypothesisCommand(id, HypothesisState.Approved, "go", "cli:xd"));
        await SendAsync(new StartHypothesisCommand(id, c, "cli:xd"));
        await SendAsync(new JudgeHypothesisCommand(id, new ExperimentOptions(), 0, "cli:xd"));

        var accepted = await SendAsync(new MoveHypothesisCommand(id, HypothesisState.Accepted, "take", "cli:xd"));
        var rejected = await SendAsync(new MoveHypothesisCommand(id, HypothesisState.Rejected, "not better", "cli:xd"));

        accepted.Error.Code.ShouldBe(HypothesisErrors.MoveRefusedCode);
        rejected.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_candidate_with_nothing_changed_is_refused_at_start_and_a_run_that_is_not_stored_cannot_be_proposed()
    {
        var (b, _) = await StoreRunsAsync();
        var id = (await SendAsync(Slippage(b))).Value.Id;
        await SendAsync(new MoveHypothesisCommand(id, HypothesisState.Approved, "go", "cli:xd"));

        var same = await SendAsync(new StartHypothesisCommand(id, b, "cli:xd"));
        var missing = await SendAsync(Slippage(new string('f', 64)));

        same.Error.Code.ShouldBe(HypothesisErrors.NotOneVariableCode);
        missing.Error.Code.ShouldBe(LearningErrors.RunNotFoundCode);
    }

    [Fact]
    public async Task A_state_row_cannot_point_at_another_state_row_and_only_a_proposing_row_is_a_hypothesis()
    {
        var (b, _) = await StoreRunsAsync();
        var id = (await SendAsync(Slippage(b))).Value.Id;
        await SendAsync(new MoveHypothesisCommand(id, HypothesisState.Approved, "go", "cli:xd"));
        await using var connection = await OpenAsync();
        var stateRow = await new Npgsql.NpgsqlCommand($"SELECT max(id) FROM research.hypotheses WHERE hypothesis_id = {id}", connection).ExecuteScalarAsync(Ct);
        var insert = $"INSERT INTO research.hypotheses (hypothesis_id, state, from_state, source, knob_name, knob_kind, baseline_value, candidate_value, rationale, baseline_run_key, reason, actor, created_at) VALUES ({stateRow}, 'rejected', 'approved', 'owner', 'k', 'Slippage', '1', '2', 'r', '{b}', 'r', 'a', now())";

        var ex = await Should.ThrowAsync<Npgsql.PostgresException>(async () => await new Npgsql.NpgsqlCommand(insert, connection).ExecuteNonQueryAsync(Ct));
        using var notSeed = await Client.GetAsync(new Uri($"/api/hypotheses/{stateRow}", UriKind.Relative), Ct);
        using var numeric = await Client.GetAsync(new Uri("/api/hypotheses?state=1", UriKind.Relative), Ct);

        ex.SqlState.ShouldBe("23503");
        notSeed.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        numeric.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task At_most_three_stay_open_even_when_proposed_at_once()
    {
        var (b, _) = await StoreRunsAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => SendAsync(Slippage(b))));

        results.Count(r => r.IsSuccess).ShouldBe(3);
        results.Where(r => r.IsFailure).ShouldAllBe(r => r.Error.Code == HypothesisErrors.OpenLimitCode);
    }

    [Fact]
    public async Task Two_moves_at_once_write_one_row_and_the_other_is_stale_or_refused()
    {
        var (b, _) = await StoreRunsAsync();
        var id = (await SendAsync(Slippage(b))).Value.Id;

        var both = await Task.WhenAll(SendAsync(new MoveHypothesisCommand(id, HypothesisState.Approved, "a", "cli:xd")), SendAsync(new MoveHypothesisCommand(id, HypothesisState.Rejected, "r", "cli:xd")));

        both.Count(r => r.IsSuccess).ShouldBe(1);
        using var one = await Client.GetAsync(new Uri($"/api/hypotheses/{id}", UriKind.Relative), Ct);
        (await one.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("history").GetArrayLength().ShouldBe(2);
    }
}
