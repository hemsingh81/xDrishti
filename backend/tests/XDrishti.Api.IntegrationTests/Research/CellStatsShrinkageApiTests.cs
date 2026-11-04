using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Learning;
using XDrishti.Application.Research;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>
/// CF-02 on a real database: a snapshot stores the shrinkage columns, <c>GET /api/cells</c> serves the block, a row written before the migration (null columns) reads "not computed", the word columns are checked, and
/// <c>Confidence:Shrinkage</c> is bound and validated at start (Gate is refused).
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class CellStatsShrinkageApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly DateOnly AsOf = new(2026, 4, 30);

    private static readonly string Snapshot = new('e', 64);

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "DELETE FROM research.cell_stats");
    }

    private async Task ComputeAsync()
    {
        var run = StoredRunFixtures.Run(to: RunStoreRoundTripTests.NextTo());
        using var scope = Factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<ICommandHandler<StoreRunCommand, StoreOutcome>>().HandleAsync(new StoreRunCommand(run, StoreKit.Record(run), SimulateStore.Actor), Ct);
        stored.IsSuccess.ShouldBeTrue();
        var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<ComputeCellStatsCommand, CellStatsOutcome>>()
            .HandleAsync(new ComputeCellStatsCommand(AsOf, [new CellSource(run.RunKey, "intraday", "FIXED_RR")], new CellStatsOptions(), "test"), Ct);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
    }

    private static CellStatsRow FullRow() => new(
        new CellId("ORB", "15m", "BUY", "intraday", "FIXED_RR"), CellWindow.All, AsOf, true, 10, 6, 9.5, 0.6, 0.5, 0.55, 0.4, 0.7, 1, -1, 0.4, 1.4, 3, 0.5, 1, 1, 2, false, null,
        new ShrinkageAssessment(ConfidenceMode.ReportOnly, "strategy", 0.1, 0.04, "estimated", 0.2174, 0.1652, -0.1258, 20, "default", 0.53, 9.5, "a note"));

    [Fact]
    public async Task A_snapshot_stores_the_shrinkage_columns_and_the_endpoint_serves_the_block()
    {
        await ComputeAsync();

        (await ScalarAsync<long>("SELECT count(*) FROM research.cell_stats WHERE shrink_mode IS NULL OR shrink_mode <> 'ReportOnly' OR shrink_note IS NULL")).ShouldBe(0);
        using var list = await Client.GetAsync(new Uri("/api/cells", UriKind.Relative), Ct);
        var rows = await list.Content.ReadFromJsonAsync<JsonElement>(Ct);
        rows.EnumerateArray().ShouldAllBe(r => r.GetProperty("shrinkage").ValueKind == JsonValueKind.Object);
        var block = rows[0].GetProperty("shrinkage");
        block.GetProperty("mode").GetString().ShouldBe("ReportOnly");
        block.GetProperty("note").GetString().ShouldNotBeNull();
        rows[0].GetProperty("limits").GetString()!.ShouldContain("not a forecast");
        rows[0].GetProperty("expectancyR").ValueKind.ShouldNotBe(JsonValueKind.Undefined);   // the raw figure is still there
    }

    [Fact]
    public async Task Every_figure_survives_the_store_and_the_endpoint()
    {
        using (var scope = Factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<ICellStatsStore>();
            (await store.StoreAsync(new CellSnapshotToStore(Snapshot, AsOf, new CellStatsOptions(), "s", [FullRow()], "test", DateTimeOffset.UtcNow), Ct)).ShouldBeTrue();
        }

        using var list = await Client.GetAsync(new Uri("/api/cells", UriKind.Relative), Ct);
        var block = (await list.Content.ReadFromJsonAsync<JsonElement>(Ct))[0].GetProperty("shrinkage");

        block.GetProperty("parentLevel").GetString().ShouldBe("strategy");
        block.GetProperty("parentMeanR").GetDouble().ShouldBe(0.1);
        block.GetProperty("tauSquared").GetDouble().ShouldBe(0.04);
        block.GetProperty("tauSource").GetString().ShouldBe("estimated");
        block.GetProperty("blend").GetDouble().ShouldBe(0.2174);
        block.GetProperty("shrunkMeanR").GetDouble().ShouldBe(0.1652);
        block.GetProperty("shrunkLowerR").GetDouble().ShouldBe(-0.1258);
        block.GetProperty("estimatedStrength").GetDouble().ShouldBe(20);
        block.GetProperty("strengthSource").GetString().ShouldBe("default");
        block.GetProperty("shrunkWinRate").GetDouble().ShouldBe(0.53);
        block.GetProperty("nUsed").GetDouble().ShouldBe(9.5);
        block.GetProperty("note").GetString().ShouldBe("a note");
    }

    [Fact]
    public async Task A_row_without_the_columns_reads_not_computed()
    {
        await ComputeAsync();
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "UPDATE research.cell_stats SET parent_level = NULL, parent_mean_r = NULL, tau_squared = NULL, blend = NULL, shrunk_mean_r = NULL, shrunk_lower_r = NULL, estimated_strength = NULL, shrunk_win_rate = NULL, tau_source = NULL, strength_source = NULL, shrink_n = NULL, shrink_mode = NULL, shrink_note = NULL");
        }

        using var list = await Client.GetAsync(new Uri("/api/cells", UriKind.Relative), Ct);
        var rows = await list.Content.ReadFromJsonAsync<JsonElement>(Ct);

        rows.GetArrayLength().ShouldBeGreaterThan(0);
        rows.EnumerateArray().ShouldAllBe(r => IsAbsent(r));
    }

    private static bool IsAbsent(JsonElement row) => !row.TryGetProperty("shrinkage", out var d) || d.ValueKind == JsonValueKind.Null;

    [Theory]
    [InlineData("parent_level = 'regime2'")]
    [InlineData("tau_source = 'guess'")]
    [InlineData("strength_source = 'guess'")]
    [InlineData("shrink_mode = 'Off'")]
    public async Task The_word_columns_refuse_a_word_outside_their_list(string set)
    {
        await ComputeAsync();
        await using var connection = await OpenAsync();

        await Should.ThrowAsync<Npgsql.PostgresException>(() => ExecuteAsync(connection, $"UPDATE research.cell_stats SET {set} WHERE id = (SELECT min(id) FROM research.cell_stats)"));
    }

    [Fact]
    public void The_defaults_are_report_only()
    {
        Factory.Services.GetRequiredService<ShrinkageOptions>().ShouldBe(new ShrinkageOptions());
    }

    [Fact]
    public void The_shrinkage_section_is_bound()
    {
        using var host = Factory.WithWebHostBuilder(b => b.UseSetting("Confidence:Shrinkage:Mode", "Off").UseSetting("Confidence:Shrinkage:TauSquaredFloor", "0.02").UseSetting("Confidence:Shrinkage:MinParentTrades", "40"));

        var options = host.Services.GetRequiredService<ShrinkageOptions>();

        options.Mode.ShouldBe(ConfidenceMode.Off);
        options.TauSquaredFloor.ShouldBe(0.02);
        options.MinParentTrades.ShouldBe(40);
        options.MinTradesForSibling.ShouldBe(5);
    }

    [Theory]
    [InlineData("Confidence:Shrinkage:TauSquaredFloor", "2")]
    [InlineData("Confidence:Shrinkage:MinParentTrades", "5")]
    [InlineData("Confidence:Shrinkage:QuantileLevel", "0.5")]
    [InlineData("Confidence:Shrinkage:Mode", "Gate")]
    public void A_value_out_of_range_or_the_Gate_mode_stops_the_host_at_start(string key, string value)
    {
        using var host = Factory.WithWebHostBuilder(b => b.UseSetting(key, value));

        Should.Throw<OptionsValidationException>(() => host.CreateClient());
    }
}
