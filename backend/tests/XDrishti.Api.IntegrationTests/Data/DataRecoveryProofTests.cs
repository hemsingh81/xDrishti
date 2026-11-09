using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Api.IntegrationTests.Data;

/// <summary>
/// The proof of a recovered end-of-day step on real TimescaleDB (spec AU-03, ADR 0240): ten sessions from CSV with one partial day (Tue 2026-10-06), a fake Dhan source behind the port, the real gap, aggregate, day-count and quality
/// stores. The clock is Monday 2026-10-12 12:00 India time, so Monday itself has no bars yet.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class DataRecoveryProofTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly DateOnly Partial = new(2026, 10, 6);

    private static readonly DateOnly[] Sessions =
        [new(2026, 9, 28), new(2026, 9, 29), new(2026, 9, 30), new(2026, 10, 1), new(2026, 10, 2), new(2026, 10, 5), Partial, new(2026, 10, 7), new(2026, 10, 8), new(2026, 10, 9)];

    private static readonly string[] DhanAndAggregate = ["dhan", "aggregate"];

    private static readonly string[] AggregateAndQuality = ["aggregate", "quality"];

    private const int PartialBars = 200;

    private static bool IsPartialDayWindow(FetchWindow w) => w.From.ToUnixTimeSeconds() == BarFixtures.Epoch(Partial, 0) && w.To.ToUnixTimeSeconds() == BarFixtures.Epoch(Partial.AddDays(1), 0);

    private async Task LoadAsync()
    {
        var id = await SeedAsync("ABB");
        using (var tracked = await Client.PostJsonAsync("/api/instruments/tracked", new { instrumentIds = new[] { id } }, Ct))
        {
            tracked.EnsureSuccessStatusCode();
        }

        WriteCsv("ABB", [.. Sessions.SelectMany(d => BarFixtures.Session(d, 1, count: d == Partial ? PartialBars : SessionClock.RegularBars))]);
        (await RunAsync(["csv", "aggregate"])).Status.ShouldBe("succeeded");
    }

    private async Task DhanThenQualityAsync(bool refetchGaps)
    {
        using var response = await Client.PostJsonAsync("/api/data/refresh", new { steps = DhanAndAggregate, refetchGaps }, Ct);
        response.EnsureSuccessStatusCode();
        await Factory.DataJobs.RunPendingAsync(Factory.Services, Ct);
        (await RunAsync(AggregateAndQuality)).Status.ShouldBe("succeeded");
    }

    private async Task<RecoveryProofResult> ProveAsync(params DateOnly[] sessions)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var proof = scope.ServiceProvider.GetServices<IRecoveryProof>().Single(p => p.ServiceId == "eod");
        return await proof.ProveAsync(sessions, Now.AddMinutes(-1), Ct);
    }

    [Fact]
    public async Task An_unfillable_session_is_reported_as_that_a_session_without_bars_as_still_missing_and_all_recover_once_the_gap_is_filled()
    {
        await LoadAsync();
        var serve = false;
        Factory.Dhan.UseSource(t => new IntradayResult(t.InstrumentId, [.. serve && t.Windows.Any(IsPartialDayWindow) ? BarFixtures.Session(Partial, 1, startMinute: SessionClock.OpenMinute + PartialBars, count: SessionClock.RegularBars - PartialBars) : []], t.Windows.Count));
        Factory.Dhan.UseConnectedAccount();
        var monday = new DateOnly(2026, 10, 12);

        await DhanThenQualityAsync(refetchGaps: false);
        var first = await ProveAsync(new DateOnly(2026, 10, 5), Partial, new DateOnly(2026, 10, 7), monday);

        first.AllRecovered.ShouldBeFalse();
        first.Reason.ShouldBe("2 of 4 sessions recovered; not recovered: 2026-10-06 unfillable, 2026-10-12 still_missing");

        serve = true;
        await DhanThenQualityAsync(refetchGaps: true);
        var second = await ProveAsync(new DateOnly(2026, 10, 5), Partial, new DateOnly(2026, 10, 7));

        second.AllRecovered.ShouldBeTrue();
        second.Reason.ShouldBeNull();
    }

    [Fact]
    public async Task Bars_stored_but_not_aggregated_are_not_recovered_until_the_aggregates_are_built()
    {
        await LoadAsync();
        Factory.Dhan.UseSource(t => new IntradayResult(t.InstrumentId, [.. t.Windows.Any(IsPartialDayWindow) ? BarFixtures.Session(Partial, 1, startMinute: SessionClock.OpenMinute + PartialBars, count: SessionClock.RegularBars - PartialBars) : []], t.Windows.Count));
        Factory.Dhan.UseConnectedAccount();
        await DhanThenQualityAsync(refetchGaps: false);
        (await ProveAsync(Partial)).AllRecovered.ShouldBeTrue();

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var aggregates = scope.ServiceProvider.GetRequiredService<IBarAggregates>();
            var abb = (await scope.ServiceProvider.GetRequiredService<IFetchTargets>().ListAsync(Ct)).Single(t => t.Symbol == "NSE:ABB");
            (await aggregates.MarkDirtyAsync(abb.InstrumentId, new DateTimeOffset(2026, 10, 6, 3, 45, 0, TimeSpan.Zero), Ct)).ShouldBeTrue();
        }

        (await ProveAsync(Partial)).Reason.ShouldBe("0 of 1 sessions recovered; not recovered: 2026-10-06 still_missing");
    }
}
