using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Learning;
using XDrishti.Application.Research;
using XDrishti.Domain.Learning;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>
/// Spec P6-01 end to end on a real database: a stored run becomes cells through the use case, the snapshot is insert-only and idempotent, and the endpoints read the newest snapshot (list, filters, one cell, 404, 422).
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class CellStatsApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly DateOnly AsOf = new(2026, 4, 30);

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "DELETE FROM research.cell_stats");
    }

    private async Task<CellStatsOutcome> ComputeAsync(string runKey, string exit = "FIXED_RR", double halfLife = 180)
    {
        using var scope = Factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<ComputeCellStatsCommand, CellStatsOutcome>>()
            .HandleAsync(new ComputeCellStatsCommand(AsOf, [new CellSource(runKey, "intraday", exit)], new CellStatsOptions(RecencyHalfLifeDays: halfLife), "test"), Ct);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        return result.Value;
    }

    private async Task<string> StoredRunKeyAsync()
    {
        var run = StoredRunFixtures.Run(to: RunStoreRoundTripTests.NextTo());
        using var scope = Factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<ICommandHandler<StoreRunCommand, StoreOutcome>>().HandleAsync(new StoreRunCommand(run, StoreKit.Record(run), SimulateStore.Actor), Ct);
        stored.IsSuccess.ShouldBeTrue();
        return run.RunKey;
    }

    [Fact]
    public async Task Before_any_computation_the_list_is_empty_and_a_cell_is_not_found()
    {
        using var list = await Client.GetAsync(new Uri("/api/cells", UriKind.Relative), Ct);
        using var one = await Client.GetAsync(new Uri("/api/cells/" + Uri.EscapeDataString("ORB|15m|BUY|intraday|FIXED_RR|*"), UriKind.Relative), Ct);

        list.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await list.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetArrayLength().ShouldBe(0);
        one.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_stored_run_gives_cells_the_same_inputs_write_nothing_and_the_endpoints_show_them()
    {
        var key = await StoredRunKeyAsync();

        var first = await ComputeAsync(key);
        var again = await ComputeAsync(key);

        first.AlreadyStored.ShouldBeFalse();
        first.Rows.ShouldBe(first.Cells * 3);
        again.AlreadyStored.ShouldBeTrue();
        again.SnapshotKey.ShouldBe(first.SnapshotKey);
        using var list = await Client.GetAsync(new Uri("/api/cells", UriKind.Relative), Ct);
        var rows = await list.Content.ReadFromJsonAsync<JsonElement>(Ct);
        rows.GetArrayLength().ShouldBe(first.Rows);
        var cellKey = rows[0].GetProperty("cellKey").GetString()!;
        cellKey.ShouldEndWith("|intraday|FIXED_RR|*");
        rows[0].GetProperty("limits").GetString()!.ShouldContain("modelled fills");
        using var one = await Client.GetAsync(new Uri("/api/cells/" + Uri.EscapeDataString(cellKey), UriKind.Relative), Ct);
        one.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await one.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetArrayLength().ShouldBe(3);
        using var filtered = await Client.GetAsync(new Uri("/api/cells?window=60d&strategy=ORB", UriKind.Relative), Ct);
        (await filtered.Content.ReadFromJsonAsync<JsonElement>(Ct)).EnumerateArray().ShouldAllBe(r => r.GetProperty("window").GetString() == "60d");
    }

    [Fact]
    public async Task A_new_snapshot_becomes_the_newest_and_the_old_one_stays()
    {
        var key = await StoredRunKeyAsync();
        var one = await ComputeAsync(key);
        var two = await ComputeAsync(key, halfLife: 90);

        using var list = await Client.GetAsync(new Uri("/api/cells", UriKind.Relative), Ct);

        two.SnapshotKey.ShouldNotBe(one.SnapshotKey);
        (await list.Content.ReadFromJsonAsync<JsonElement>(Ct))[0].GetProperty("snapshotKey").GetString().ShouldBe(two.SnapshotKey);
        (await ScalarAsync<long>("SELECT count(DISTINCT snapshot_key) FROM research.cell_stats")).ShouldBe(2);
    }

    [Fact]
    public async Task An_unknown_window_is_422()
    {
        using var response = await Client.GetAsync(new Uri("/api/cells?window=7d", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task The_endpoints_need_a_signed_in_owner()
    {
        using var anonymous = Factory.CreateClient();

        using var response = await anonymous.GetAsync(new Uri("/api/cells", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
