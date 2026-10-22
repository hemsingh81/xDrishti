using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Learning;
using XDrishti.Application.Research;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>
/// Spec P6-04 on a real database: decision-time features are kept with their unknowns (NaN) and written once, the training set is built from a stored run with its labels, features and point-in-time cell statistics, and its record is written once.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class TrainingSetStoreTests(ApiFactory factory) : DataTestBase(factory)
{
    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "DELETE FROM research.trade_features");
        await ExecuteAsync(connection, "DELETE FROM research.training_sets");
    }

    private async Task<SimulationRun> StoreRunAsync()
    {
        var run = StoredRunFixtures.Run(to: RunStoreRoundTripTests.NextTo());
        using var scope = Factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<ICommandHandler<StoreRunCommand, StoreOutcome>>().HandleAsync(new StoreRunCommand(run, StoreKit.Record(run), SimulateStore.Actor), Ct);
        stored.IsSuccess.ShouldBeTrue();
        return run;
    }

    private static TradeFeatureRow Row(string runKey, RunTrade t, int index)
    {
        var values = Enumerable.Range(0, TradeFeatureCatalog.StoredCount).Select(i => (double)(i + index)).ToArray();
        values[TradeFeatureCatalog.IndexOf("vix")] = double.NaN;
        return new TradeFeatureRow(runKey, t.Trade.Key, t.Trade.StrategyId, t.Trade.TriggerTimeframe.ToCode(), t.Trade.Side.ToString().ToUpperInvariant(), t.Trade.TradeDate, t.Trade.DecisionEpochSeconds, values);
    }

    private static string Join(IEnumerable<double> values) => string.Join(',', values.Select(v => v.ToString("R", CultureInfo.InvariantCulture)));

    [Fact]
    public async Task Features_round_trip_with_their_unknowns_and_a_second_write_adds_nothing()
    {
        var run = await StoreRunAsync();
        var rows = run.Trades.Select((t, i) => Row(run.RunKey, t, i)).ToList();
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ITradeFeatureStore>();

        var first = await store.StoreAsync(rows, DateTimeOffset.UtcNow, Ct);
        var second = await store.StoreAsync(rows, DateTimeOffset.UtcNow, Ct);
        var read = await store.ReadAsync([run.RunKey], TradeFeatureCatalog.Version, Ct);
        var other = await store.ReadAsync([run.RunKey], "tf-0", Ct);

        first.ShouldBe(rows.Count);
        second.ShouldBe(0);
        read.Count.ShouldBe(rows.Count);
        var wanted = rows.OrderBy(r => r.TradeKey, StringComparer.Ordinal).First();
        var got = read.Single(r => r.TradeKey == wanted.TradeKey);
        Join(got.Values).ShouldBe(Join(wanted.Values));
        double.IsNaN(got.Values[TradeFeatureCatalog.IndexOf("vix")]).ShouldBeTrue();
        got.DecisionEpochSeconds.ShouldBe(wanted.DecisionEpochSeconds);
        got.TradeDate.ShouldBe(wanted.TradeDate);
        other.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_training_set_is_built_from_the_stored_run_and_its_record_is_written_once()
    {
        var run = await StoreRunAsync();
        using (var scope = Factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ITradeFeatureStore>().StoreAsync([.. run.Trades.Select((t, i) => Row(run.RunKey, t, i))], DateTimeOffset.UtcNow, Ct);
        }

        var command = new BuildTrainingSetCommand([new CellSource(run.RunKey, "intraday", "FIXED_RR")], new CellStatsOptions(), ["cell_n", "rsi14"], "test");
        TrainingSetOutcome first;
        TrainingSetOutcome again;
        using (var scope = Factory.Services.CreateScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<BuildTrainingSetCommand, TrainingSetOutcome>>();
            var a = await handler.HandleAsync(command, Ct);
            var b = await handler.HandleAsync(command, Ct);
            a.IsSuccess.ShouldBeTrue(a.IsFailure ? a.Error.Message : string.Empty);
            first = a.Value;
            again = b.Value;
        }

        first.Rows.ShouldBeGreaterThan(0);
        first.AlreadyStored.ShouldBeFalse();
        again.AlreadyStored.ShouldBeTrue();
        again.Id.ShouldBe(first.Id);
        first.Positives.ShouldBe(run.Trades.Count(t => t.Trade.ExitReason == ExitReason.Target));
        (await ScalarAsync<long>("SELECT count(*) FROM research.training_sets")).ShouldBe(1);
        (await ScalarAsync<int>("SELECT row_count FROM research.training_sets")).ShouldBe(first.Rows);
        (await ScalarAsync<string>("SELECT feature_version FROM research.training_sets")).ShouldBe("tf-1");
        (await ScalarAsync<string>("SELECT sources FROM research.training_sets")).ShouldBe($"{run.RunKey}:intraday:FIXED_RR");
    }
}
