using System.Net;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Market.Bars;

namespace XDrishti.Api.IntegrationTests.Data;

/// <summary>The coverage table and the latest bars of an instrument.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class DataInstrumentsApiTests(ApiFactory factory) : DataTestBase(factory)
{
    private async Task<(Guid Abb, Guid Tcs, Guid Infy)> SeedCoverageAsync()
    {
        var abb = await SeedAsync("ABB");     // 375 bars, current, csv
        var tcs = await SeedAsync("TCS");     // 20 bars, behind, mixed
        var infy = await SeedAsync("INFY");   // tracked, no bars
        await SeedAsync("WIPRO");             // not tracked, no bars: not listed
        WriteCsv("ABB", BarFixtures.Session(LastSession, 1));
        WriteCsv("TCS", BarFixtures.Session(new DateOnly(2026, 10, 7), 2, count: 20));
        await RunAsync(["csv", "aggregate"]);
        await StoreAsync(tcs, BarFixtures.Session(new DateOnly(2026, 10, 7), 3, startMinute: 11 * 60, count: 1)[0], "dhan");
        using var track = await Client.PostJsonAsync("/api/instruments/tracked", new { instrumentIds = new[] { infy } }, Ct);
        track.EnsureSuccessStatusCode();
        return (abb, tcs, infy);
    }

    [Fact]
    public async Task The_list_has_the_tracked_instruments_and_those_with_bars_with_source_status_and_days_behind()
    {
        var (abb, tcs, infy) = await SeedCoverageAsync();
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "UPDATE market.bar_coverage SET dhan_bars = 1, csv_bars = csv_bars WHERE instrument_id = @id", ("id", tcs));
        }

        var page = await InstrumentsAsync();

        page.Total.ShouldBe(3);
        page.Items.Select(i => i.Symbol).ShouldBe(["NSE:ABB", "NSE:INFY", "NSE:TCS"]);
        var a = page.Items[0];
        (a.Id, a.Name, a.Type, a.Tracked, a.Bars, a.Source, a.Status, a.DaysBehind).ShouldBe((abb, "ABB Ltd", "share", true, 375, "csv", "current", 0));
        var i = page.Items[1];
        (i.Id, i.Bars, i.Source, i.Status, i.FirstBar, i.LastBar, i.DaysBehind).ShouldBe((infy, 0, null, "empty", null, null, null));
        var t = page.Items[2];
        (t.Tracked, t.Bars, t.Source, t.Status, t.DaysBehind).ShouldBe((true, 20, "mixed", "behind", 2));
        t.FirstBar!.Value.Offset.ShouldBe(TimeSpan.FromMinutes(330));
    }

    [Fact]
    public async Task The_list_filters_by_text_and_status_sorts_and_pages()
    {
        await SeedCoverageAsync();

        (await InstrumentsAsync("?q=tc")).Items.Select(i => i.Symbol).ShouldBe(["NSE:TCS"]);
        (await InstrumentsAsync("?q=abb%20ltd")).Items.Select(i => i.Symbol).ShouldBe(["NSE:ABB"]);
        (await InstrumentsAsync("?status=behind")).Items.Select(i => i.Symbol).ShouldBe(["NSE:TCS"]);
        (await InstrumentsAsync("?status=current")).Items.Select(i => i.Symbol).ShouldBe(["NSE:ABB"]);
        (await InstrumentsAsync("?status=empty")).Items.Select(i => i.Symbol).ShouldBe(["NSE:INFY"]);
        (await InstrumentsAsync("?sort=-bars")).Items.Select(i => i.Symbol).ShouldBe(["NSE:ABB", "NSE:TCS", "NSE:INFY"]);
        (await InstrumentsAsync("?sort=bars")).Items.Select(i => i.Symbol).ShouldBe(["NSE:INFY", "NSE:TCS", "NSE:ABB"]);
        (await InstrumentsAsync("?sort=-lastBar")).Items.Select(i => i.Symbol).ShouldBe(["NSE:ABB", "NSE:TCS", "NSE:INFY"]); // no bars last, either way
        (await InstrumentsAsync("?sort=lastBar")).Items.Select(i => i.Symbol).ShouldBe(["NSE:TCS", "NSE:ABB", "NSE:INFY"]);
        (await InstrumentsAsync("?sort=-name")).Items.Select(i => i.Symbol).ShouldBe(["NSE:TCS", "NSE:INFY", "NSE:ABB"]);
        var second = await InstrumentsAsync("?pageSize=2&page=2");
        second.Items.Select(i => i.Symbol).ShouldBe(["NSE:TCS"]);
        (second.Page, second.PageSize, second.Total).ShouldBe((2, 2, 3));
        (await InstrumentsAsync("?page=9")).Items.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("?status=weird", "status")]
    [InlineData("?sort=colour", "sort")]
    [InlineData("?sort=1", "sort")]
    [InlineData("?page=0", "page")]
    [InlineData("?pageSize=201", "pageSize")]
    [InlineData("?pageSize=0", "pageSize")]
    public async Task A_bad_list_request_is_refused_with_the_field(string query, string field)
    {
        using var response = await Client.GetAsync("/api/data/instruments" + query, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadProblemAsync(Ct)).Code.ShouldBe("data.invalid");
        (await response.ReadFieldErrorsAsync(Ct)).Keys.ShouldContain(field);
    }

    [Fact]
    public async Task The_latest_bars_come_newest_first_in_every_timeframe_with_india_times()
    {
        var (abb, _, _) = await SeedCoverageAsync();
        var bars = BarFixtures.Session(LastSession, 1);

        var minutes = await BarsAsync(abb, "1m", take: 3);
        var fiveMinutes = await BarsAsync(abb, "5m", take: 2);
        var hours = await BarsAsync(abb, "1h");
        var days = await BarsAsync(abb, "1D");
        var weeks = await BarsAsync(abb, "1W");
        var months = await BarsAsync(abb, "1M");
        var defaulted = await BarsAsync(abb, null);

        (minutes.Symbol, minutes.Name, minutes.Timeframe).ShouldBe(("NSE:ABB", "ABB Ltd", "1m"));
        minutes.Items.Select(b => b.Ts.ToUnixTimeSeconds()).ShouldBe([bars[374].EpochSeconds, bars[373].EpochSeconds, bars[372].EpochSeconds]);
        minutes.Items[0].ShouldBe(new BarRow(minutes.Items[0].Ts, bars[374].Open, bars[374].High, bars[374].Low, bars[374].Close, bars[374].Volume, 1, true, "csv"));
        minutes.Items[0].Ts.ToString("HH:mm zzz").ShouldBe("15:29 +05:30");
        fiveMinutes.Items.Select(b => b.Ts.ToString("HH:mm")).ShouldBe(["15:25", "15:20"]);
        fiveMinutes.Items[0].ShouldBe(fiveMinutes.Items[0] with { Bars = 5, Complete = true, Source = null });
        hours.Items.Select(b => b.Ts.ToString("HH:mm")).ShouldBe(["15:15", "14:15", "13:15", "12:15", "11:15", "10:15", "09:15"]);
        (hours.Items[0].Bars, hours.Items[0].Complete).ShouldBe((15, true));
        days.Items.ShouldHaveSingleItem().Ts.ToString("yyyy-MM-dd HH:mm zzz").ShouldBe("2026-10-09 00:00 +05:30");
        (days.Items[0].Open, days.Items[0].Close, days.Items[0].Bars, days.Items[0].Complete).ShouldBe((bars[0].Open, bars[^1].Close, 375, true));
        weeks.Items.ShouldHaveSingleItem().Ts.ToString("yyyy-MM-dd").ShouldBe("2026-10-05");
        months.Items.ShouldHaveSingleItem().Ts.ToString("yyyy-MM-dd").ShouldBe("2026-10-01");
        defaulted.Timeframe.ShouldBe("1D");
    }

    [Theory]
    [InlineData("?timeframe=2h", "timeframe")]
    [InlineData("?timeframe=1d", "timeframe")]
    [InlineData("?take=0", "take")]
    [InlineData("?take=1001", "take")]
    public async Task Bad_bar_requests_are_refused_with_the_field(string query, string field)
    {
        var abb = await SeedAsync("ABB");

        using var response = await Client.GetAsync($"/api/data/instruments/{abb}/bars{query}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadProblemAsync(Ct)).Code.ShouldBe("data.invalid");
        (await response.ReadFieldErrorsAsync(Ct)).Keys.ShouldContain(field);
    }

    [Fact]
    public async Task An_unknown_instrument_has_no_bars_to_read_and_a_known_one_without_bars_gives_an_empty_list()
    {
        var abb = await SeedAsync("ABB");

        using var unknown = await Client.GetAsync($"/api/data/instruments/{Guid.NewGuid()}/bars", Ct);
        var empty = await BarsAsync(abb, "1m");

        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await unknown.ReadProblemAsync(Ct)).Code.ShouldBe("data.instrument_not_found");
        empty.Items.ShouldBeEmpty();
    }

    private async Task<InstrumentBars> BarsAsync(Guid id, string? timeframe, int? take = null)
    {
        var query = string.Join('&', new[] { timeframe is null ? null : $"timeframe={timeframe}", take is null ? null : $"take={take}" }.Where(x => x is not null));
        using var response = await Client.GetAsync($"/api/data/instruments/{id}/bars" + (query.Length > 0 ? "?" + query : string.Empty), Ct);
        return await response.ReadAsync<InstrumentBars>(Ct);
    }
}
