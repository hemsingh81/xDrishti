using System.Diagnostics;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Data;

/// <summary>
/// How fast the pipeline loads and aggregates a realistic amount of data: about two years of one-minute bars per instrument (≈ 190,000 rows, as in the real files).
/// Generated bars by default (3 instruments; <c>XD_PERF_FILES</c> changes the number); set <c>XD_PERF_CSV_DIR</c> to a folder with real <c>&lt;SYMBOL&gt;_1m.csv.gz</c> files to load the first
/// ones of them instead (they are copied into the test's temporary folder, never into the repository). The floors are far below what a laptop reaches: they only catch a pipeline that has become per-row slow.
/// The measured numbers are written to the test output.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class DataLoadPerformanceTests(ApiFactory factory) : DataTestBase(factory)
{
    [Fact]
    public async Task Two_years_of_minute_bars_per_instrument_load_and_aggregate_at_a_useful_speed()
    {
        var instruments = int.TryParse(Environment.GetEnvironmentVariable("XD_PERF_FILES"), out var requested) && requested > 0 ? requested : 3;
        var realFolder = Environment.GetEnvironmentVariable("XD_PERF_CSV_DIR");
        var tickers = new List<string>();
        if (realFolder is { Length: > 0 } && Directory.Exists(realFolder))
        {
            foreach (var path in Directory.EnumerateFiles(realFolder, "*_1m.csv.gz").Order(StringComparer.Ordinal).Take(instruments))
            {
                var name = Path.GetFileName(path);
                File.Copy(path, CsvPath(name));
                tickers.Add(name[..name.IndexOf("_1m", StringComparison.Ordinal)]);
            }
        }
        else
        {
            for (var i = 0; i < instruments; i++)
            {
                tickers.Add($"PERF{i}");
                var bars = new List<XDrishti.Domain.Market.Bars.Bar>();
                for (var date = new DateOnly(2024, 4, 1); date <= new DateOnly(2026, 4, 30); date = date.AddDays(1))
                {
                    if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                    {
                        bars.AddRange(BarFixtures.Session(date, i + 1));
                    }
                }

                WriteCsv(tickers[i], bars);
            }
        }

        foreach (var ticker in tickers)
        {
            await SeedAsync(ticker);
        }

        var clock = Stopwatch.StartNew();
        var csv = await RunAsync(["csv"]);
        var loadSeconds = clock.Elapsed.TotalSeconds;
        csv.Status.ShouldBe("succeeded", csv.Error);

        clock.Restart();
        var aggregate = await RunAsync(["aggregate"]);
        var aggregateSeconds = clock.Elapsed.TotalSeconds;
        aggregate.Status.ShouldBe("succeeded", aggregate.Error);

        var rows = csv.Csv.Rows;
        TestContext.Current.SendDiagnosticMessage(
            $"CSV load: {rows:N0} rows from {csv.Csv.FilesLoaded} files in {loadSeconds:F1} s = {rows / loadSeconds:N0} rows/s ({csv.Csv.Inserted:N0} inserted, {csv.Csv.Repaired} repaired, {csv.Csv.Rejected} rejected). " +
            $"Aggregates: {aggregate.Aggregate.Buckets:N0} bars for {aggregate.Aggregate.Done} instruments in {aggregateSeconds:F1} s ({aggregateSeconds / aggregate.Aggregate.Done:F1} s per instrument).");
        (rows / loadSeconds).ShouldBeGreaterThan(20_000);
        aggregate.Aggregate.Done.ShouldBe(tickers.Count);
        (aggregateSeconds / tickers.Count).ShouldBeLessThan(60);
    }
}
