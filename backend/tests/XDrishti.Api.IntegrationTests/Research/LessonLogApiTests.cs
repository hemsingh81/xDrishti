using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>Spec P6-16 on a real database: the log is insert-only, an opening is idempotent under concurrency, a lesson walks open, linked, closed through a real hypothesis and experiment and its before and after are the stored ones, and the endpoints read the log.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class LessonLogApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private const string Cell = "ORB|15m|BUY|intraday|FIXED_RR|*";

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "DELETE FROM research.lessons");
        await ExecuteAsync(connection, "DELETE FROM research.hypotheses");
        await ExecuteAsync(connection, "DELETE FROM research.experiments");
    }

    private static NewLessonRow Opening(string evidence = "GaveBackProfit:2/10->6/10") =>
        new(null, "open", null, "growing_tag", Cell, evidence, "gave back profit grew", null, null, null, null, null, null, null, null, "found by the replayed monitors", "cli:xd", DateTimeOffset.UnixEpoch);

    private async Task<TResult> WithAsync<TResult>(Func<IServiceProvider, Task<TResult>> work)
    {
        using var scope = Factory.Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    private Task<Result<LessonMoveOutcome>> SendAsync<TCommand>(TCommand command)
        where TCommand : ICommand =>
        WithAsync(p => p.GetRequiredService<ICommandHandler<TCommand, LessonMoveOutcome>>().HandleAsync(command, Ct).AsTask());

    private async Task<long> HypothesisJudgedAsync()
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

        var p = scope.ServiceProvider;
        var proposed = await p.GetRequiredService<ICommandHandler<ProposeHypothesisCommand, HypothesisMoveOutcome>>()
            .HandleAsync(new ProposeHypothesisCommand(HypothesisSource.Owner, null, new ExperimentKnob("slippage", ExperimentKnobKind.Slippage, "1", "2"), "fills may be dearer", a.RunKey, new HypothesisGrid(), "cli:xd"), Ct);
        var id = proposed.Value.Id;
        await p.GetRequiredService<ICommandHandler<MoveHypothesisCommand, HypothesisMoveOutcome>>().HandleAsync(new MoveHypothesisCommand(id, HypothesisState.Approved, "go", "cli:xd"), Ct);
        await p.GetRequiredService<ICommandHandler<StartHypothesisCommand, HypothesisMoveOutcome>>().HandleAsync(new StartHypothesisCommand(id, b.RunKey, "cli:xd"), Ct);
        (await p.GetRequiredService<ICommandHandler<JudgeHypothesisCommand, HypothesisMoveOutcome>>().HandleAsync(new JudgeHypothesisCommand(id, new ExperimentOptions(), 0, "cli:xd"), Ct)).IsSuccess.ShouldBeTrue();
        return id;
    }

    [Fact]
    public async Task Before_any_lesson_the_list_is_empty_and_one_is_not_found_and_a_bad_filter_is_refused()
    {
        using var list = await Client.GetAsync(new Uri("/api/lessons", UriKind.Relative), Ct);
        using var one = await Client.GetAsync(new Uri("/api/lessons/1", UriKind.Relative), Ct);
        using var bad = await Client.GetAsync(new Uri("/api/lessons?state=nope", UriKind.Relative), Ct);

        (await list.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetArrayLength().ShouldBe(0);
        one.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        bad.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task The_same_finding_opened_at_once_writes_one_lesson()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => WithAsync(p => p.GetRequiredService<ILessonLog>().OpenAsync(Opening(), Ct).AsTask())));
        var other = await WithAsync(p => p.GetRequiredService<ILessonLog>().OpenAsync(Opening("GaveBackProfit:3/10->7/10"), Ct).AsTask());

        results.Count(r => r.Status == LessonAppendStatus.Created).ShouldBe(1);
        results.Where(r => r.Status == LessonAppendStatus.Existing).Select(r => r.Id).Distinct().ShouldBe([results.Single(r => r.Status == LessonAppendStatus.Created).Id]);
        other.Status.ShouldBe(LessonAppendStatus.Created);
        using var list = await Client.GetAsync(new Uri("/api/lessons", UriKind.Relative), Ct);
        (await list.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task A_lesson_is_linked_to_a_judged_hypothesis_and_closed_with_the_numbers_of_the_stored_experiment()
    {
        var hypothesis = await HypothesisJudgedAsync();
        var lesson = (await WithAsync(p => p.GetRequiredService<ILessonLog>().OpenAsync(Opening(), Ct).AsTask())).Id;

        var linked = await SendAsync(new LinkLessonCommand(lesson, "hypothesis", hypothesis, "tried dearer fills", "cli:xd"));
        var closed = await SendAsync(new CloseLessonCommand(lesson, "read the stored experiment", "cli:xd"));

        linked.IsSuccess.ShouldBeTrue();
        closed.IsSuccess.ShouldBeTrue();
        using var one = await Client.GetAsync(new Uri($"/api/lessons/{lesson}", UriKind.Relative), Ct);
        var detail = await one.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var experimentId = detail.GetProperty("experimentId").GetInt64();
        using var experiment = await Client.GetAsync(new Uri($"/api/experiments/{experimentId}", UriKind.Relative), Ct);
        var stored = await experiment.Content.ReadFromJsonAsync<JsonElement>(Ct);
        detail.GetProperty("state").GetString().ShouldBe("closed");
        detail.GetProperty("hypothesisRef").GetInt64().ShouldBe(hypothesis);
        detail.GetProperty("experimentOutcome").GetString().ShouldBe(stored.GetProperty("outcome").GetString());
        detail.GetProperty("beforeTotalR").GetDouble().ShouldBe(stored.GetProperty("baselineTotalR").GetDouble());
        detail.GetProperty("afterTotalR").GetDouble().ShouldBe(stored.GetProperty("candidateTotalR").GetDouble());
        detail.GetProperty("result").GetString().ShouldBe(stored.GetProperty("outcome").GetString() == "Better" ? "improved" : "no_measurable_change");
        detail.GetProperty("history").EnumerateArray().Select(h => h.GetProperty("state").GetString()).ShouldBe(["open", "linked", "closed"]);
        detail.GetProperty("nextStates").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task A_lesson_linked_to_an_unjudged_hypothesis_is_not_closed_and_a_dismissed_one_is_final()
    {
        var lesson = (await WithAsync(p => p.GetRequiredService<ILessonLog>().OpenAsync(Opening(), Ct).AsTask())).Id;

        var missing = await SendAsync(new LinkLessonCommand(lesson, "hypothesis", 9999, "x", "cli:xd"));
        var closeOpen = await SendAsync(new CloseLessonCommand(lesson, "x", "cli:xd"));
        var dismissed = await SendAsync(new DismissLessonCommand(lesson, "not worth it", "cli:xd"));
        var again = await SendAsync(new DismissLessonCommand(lesson, "again", "cli:xd"));

        missing.Error.Code.ShouldBe(LessonErrors.FixNotFoundCode);
        closeOpen.Error.Code.ShouldBe(LessonErrors.MoveRefusedCode);
        dismissed.IsSuccess.ShouldBeTrue();
        again.Error.Code.ShouldBe(LessonErrors.MoveRefusedCode);
        using var list = await Client.GetAsync(new Uri("/api/lessons?state=dismissed", UriKind.Relative), Ct);
        (await list.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Two_moves_at_once_write_one_row()
    {
        var lesson = (await WithAsync(p => p.GetRequiredService<ILessonLog>().OpenAsync(Opening(), Ct).AsTask())).Id;

        var both = await Task.WhenAll(SendAsync(new DismissLessonCommand(lesson, "a", "cli:xd")), SendAsync(new DismissLessonCommand(lesson, "b", "cli:xd")));

        both.Count(r => r.IsSuccess).ShouldBe(1);
        using var one = await Client.GetAsync(new Uri($"/api/lessons/{lesson}", UriKind.Relative), Ct);
        (await one.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("history").GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task A_state_row_cannot_point_at_another_state_row_and_a_closed_row_without_numbers_is_refused_by_the_database()
    {
        var lesson = (await WithAsync(p => p.GetRequiredService<ILessonLog>().OpenAsync(Opening(), Ct).AsTask())).Id;
        await SendAsync(new DismissLessonCommand(lesson, "x", "cli:xd"));
        await using var connection = await OpenAsync();
        var stateRow = await new NpgsqlCommand($"SELECT max(id) FROM research.lessons WHERE lesson_id = {lesson}", connection).ExecuteScalarAsync(Ct);
        const string Tail = "monitor_kind, subject, evidence_key, problem, reason, actor, created_at";
        var pointer = $"INSERT INTO research.lessons (lesson_id, state, from_state, {Tail}) VALUES ({stateRow}, 'dismissed', 'open', 'cusum', 's', 'e', 'p', 'r', 'a', now())";
        var closedBare = $"INSERT INTO research.lessons (lesson_id, state, from_state, {Tail}) VALUES ({lesson}, 'closed', 'linked', 'cusum', 's', 'e', 'p', 'r', 'a', now())";

        var fk = await Should.ThrowAsync<PostgresException>(async () => await new NpgsqlCommand(pointer, connection).ExecuteNonQueryAsync(Ct));
        var check = await Should.ThrowAsync<PostgresException>(async () => await new NpgsqlCommand(closedBare, connection).ExecuteNonQueryAsync(Ct));

        fk.SqlState.ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
        check.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        using var notSeed = await Client.GetAsync(new Uri($"/api/lessons/{stateRow}", UriKind.Relative), Ct);
        notSeed.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
