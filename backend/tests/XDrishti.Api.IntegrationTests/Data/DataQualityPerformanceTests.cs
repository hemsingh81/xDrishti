using System.Diagnostics;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Market.Quality;

namespace XDrishti.Api.IntegrationTests.Data;

/// <summary>
/// How long a quality run over a realistic universe takes on real TimescaleDB: 214 tracked instruments with seven sessions of 1-minute bars each (the five of the window plus the earlier
/// days of the week and month that the week and month buckets reach back to: ≈ 560,000 rows), aggregated by the pipeline. The floor is far below what a laptop reaches: it only catches a check
/// that has become per-row slow. The measured times are written to the test output.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class DataQualityPerformanceTests(ApiFactory factory) : DataTestBase(factory)
{
    private const int Instruments = 214;

    [Fact]
    public async Task A_quality_run_over_214_instruments_and_a_week_of_minute_bars_takes_seconds()
    {
        var ids = new List<Guid>();
        for (var i = 0; i < Instruments; i++)
        {
            ids.Add(await SeedAsync($"P{i:D3}"));
        }

        using (var tracked = await Client.PostJsonAsync("/api/instruments/tracked", new { instrumentIds = ids }, Ct))
        {
            tracked.EnsureSuccessStatusCode();
        }

        await using (var connection = await OpenAsync())
        {
            var seed = 0;
            foreach (var id in ids)
            {
                await ExecuteAsync(
                    connection,
                    """
                    INSERT INTO market.bars_1m (instrument_id, ts, open, high, low, close, volume, source)
                    SELECT @id, ((d + time '09:15') AT TIME ZONE 'Asia/Kolkata') + m * interval '1 minute',
                           100 + 0.01 * ((m + @seed) % 7), greatest(100 + 0.01 * ((m + @seed) % 7), 100 + 0.01 * ((m + @seed + 1) % 7)) + 0.01,
                           least(100 + 0.01 * ((m + @seed) % 7), 100 + 0.01 * ((m + @seed + 1) % 7)) - 0.01, 100 + 0.01 * ((m + @seed + 1) % 7), (m * 13) % 1000, 'dhan'
                    FROM (VALUES (date '2026-10-01'), ('2026-10-02'), ('2026-10-05'), ('2026-10-06'), ('2026-10-07'), ('2026-10-08'), ('2026-10-09')) AS days(d)
                    CROSS JOIN generate_series(0, 374) AS m
                    """,
                    ("id", id), ("seed", seed++));
            }

            await ExecuteAsync(
                connection,
                """
                INSERT INTO market.bar_coverage (instrument_id, first_bar, last_bar, bars, csv_bars, dhan_bars, updated_at, dirty_from, dirty_to, aggregates)
                SELECT instrument_id, min(ts), max(ts), count(*), 0, count(*), now(), min(ts), max(ts), '{}'::jsonb FROM market.bars_1m GROUP BY instrument_id
                """);
        }

        var clock = Stopwatch.StartNew();
        var aggregate = await RunAsync(["aggregate"]);
        var aggregateSeconds = clock.Elapsed.TotalSeconds;
        aggregate.Status.ShouldBe("succeeded", aggregate.Error);

        clock.Restart();
        Factory.Clock.Advance(TimeSpan.FromSeconds(1));
        using var started = await Client.PostJsonAsync("/api/data/quality/run", new { }, Ct);
        started.EnsureSuccessStatusCode();
        await Factory.DataJobs.RunPendingAsync(Factory.Services, Ct);
        var qualitySeconds = clock.Elapsed.TotalSeconds;

        using var overview = await Client.GetAsync("/api/data/quality/overview", Ct);
        var result = await overview.ReadAsync<QualityOverview>(Ct);
        TestContext.Current.SendDiagnosticMessage(
            $"Aggregates: {aggregate.Aggregate.Buckets:N0} bars for {aggregate.Aggregate.Done} instruments in {aggregateSeconds:F1} s. " +
            $"Quality run: {result.Counts.Checked} instruments x {result.LastRun!.Sessions} sessions in {qualitySeconds:F1} s ({qualitySeconds / result.Counts.Checked * 1000:F0} ms per instrument).");
        result.Counts.Tracked.ShouldBe(Instruments);
        (result.Counts.Checked, result.Counts.Passed).ShouldBe((Instruments, Instruments));
        result.LastRun.Status.ShouldBe("succeeded");
        qualitySeconds.ShouldBeLessThan(60);
    }
}
