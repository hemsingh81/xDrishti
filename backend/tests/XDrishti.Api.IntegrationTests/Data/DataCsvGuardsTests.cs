using System.Net;
using System.Net.Http.Json;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Api.IntegrationTests.Data;

/// <summary>
/// A CSV file with a wrong clock, the wrong time unit, dates in the future or more time than the limits allow fails as a whole before anything of it is merged;
/// the legitimate oddities of the real files (the Muhurat evening session, a Saturday session, repaired bars) still load (ADR 0016 §4, §7).
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class DataCsvGuardsTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly DateOnly Monday = new(2026, 10, 5);

    private static List<Bar> Days(int count) => [.. Enumerable.Range(0, count).SelectMany(d => BarFixtures.Session(Monday.AddDays(d), 1))];

    private static IEnumerable<Bar> Shifted(IEnumerable<Bar> bars, long seconds) => bars.Select(b => b with { EpochSeconds = b.EpochSeconds + seconds });

    public static TheoryData<string, string> WrongFiles => new()
    {
        { "shifted", "after subtracting 5 h 30 min" },
        { "shiftedBack", "after adding 5 h 30 min" },
        { "endOfMinute", "END of each minute" },
        { "milliseconds", "look like epoch milliseconds" },
        { "future", "later than one day from now (2026-10-13)" },
        { "tooLong", "spans more than 3,700 days" },
    };

    [Theory]
    [MemberData(nameof(WrongFiles))]
    public async Task A_file_with_a_wrong_clock_the_wrong_unit_or_too_much_time_fails_alone_and_nothing_of_it_is_stored(string kind, string reason)
    {
        var bad = await SeedAsync("BAD");
        var ok = await SeedAsync("OK");
        var days = Days(3);
        var csv = kind switch
        {
            "shifted" => BarFixtures.Csv(Shifted(days, 19_800)),
            "shiftedBack" => BarFixtures.Csv(Shifted(days, -19_800)),
            "endOfMinute" => BarFixtures.Csv(Shifted(days, 60)),
            "milliseconds" => BarFixtures.Csv(days.Select(b => b with { EpochSeconds = b.EpochSeconds * 1000 })),
            "future" => BarFixtures.Csv(Shifted(days, 40 * 86_400)),
            _ => BarFixtures.Csv([.. BarFixtures.Session(new DateOnly(2010, 1, 4), 1, count: 10), .. days]),
        };
        BarFixtures.WriteFile(Factory.CsvFolder, "BAD", csv);
        WriteCsv("OK", BarFixtures.Session(LastSession, 9, count: 5));

        var job = await RunAsync(["csv"]);

        job.Status.ShouldBe("failed");
        (job.Csv.FilesFailed, job.Csv.FilesLoaded).ShouldBe((1, 1));
        var failure = job.Csv.FailedFiles.ShouldHaveSingleItem();
        failure.ShouldStartWith("BAD_1m.csv.gz: ");
        failure.ShouldContain(reason);
        (await StoredBarsAsync(bad)).ShouldBeEmpty();
        (await StoredBarsAsync(ok)).Count.ShouldBe(5);
        (await ScalarAsync<long>("SELECT count(*) FROM market.csv_files WHERE file_name = 'BAD_1m.csv.gz'")).ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM market.tracked_instruments WHERE instrument_id = @id", ("id", bad))).ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM market.tracked_instruments WHERE instrument_id = @id", ("id", ok))).ShouldBe(1);
        (await OverviewAsync()).Csv.FilesPending.ShouldBe(1);
    }

    [Fact]
    public async Task The_muhurat_session_a_saturday_session_and_repaired_bars_in_one_file_are_legitimate_and_load()
    {
        var abb = await SeedAsync("ABB");
        var bars = new List<Bar>();
        bars.AddRange(BarFixtures.Session(new DateOnly(2024, 10, 31), 1));
        bars.AddRange(BarFixtures.EveningSession(new DateOnly(2024, 11, 1), 2));  // Muhurat: 60 bars from 18:15
        bars.AddRange(BarFixtures.Session(new DateOnly(2024, 11, 2), 3));         // a Saturday special session
        bars.AddRange(BarFixtures.Session(new DateOnly(2024, 11, 4), 4));
        var broken = bars[10] with { Open = bars[10].High + 5 };                  // the open above the high: repaired when loaded
        bars[10] = broken;
        WriteCsv("ABB", bars);

        var job = await RunAsync(["csv"]);

        job.Csv.Status.ShouldBe("succeeded", job.Csv.Message);
        (job.Csv.FilesFailed, job.Csv.Rows, job.Csv.Inserted, job.Csv.Repaired, job.Csv.Rejected).ShouldBe((0, 375 + 60 + 375 + 375, 375 + 60 + 375 + 375, 1, 0));
        (await StoredBarsAsync(abb)).Count.ShouldBe(1185);
    }

    [Fact]
    public async Task An_instrument_the_owner_untracked_stays_untracked_when_its_file_changes_and_loads_again()
    {
        var abb = await SeedAsync("ABB");
        WriteCsv("ABB", BarFixtures.Session(LastSession, 1, count: 5));
        await RunAsync(["csv"]);
        using var untrack = new HttpRequestMessage(HttpMethod.Delete, new Uri("/api/instruments/tracked", UriKind.Relative)) { Content = JsonContent.Create(new { instrumentIds = new[] { abb } }) };
        (await Client.SendAsync(untrack, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        WriteCsv("ABB", BarFixtures.Session(LastSession, 1, count: 8), modified: new DateTimeOffset(2026, 5, 2, 0, 0, 0, TimeSpan.Zero));

        var job = await RunAsync(["csv"]);

        (job.Csv.FilesLoaded, job.Csv.Inserted, job.Csv.InstrumentsTracked).ShouldBe((1, 3, 0));
        (await ScalarAsync<bool>("SELECT active FROM market.tracked_instruments WHERE instrument_id = @id", ("id", abb))).ShouldBeFalse();
    }

    [Fact]
    public async Task An_instrument_whose_file_is_unchanged_but_that_was_never_tracked_is_tracked_by_the_next_run()
    {
        var abb = await SeedAsync("ABB");
        WriteCsv("ABB", BarFixtures.Session(LastSession, 1, count: 5));
        await RunAsync(["csv"]);
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "DELETE FROM market.tracked_instruments"); // as if the tracking of the first run had failed
        }

        var job = await RunAsync(["csv"]);

        (job.Csv.FilesUnchanged, job.Csv.FilesLoaded, job.Csv.InstrumentsTracked).ShouldBe((1, 0, 1));
        (await ScalarAsync<bool>("SELECT active FROM market.tracked_instruments WHERE instrument_id = @id", ("id", abb))).ShouldBeTrue();
    }
}
