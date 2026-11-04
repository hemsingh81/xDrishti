using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Learning;
using XDrishti.Application.Research;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>
/// CF-01 on a real database: a snapshot stores the dependence columns, <c>GET /api/cells</c> serves the block, a row written before the migration (null columns) reads "not computed", and the <c>Confidence</c> section is bound and
/// validated at start.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class CellStatsDependenceApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly DateOnly AsOf = new(2026, 4, 30);

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

    [Fact]
    public async Task A_snapshot_stores_the_dependence_columns_and_the_endpoint_serves_the_block()
    {
        await ComputeAsync();

        (await ScalarAsync<long>("SELECT count(*) FROM research.cell_stats WHERE n_eff_method IS NULL OR n_eff_mode <> 'ReportOnly'")).ShouldBe(0);
        using var list = await Client.GetAsync(new Uri("/api/cells", UriKind.Relative), Ct);
        var rows = await list.Content.ReadFromJsonAsync<JsonElement>(Ct);
        rows.EnumerateArray().ShouldAllBe(r => r.GetProperty("dependence").ValueKind == JsonValueKind.Object);
        var dependence = rows[0].GetProperty("dependence");
        dependence.GetProperty("mode").GetString().ShouldBe("ReportOnly");
        dependence.GetProperty("n").GetInt32().ShouldBe(rows[0].GetProperty("n").GetInt32());
        dependence.GetProperty("summary").GetString()!.ShouldStartWith("n=");
        dependence.GetProperty("targetEdgeR").GetDouble().ShouldBe(0.15);
        rows[0].GetProperty("limits").GetString()!.ShouldContain("Effective n is an estimate");
        rows[0].GetProperty("effectiveN").GetDouble().ShouldBeGreaterThanOrEqualTo(0);   // the recency-weight sum is still there
    }

    [Fact]
    public async Task A_row_without_the_columns_reads_not_computed()
    {
        await ComputeAsync();
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "UPDATE research.cell_stats SET n_eff = NULL, design_effect = NULL, n_eff_method = NULL, needed_trades = NULL, min_detectable_edge_r = NULL, min_trl = NULL, enough_by_n_eff = NULL, span_years = NULL, n_eff_mode = NULL, n_eff_note = NULL, target_edge_r = NULL");
        }

        using var list = await Client.GetAsync(new Uri("/api/cells", UriKind.Relative), Ct);
        var rows = await list.Content.ReadFromJsonAsync<JsonElement>(Ct);

        rows.GetArrayLength().ShouldBeGreaterThan(0);
        rows.EnumerateArray().ShouldAllBe(r => IsAbsent(r));
    }

    private static bool IsAbsent(JsonElement row) => !row.TryGetProperty("dependence", out var d) || d.ValueKind == JsonValueKind.Null;

    [Fact]
    public async Task The_method_column_refuses_a_word_outside_the_three()
    {
        await ComputeAsync();
        await using var connection = await OpenAsync();

        await Should.ThrowAsync<Npgsql.PostgresException>(() => ExecuteAsync(connection, "UPDATE research.cell_stats SET n_eff_method = 'product' WHERE id = (SELECT min(id) FROM research.cell_stats)"));
    }

    [Fact]
    public void The_defaults_are_report_only()
    {
        Factory.Services.GetRequiredService<ConfidenceOptions>().ShouldBe(new ConfidenceOptions());
    }

    [Fact]
    public void The_confidence_section_is_bound()
    {
        using var host = Factory.WithWebHostBuilder(b => b.UseSetting("Confidence:EffectiveN:Mode", "Gate").UseSetting("Confidence:Power:Alpha", "0.1").UseSetting("Confidence:Power:TargetEdgeR", "0.2"));

        var options = host.Services.GetRequiredService<ConfidenceOptions>();

        options.Mode.ShouldBe(ConfidenceMode.Gate);
        options.Alpha.ShouldBe(0.1);
        options.TargetEdgeR.ShouldBe(0.2);
        options.MinClusters.ShouldBe(5);
    }

    [Fact]
    public void A_value_out_of_range_stops_the_host_at_start()
    {
        using var host = Factory.WithWebHostBuilder(b => b.UseSetting("Confidence:Power:Alpha", "0.9"));

        Should.Throw<OptionsValidationException>(() => host.CreateClient());
    }
}
