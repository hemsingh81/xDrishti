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
using XDrishti.Domain.Learning;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>Spec P6-11 on a real database: an experiment between two stored runs is recorded once, whatever the outcome, the ledger count is the trials of the next one, and the two endpoints read the ledger.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class ExperimentLedgerApiTests(ApiFactory factory) : DataTestBase(factory)
{
    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "DELETE FROM research.experiments");
    }

    private async Task<(string Base, string Cand)> StoreRunsAsync()
    {
        var a = StoredRunFixtures.Run(to: RunStoreRoundTripTests.NextTo());
        var b = StoredRunFixtures.Run(to: RunStoreRoundTripTests.NextTo());
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ICommandHandler<StoreRunCommand, XDrishti.Application.Abstractions.Research.StoreOutcome>>();
        foreach (var run in new[] { a, b })
        {
            (await store.HandleAsync(new StoreRunCommand(run, StoreKit.Record(run), SimulateStore.Actor), Ct)).IsSuccess.ShouldBeTrue();
        }

        return (a.RunKey, b.RunKey);
    }

    private async Task<XDrishti.Domain.Common.Result<RunExperimentOutcome>> RunAsync(string baseKey, string candKey, string knob)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<RunExperimentCommand, RunExperimentOutcome>>()
            .HandleAsync(new RunExperimentCommand(baseKey, candKey, new ExperimentKnob(knob, ExperimentKnobKind.Parameter, "1", "2"), new ExperimentOptions(), 0, "test"), Ct);
    }

    [Fact]
    public async Task Before_any_experiment_the_list_is_empty_and_one_is_not_found()
    {
        using var list = await Client.GetAsync(new Uri("/api/experiments", UriKind.Relative), Ct);
        using var one = await Client.GetAsync(new Uri("/api/experiments/1", UriKind.Relative), Ct);
        using var bad = await Client.GetAsync(new Uri("/api/experiments?limit=0", UriKind.Relative), Ct);

        (await list.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetArrayLength().ShouldBe(0);
        one.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        bad.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task An_experiment_is_recorded_once_counts_as_a_trial_and_is_read_back()
    {
        var (b, c) = await StoreRunsAsync();

        var first = await RunAsync(b, c, "stop");
        var again = await RunAsync(b, c, "stop");
        var second = await RunAsync(b, c, "target");

        first.IsSuccess.ShouldBeTrue(first.IsFailure ? first.Error.Message : string.Empty);
        again.Value.AlreadyRecorded.ShouldBeTrue();
        again.Value.Id.ShouldBe(first.Value.Id);
        first.Value.Trials.ShouldBe(1);
        second.Value.Trials.ShouldBe(2);
        using var list = await Client.GetAsync(new Uri("/api/experiments", UriKind.Relative), Ct);
        var rows = await list.Content.ReadFromJsonAsync<JsonElement>(Ct);
        rows.GetArrayLength().ShouldBe(2);
        rows[0].GetProperty("knobName").GetString().ShouldBe("target");
        rows[0].GetProperty("reportText").GetString().ShouldBeEmpty();
        using var one = await Client.GetAsync(new Uri($"/api/experiments/{first.Value.Id}", UriKind.Relative), Ct);
        var detail = await one.Content.ReadFromJsonAsync<JsonElement>(Ct);
        detail.GetProperty("outcome").GetString().ShouldBe(first.Value.Outcome);
        detail.GetProperty("reportText").GetString()!.ShouldContain("not measured");
    }

    [Fact]
    public async Task An_experiment_on_a_run_that_is_not_stored_is_refused_and_writes_nothing()
    {
        var result = await RunAsync(new string('a', 64), new string('b', 64), "stop");

        result.Error.Code.ShouldBe(XDrishti.Application.Learning.LearningErrors.RunNotFoundCode);
        using var list = await Client.GetAsync(new Uri("/api/experiments", UriKind.Relative), Ct);
        (await list.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetArrayLength().ShouldBe(0);
    }
}
