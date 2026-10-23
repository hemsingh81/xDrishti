using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>Spec P6-08 on a real database: a cell move is a row, a revert is another row, the state is the replay, the endpoints read it, and a champion designation writes its changelog row in the same transaction.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class LearningChangelogApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private const string Cell = "ORB|15m|BUY|intraday|FIXED_RR|*";

    private static readonly string SetId = new('a', 64);

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "DELETE FROM research.learning_changelog");
        await ExecuteAsync(connection, "DELETE FROM research.model_designations");
        await ExecuteAsync(connection, "DELETE FROM research.model_versions");
        await ExecuteAsync(connection, "DELETE FROM research.training_sets WHERE set_id = @id", ("id", SetId));
        await ExecuteAsync(connection, """
            INSERT INTO research.training_sets (set_id, label_set_id, rule_version, feature_version, basis, sources, required_features, half_life_days, prior_strength, min_trades, credible_level, from_date, to_date, labelled, row_count,
                positives, negatives, unresolved, no_target, dropped_no_features, dropped_unknown, created_at, created_by)
            VALUES (@id, @id, 'tb-1', 'tf-1', 'Gross', 's', 'cell_n', 180, 20, 30, 0.9, '2026-01-01', '2026-06-01', 10, 10, 6, 4, 0, 0, 0, 0, now(), 'test')
            """, ("id", SetId));
    }

    private async Task<Result<ChangelogOutcome>> MoveAsync(CellState to, TransitionFacts? facts = null)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<RecordCellTransitionCommand, ChangelogOutcome>>()
            .HandleAsync(new RecordCellTransitionCommand(Cell, to, "test reason", "test", facts ?? new TransitionFacts(true, true, true, true, 20), ["run:abc"]), Ct);
    }

    private async Task<Result<ChangelogOutcome>> RevertAsync(long id)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<RevertChangelogCommand, ChangelogOutcome>>().HandleAsync(new RevertChangelogCommand(id, "undo", "test"), Ct);
    }

    private async Task<JsonElement> StateAsync()
    {
        using var response = await Client.GetAsync(new Uri("/api/changelog/cell-state?cell=" + Uri.EscapeDataString(Cell), UriKind.Relative), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    [Fact]
    public async Task A_cell_with_no_rows_is_in_research_and_the_changelog_is_empty()
    {
        using var list = await Client.GetAsync(new Uri("/api/changelog", UriKind.Relative), Ct);

        (await list.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetArrayLength().ShouldBe(0);
        (await StateAsync()).GetProperty("state").GetString().ShouldBe("research");
    }

    [Fact]
    public async Task Moves_and_a_revert_are_rows_and_the_state_is_their_replay()
    {
        var candidate = await MoveAsync(CellState.Candidate);
        var paper = await MoveAsync(CellState.Paper);
        var revert = await RevertAsync(paper.Value.Id);

        revert.IsSuccess.ShouldBeTrue();
        (await RevertAsync(paper.Value.Id)).Error.Code.ShouldBe(ChangelogErrors.NotRevertibleCode);
        (await RevertAsync(candidate.Value.Id)).Error.Code.ShouldBe(ChangelogErrors.NotRevertibleCode);
        (await MoveAsync(CellState.Active)).Error.Code.ShouldBe(ChangelogErrors.TransitionRefusedCode);
        var state = await StateAsync();
        state.GetProperty("state").GetString().ShouldBe("candidate");
        state.GetProperty("rows").GetInt32().ShouldBe(3);
        state.GetProperty("problems").GetArrayLength().ShouldBe(0);
        using var list = await Client.GetAsync(new Uri("/api/changelog?subjectKind=cell&subject=" + Uri.EscapeDataString(Cell), UriKind.Relative), Ct);
        var rows = await list.Content.ReadFromJsonAsync<JsonElement>(Ct);
        rows.EnumerateArray().Select(r => r.GetProperty("kind").GetString()).ShouldBe(["revert", "transition", "transition"]);
        rows[0].GetProperty("revertsId").GetInt64().ShouldBe(paper.Value.Id);
        rows[0].GetProperty("evidenceIds")[0].GetString().ShouldBe($"changelog:{paper.Value.Id}");
        using var one = await Client.GetAsync(new Uri("/api/changelog/" + candidate.Value.Id, UriKind.Relative), Ct);
        (await one.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("actor").GetString().ShouldBe("test");
    }

    [Fact]
    public async Task The_endpoints_refuse_bad_input_and_an_unknown_row()
    {
        using var kind = await Client.GetAsync(new Uri("/api/changelog?subjectKind=flower", UriKind.Relative), Ct);
        using var limit = await Client.GetAsync(new Uri("/api/changelog?limit=500", UriKind.Relative), Ct);
        using var cell = await Client.GetAsync(new Uri("/api/changelog/cell-state?cell=x", UriKind.Relative), Ct);
        using var missing = await Client.GetAsync(new Uri("/api/changelog/12345", UriKind.Relative), Ct);

        kind.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        limit.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        cell.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_port_refuses_a_row_that_does_not_follow_and_a_revert_of_a_row_that_is_no_longer_the_latest()
    {
        var candidate = await MoveAsync(CellState.Candidate);
        using var scope = Factory.Services.CreateScope();
        var log = scope.ServiceProvider.GetRequiredService<ILearningChangelog>();
        var at = DateTimeOffset.UtcNow;

        var wrongFrom = await log.AppendAsync(new NewChangelogEntry("transition", "cell", Cell, "paper", "active", "r", "t", ["e"], null, null, null, null, at), Ct);
        var revert = new NewChangelogEntry("revert", "cell", Cell, "candidate", "research", "r", "t", ["e"], null, null, null, candidate.Value.Id, at);
        var first = await log.AppendAsync(revert, Ct);
        var again = await log.AppendAsync(revert with { FromState = "research", ToState = "candidate" }, Ct);

        wrongFrom.Stale.ShouldBeTrue();
        first.Stale.ShouldBeFalse();
        again.Stale.ShouldBeTrue("the reverted row is no longer the latest");
    }

    [Fact]
    public async Task A_champion_designation_writes_its_changelog_row_and_a_replaced_champion_gets_one_too()
    {
        var first = await RegisterAsync('d', 'a');
        var second = await RegisterAsync('f', 'b', trials: 2);

        (await DesignateAsync(first, ModelRole.Champion)).IsSuccess.ShouldBeTrue();
        (await DesignateAsync(first, ModelRole.Champion)).Value.Changed.ShouldBeFalse();
        (await DesignateAsync(second, ModelRole.Champion)).IsSuccess.ShouldBeTrue();

        using var list = await Client.GetAsync(new Uri("/api/changelog?subjectKind=model", UriKind.Relative), Ct);
        var rows = (await list.Content.ReadFromJsonAsync<JsonElement>(Ct)).EnumerateArray().ToList();
        rows.Select(r => $"{r.GetProperty("subject").GetString()} {r.GetProperty("fromState").GetString()}>{r.GetProperty("toState").GetString()}").ShouldBe(
        [
            $"model:{second} challenger>champion",
            $"model:{first} champion>retired",
            $"model:{first} challenger>champion",
        ]);
        rows[0].GetProperty("beforeRef").GetString().ShouldBe($"model:{first}");
        rows[0].GetProperty("kind").GetString().ShouldBe("designation");
        (await RevertAsync(rows[0].GetProperty("id").GetInt64())).Error.Code.ShouldBe(ChangelogErrors.NotRevertibleCode);
    }

    [Fact]
    public async Task A_refused_champion_writes_no_changelog_row()
    {
        var weak = await RegisterAsync('c', 'c', ValidationOutcome.Inconclusive);

        var refused = await DesignateAsync(weak, ModelRole.Champion);

        refused.Error.Code.ShouldBe(ModelErrors.NotEligibleCode);
        using var list = await Client.GetAsync(new Uri("/api/changelog", UriKind.Relative), Ct);
        (await list.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetArrayLength().ShouldBe(0);
    }

    private async Task<long> RegisterAsync(char key, char sha, ValidationOutcome verdict = ValidationOutcome.Eligible, int trials = 1)
    {
        var model = new ModelToRegister(new string(key, 64), new string(sha, 64), 100, "FastTree", null, SetId, "tf-1", 3, 30, 10, 40, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1), verdict, 6, 5, 4, true, 3.5, 0.65, 0.23,
            verdict == ValidationOutcome.Eligible ? string.Empty : "does not beat the baselines", "report text", "params", [-1.0, 0.0, 1.0], [0.3, 0.5, 0.7], [0.25, 0.25, 0.25], trials, 4, DateTimeOffset.UtcNow, "test");
        using var scope = Factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<IModelRegistry>().RegisterAsync(model, Ct)).Id;
    }

    private async Task<Result<DesignationOutcome>> DesignateAsync(long id, ModelRole role)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<DesignateModelCommand, DesignationOutcome>>().HandleAsync(new DesignateModelCommand(id, role, "test reason", "test"), Ct);
    }
}
