using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Market.Actions;
using XDrishti.Application.Market.Bars;
using XDrishti.Application.Market.Bars.Eod;
using XDrishti.Application.Market.Quality;

namespace XDrishti.Api.IntegrationTests.Data;

/// <summary>
/// Corporate actions against a real TimescaleDB (ADR 0017 §5, P1-10a): the detection that follows a data job and runs on request, the owner's decisions, the audit trail,
/// the pending counts and notice, and adjusted reads in every timeframe.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class DataCorporateActionsTests(ApiFactory factory) : DataTestBase(factory)
{
    /// <summary>Loads a generated series for a ticker through the CSV pipeline and returns the instrument and the series.</summary>
    private async Task<(Guid Id, List<SeriesDay> Series)> LoadAsync(string ticker, List<SeriesDay> series)
    {
        var id = await SeedAsync(ticker);
        WriteCsv(ticker, ActionFixtures.Bars(series));
        return (id, series);
    }

    private async Task<CorporateActionPage> ListAsync(string query = "")
    {
        using var response = await Client.GetAsync("/api/data/corporate-actions" + query, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return await response.ReadAsync<CorporateActionPage>(Ct);
    }

    private async Task<InstrumentBars> BarsAsync(Guid id, string timeframe, bool adjusted, int take = 100)
    {
        using var response = await Client.GetAsync($"/api/data/instruments/{id}/bars?timeframe={timeframe}&take={take}&adjusted={(adjusted ? "true" : "false")}", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return await response.ReadAsync<InstrumentBars>(Ct);
    }

    // The CSV files these tests load are adjusted at source (csvAdjustedThrough), so the tests acknowledge it; DataCorporateActionGuardsTests covers the refusal.
    private async Task<HttpResponseMessage> AddAsync(object body) => await Client.PostJsonAsync("/api/data/corporate-actions?acknowledgeCsvAdjusted=true", body, Ct);

    private async Task<List<(string Event, string? Detail, string Actor)>> AuditAsync()
    {
        await using var connection = await OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand("SELECT event, detail, actor FROM accounts.account_events WHERE event LIKE 'CorporateAction%' ORDER BY at, id", connection);
        var events = new List<(string, string?, string)>();
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
        {
            events.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetString(2)));
        }

        return events;
    }

    [Fact]
    public async Task A_split_in_the_loaded_files_is_suggested_after_the_job_and_nothing_is_adjusted_until_the_owner_confirms()
    {
        var split = ActionFixtures.Series(ratio: 0.2, volumeMultiple: 5);
        var (abb, _) = await LoadAsync("ABB", split);
        await LoadAsync("TCS", ActionFixtures.Series());

        var job = await RunAsync();

        job.Status.ShouldBe("succeeded", job.Error);
        job.Aggregate.Message.ShouldBe("1 corporate action suggested for review.");
        var page = await ListAsync("?status=suggested");
        (page.Total, page.Pending).ShouldBe((1, 1));
        var suggestion = page.Items.Single();
        (suggestion.InstrumentId, suggestion.Symbol, suggestion.ExDate, suggestion.Kind, suggestion.Ratio, suggestion.Factor, suggestion.Status, suggestion.Source, suggestion.DecidedAt, suggestion.DecidedBy)
            .ShouldBe((abb, "NSE:ABB", split[ActionFixtures.EventDay].Date, "split", "1:5", 0.2, "suggested", "detected", null, null));
        suggestion.Note.ShouldBe("Open was 0.200 of the previous close on 5.0x the usual volume. Could also be bonus 4:1.");

        var raw = await BarsAsync(abb, "1D", adjusted: false);
        var adjusted = await BarsAsync(abb, "1D", adjusted: true);
        adjusted.Items.ShouldBe(raw.Items);
        (adjusted.Adjustment!.Applied, adjusted.Adjustment.Factor).ShouldBe((0, 1.0));
        adjusted.Adjustment.Actions.ShouldBeEmpty();
        raw.Adjustment.ShouldBeNull();
        (await OverviewAsync()).PendingCorporateActions.ShouldBe(1);
    }

    [Fact]
    public async Task The_pending_suggestion_shows_on_the_overview_the_quality_overview_and_the_bell_until_it_is_decided()
    {
        await LoadAsync("ABB", ActionFixtures.Series(ratio: 0.5, volumeMultiple: 3));
        await RunAsync();

        using var qualityResponse = await Client.GetAsync("/api/data/quality/overview", Ct);
        var quality = await qualityResponse.ReadAsync<QualityOverview>(Ct);
        using var noticesResponse = await Client.GetAsync("/api/data/notices", Ct);
        var notices = await noticesResponse.ReadAsync<DataNotices>(Ct);

        quality.PendingCorporateActions.ShouldBe(1);
        var notice = notices.Items.Single(n => n.Kind == "data_corporate_actions_pending");
        (notice.Severity, notice.Text).ShouldBe(("warn", "1 suggested corporate action (split or bonus) to review"));
        notice.At.ShouldNotBeNull();

        var id = (await ListAsync()).Items.Single().Id;
        using var confirmed = await Client.PutJsonAsync($"/api/data/corporate-actions/{id}?acknowledgeCsvAdjusted=true", new { }, Ct);
        confirmed.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await OverviewAsync()).PendingCorporateActions.ShouldBe(0);
        using var after = await Client.GetAsync("/api/data/quality/overview", Ct);
        (await after.ReadAsync<QualityOverview>(Ct)).PendingCorporateActions.ShouldBe(0);
        using var noticesAfter = await Client.GetAsync("/api/data/notices", Ct);
        (await noticesAfter.ReadAsync<DataNotices>(Ct)).Items.ShouldNotContain(n => n.Kind == "data_corporate_actions_pending");
    }

    [Fact]
    public async Task A_gap_that_comes_back_and_a_gap_down_on_ordinary_volume_are_not_suggested()
    {
        await LoadAsync("ABB", ActionFixtures.Series(ratio: 0.5, volumeMultiple: 4, reverseAt: ActionFixtures.EventDay + 2));
        await LoadAsync("TCS", ActionFixtures.Series(ratio: 0.5, volumeMultiple: 1.0));

        var job = await RunAsync();

        job.Aggregate.Message.ShouldBeNull();
        (await ListAsync()).Total.ShouldBe(0);
    }

    [Fact]
    public async Task Detecting_again_on_request_finds_the_same_candidate_and_suggests_nothing_new()
    {
        await LoadAsync("ABB", ActionFixtures.Series(ratio: 0.25, volumeMultiple: 4));
        await RunAsync();

        using var response = await Client.PostAsync("/api/data/corporate-actions/detect", Ct);
        var again = await response.ReadAsync<DetectionResult>(Ct);
        using var second = await Client.PostAsync("/api/data/corporate-actions/detect", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        again.ShouldBe(new DetectionResult(1, 1, 0, 1));
        (await second.ReadAsync<DetectionResult>(Ct)).ShouldBe(new DetectionResult(1, 1, 0, 1));
        (await ListAsync()).Total.ShouldBe(1);
    }

    [Fact]
    public async Task Detection_on_request_finds_what_the_job_has_not_looked_at_and_a_full_rebuild_does_not_suggest_it_twice()
    {
        var (abb, _) = await LoadAsync("ABB", ActionFixtures.Series(ratio: 0.2, volumeMultiple: 5));
        await RunAsync(["csv", "aggregate"]);
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "DELETE FROM market.corporate_actions");
        }

        using var response = await Client.PostAsync("/api/data/corporate-actions/detect", Ct);
        var found = await response.ReadAsync<DetectionResult>(Ct);
        var rebuild = await RunAsync(["aggregate"]);

        found.ShouldBe(new DetectionResult(1, 1, 1, 1));
        rebuild.Aggregate.Full.ShouldBeTrue();
        (await ListAsync()).Items.Single().InstrumentId.ShouldBe(abb);
    }

    [Fact]
    public async Task An_ignored_suggestion_stays_in_the_list_and_is_never_suggested_again()
    {
        await LoadAsync("ABB", ActionFixtures.Series(ratio: 0.2, volumeMultiple: 5));
        await RunAsync();
        var id = (await ListAsync()).Items.Single().Id;

        using var ignored = await Client.PostAsync($"/api/data/corporate-actions/{id}/ignore", Ct);
        using var detect = await Client.PostAsync("/api/data/corporate-actions/detect", Ct);
        var rebuild = await RunAsync(["aggregate"]);

        var view = await ignored.ReadAsync<CorporateActionView>(Ct);
        (view.Status, view.DecidedBy).ShouldBe(("ignored", ApiFactory.OwnerEmail));
        (await detect.ReadAsync<DetectionResult>(Ct)).ShouldBe(new DetectionResult(1, 1, 0, 0));
        rebuild.Aggregate.Message.ShouldBeNull();
        var page = await ListAsync();
        (page.Total, page.Pending, page.Items.Single().Status).ShouldBe((1, 0, "ignored"));
        (await OverviewAsync()).PendingCorporateActions.ShouldBe(0);
    }

    [Fact]
    public async Task Confirming_the_split_adjusts_the_daily_bars_before_the_ex_date_and_divides_the_volume()
    {
        var split = ActionFixtures.Series(ratio: 0.2, volumeMultiple: 5);
        var (abb, _) = await LoadAsync("ABB", split);
        await RunAsync();
        var id = (await ListAsync()).Items.Single().Id;
        using var confirmed = await Client.PutJsonAsync($"/api/data/corporate-actions/{id}?acknowledgeCsvAdjusted=true", new { }, Ct);
        var exDate = split[ActionFixtures.EventDay].Date;

        var raw = await BarsAsync(abb, "1D", adjusted: false, take: 100);
        var adjusted = await BarsAsync(abb, "1D", adjusted: true, take: 100);

        (await confirmed.ReadAsync<CorporateActionView>(Ct)).Status.ShouldBe("applied");
        adjusted.Items.Count.ShouldBe(raw.Items.Count);
        for (var i = 0; i < raw.Items.Count; i++)
        {
            var date = DateOnly.FromDateTime(raw.Items[i].Ts.DateTime);
            if (date >= exDate)
            {
                adjusted.Items[i].ShouldBe(raw.Items[i]);
            }
            else
            {
                adjusted.Items[i].Open.ShouldBe(Math.Round(raw.Items[i].Open * 0.2, 4));
                adjusted.Items[i].High.ShouldBe(Math.Round(raw.Items[i].High * 0.2, 4));
                adjusted.Items[i].Low.ShouldBe(Math.Round(raw.Items[i].Low * 0.2, 4));
                adjusted.Items[i].Close.ShouldBe(Math.Round(raw.Items[i].Close * 0.2, 4));
                adjusted.Items[i].Volume.ShouldBe(raw.Items[i].Volume * 5);
                (adjusted.Items[i].Bars, adjusted.Items[i].Complete, adjusted.Items[i].Ts).ShouldBe((raw.Items[i].Bars, raw.Items[i].Complete, raw.Items[i].Ts));
            }
        }

        adjusted.Adjustment.ShouldNotBeNull();
        (adjusted.Adjustment.Applied, adjusted.Adjustment.Factor).ShouldBe((1, 0.2));
        adjusted.Adjustment.Actions.ShouldBe([new AdjustmentActionView(exDate, "split", "1:5", 0.2)]);

        // The day before the ex-date joins the ex-date smoothly: the adjusted close is within the daily drift of the ex-date's open.
        var before = adjusted.Items.Single(b => DateOnly.FromDateTime(b.Ts.DateTime) == exDate.AddDays(-1 - (exDate.DayOfWeek == DayOfWeek.Monday ? 2 : 0)));
        var onDay = adjusted.Items.Single(b => DateOnly.FromDateTime(b.Ts.DateTime) == exDate);
        (Math.Abs(onDay.Open / before.Close - 1)).ShouldBeLessThan(0.001);
    }

    [Fact]
    public async Task Weeks_months_minutes_and_intraday_bars_are_adjusted_by_the_factor_of_their_date_and_the_month_holding_the_ex_date_is_built_from_the_days()
    {
        var split = ActionFixtures.Series(ratio: 0.2, volumeMultiple: 5);
        var (abb, _) = await LoadAsync("ABB", split);
        await RunAsync();
        var id = (await ListAsync()).Items.Single().Id;
        (await Client.PutJsonAsync($"/api/data/corporate-actions/{id}?acknowledgeCsvAdjusted=true", new { }, Ct)).EnsureSuccessStatusCode();
        var exDate = split[ActionFixtures.EventDay].Date; // Monday 2026-08-31: a week starts there, the month of August ends there

        // The week of the ex-date starts on it (nothing before it), so every bucket of the week and the intraday timeframes lies on one side of it.
        foreach (var timeframe in new[] { "1W", "1h", "30m", "5m", "1m" })
        {
            var raw = await BarsAsync(abb, timeframe, adjusted: false, take: 1000);
            var adjusted = await BarsAsync(abb, timeframe, adjusted: true, take: 1000);

            adjusted.Items.Count.ShouldBe(raw.Items.Count, timeframe);
            for (var i = 0; i < raw.Items.Count; i++)
            {
                var factor = DateOnly.FromDateTime(raw.Items[i].Ts.DateTime) < exDate ? 0.2 : 1.0;
                adjusted.Items[i].Close.ShouldBe(Math.Round(raw.Items[i].Close * factor, 4), $"{timeframe} close at {raw.Items[i].Ts}");
                adjusted.Items[i].Open.ShouldBe(Math.Round(raw.Items[i].Open * factor, 4), $"{timeframe} open at {raw.Items[i].Ts}");
                adjusted.Items[i].Volume.ShouldBe(factor == 1.0 ? raw.Items[i].Volume : raw.Items[i].Volume * 5, $"{timeframe} volume at {raw.Items[i].Ts}");
            }

            adjusted.Adjustment!.Applied.ShouldBe(1, timeframe);
        }

        // August holds the ex-date on its last day: its days before the ex-date are adjusted, the day of the ex-date is not.
        var months = await BarsAsync(abb, "1M", adjusted: true);
        months.Items.Select(b => b.Ts.Month).ShouldBe([9, 8, 7]);
        AssertBucket(months.Items[1], split.Where(d => d.Date.Month == 8), exDate, 0.2);
        AssertBucket(months.Items[2], split.Where(d => d.Date.Month == 7), exDate, 0.2); // wholly before
        AssertBucket(months.Items[0], split.Where(d => d.Date.Month == 9), exDate, 0.2); // wholly after
    }

    [Fact]
    public async Task A_week_and_a_month_that_hold_the_ex_date_in_the_middle_are_built_from_the_adjusted_days_and_the_others_equal_the_raw_ones()
    {
        var series = ActionFixtures.Series();
        var (abb, _) = await LoadAsync("ABB", series);
        await RunAsync();
        var exDate = series[32].Date; // Wednesday 2026-08-19: inside the week of 17 August and inside August
        (await AddAsync(new { instrumentId = abb, exDate = exDate.ToString("yyyy-MM-dd"), kind = "split", numerator = 1, denominator = 5 })).EnsureSuccessStatusCode();

        var rawWeeks = await BarsAsync(abb, "1W", adjusted: false, take: 1000);
        var weeks = await BarsAsync(abb, "1W", adjusted: true, take: 1000);
        var months = await BarsAsync(abb, "1M", adjusted: true, take: 1000);

        weeks.Items.Count.ShouldBe(rawWeeks.Items.Count);
        var holding = weeks.Items.Single(w => DateOnly.FromDateTime(w.Ts.DateTime) == new DateOnly(2026, 8, 17));
        AssertBucket(holding, series.Where(d => d.Date >= new DateOnly(2026, 8, 17) && d.Date <= new DateOnly(2026, 8, 21)), exDate, 0.2);
        holding.Close.ShouldBe(series.Single(d => d.Date == new DateOnly(2026, 8, 21)).Close); // the close of Friday is after the ex-date: raw
        holding.Close.ShouldNotBe(Math.Round(rawWeeks.Items.Single(w => w.Ts == holding.Ts).Close * 0.2, 4)); // not the whole week x 0.2 as before
        foreach (var week in weeks.Items.Where(w => DateOnly.FromDateTime(w.Ts.DateTime) != new DateOnly(2026, 8, 17)))
        {
            var start = DateOnly.FromDateTime(week.Ts.DateTime);
            AssertBucket(week, series.Where(d => d.Date >= start && d.Date < start.AddDays(7)), exDate, 0.2);
        }

        AssertBucket(months.Items.Single(m => m.Ts.Month == 8), series.Where(d => d.Date.Month == 8), exDate, 0.2);
        AssertBucket(months.Items.Single(m => m.Ts.Month == 9), series.Where(d => d.Date.Month == 9), exDate, 0.2);
    }

    [Fact]
    public async Task Without_an_applied_action_adjusted_weeks_and_months_are_identical_to_raw()
    {
        var (abb, _) = await LoadAsync("ABB", ActionFixtures.Series());
        await RunAsync();

        foreach (var timeframe in new[] { "1W", "1M" })
        {
            var raw = await BarsAsync(abb, timeframe, adjusted: false, take: 1000);
            var adjusted = await BarsAsync(abb, timeframe, adjusted: true, take: 1000);

            adjusted.Items.ShouldBe(raw.Items, timeframe);
        }
    }

    /// <summary>A week or month bar must be the day series of its bucket adjusted day by day (the factor applies to days before the ex-date) and then combined.</summary>
    private static void AssertBucket(BarRow bar, IEnumerable<SeriesDay> days, DateOnly exDate, double factor)
    {
        var list = days.OrderBy(d => d.Date).ToList();
        double F(SeriesDay d) => d.Date < exDate ? factor : 1.0;
        bar.Open.ShouldBe(Math.Round(list[0].Open * F(list[0]), 4), $"open of {bar.Ts}");
        bar.High.ShouldBe(list.Max(d => Math.Round(d.High * F(d), 4)), $"high of {bar.Ts}");
        bar.Low.ShouldBe(list.Min(d => Math.Round(d.Low * F(d), 4)), $"low of {bar.Ts}");
        bar.Close.ShouldBe(Math.Round(list[^1].Close * F(list[^1]), 4), $"close of {bar.Ts}");
        bar.Volume.ShouldBe(list.Sum(d => F(d) == 1.0 ? d.Volume : (long)Math.Round(d.Volume / factor, MidpointRounding.AwayFromZero)), $"volume of {bar.Ts}");
    }

    [Fact]
    public async Task Two_applied_actions_multiply_and_an_ignored_one_does_nothing()
    {
        var (abb, series) = await LoadAsync("ABB", ActionFixtures.Series());
        await RunAsync();
        (await AddAsync(new { instrumentId = abb, exDate = series[30].Date.ToString("yyyy-MM-dd"), kind = "split", numerator = 1, denominator = 2 })).EnsureSuccessStatusCode();
        (await AddAsync(new { instrumentId = abb, exDate = series[50].Date.ToString("yyyy-MM-dd"), kind = "bonus", numerator = 1, denominator = 1 })).EnsureSuccessStatusCode();
        using var third = await AddAsync(new { instrumentId = abb, exDate = series[10].Date.ToString("yyyy-MM-dd"), kind = "split", numerator = 1, denominator = 10 });
        var ignoredId = (await third.Content.ReadFromJsonAsync<CorporateActionView>(Ct))!.Id;
        (await Client.PostAsync($"/api/data/corporate-actions/{ignoredId}/ignore", Ct)).EnsureSuccessStatusCode();

        var raw = await BarsAsync(abb, "1D", adjusted: false);
        var adjusted = await BarsAsync(abb, "1D", adjusted: true);

        adjusted.Adjustment!.Applied.ShouldBe(2);
        adjusted.Adjustment.Factor.ShouldBe(0.25);
        adjusted.Adjustment.Actions.Select(a => (a.Kind, a.Ratio)).ShouldBe([("split", "1:2"), ("bonus", "1:1")]);
        var oldest = adjusted.Items[^1];
        oldest.Close.ShouldBe(Math.Round(raw.Items[^1].Close * 0.25, 4));
        oldest.Volume.ShouldBe(raw.Items[^1].Volume * 4);
        var between = adjusted.Items.Single(b => DateOnly.FromDateTime(b.Ts.DateTime) == series[40].Date);
        between.Close.ShouldBe(Math.Round(raw.Items.Single(b => b.Ts == between.Ts).Close * 0.5, 4));
    }

    [Fact]
    public async Task Adding_an_action_by_hand_answers_201_with_the_whole_view_and_audits_it_without_the_note()
    {
        var abb = await SeedAsync("ABB");

        using var response = await AddAsync(new { instrumentId = abb, exDate = "2026-09-01", kind = "split", numerator = 1, denominator = 5, note = "private note" });
        var body = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, body);
        response.Headers.Location!.ToString().ShouldBe($"/api/data/corporate-actions?instrumentId={abb}");
        using var json = JsonDocument.Parse(body);
        json.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(
            ["id", "instrumentId", "symbol", "name", "exDate", "kind", "numerator", "denominator", "ratio", "factor", "status", "source", "note", "createdAt", "decidedAt", "decidedBy", "csvAdjustedThrough"]);
        (json.RootElement.GetProperty("exDate").GetString(), json.RootElement.GetProperty("factor").GetDouble(), json.RootElement.GetProperty("status").GetString(), json.RootElement.GetProperty("source").GetString(), json.RootElement.GetProperty("note").GetString())
            .ShouldBe(("2026-09-01", 0.2, "applied", "manual", "private note"));
        var audit = await AuditAsync();
        audit.ShouldBe([("CorporateActionAdded", "Split 1:5 NSE:ABB ex 2026-09-01 (manual)", ApiFactory.OwnerEmail)]);

        using var page = JsonDocument.Parse(await (await Client.GetAsync("/api/data/corporate-actions", Ct)).Content.ReadAsStringAsync(Ct));
        page.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["items", "page", "pageSize", "total", "pending"]);
    }

    [Fact]
    public async Task The_same_ex_date_and_kind_cannot_be_added_twice_whatever_the_status_and_the_database_agrees()
    {
        var abb = await SeedAsync("ABB");
        var body = new { instrumentId = abb, exDate = "2026-09-01", kind = "split", numerator = 1, denominator = 5 };
        (await AddAsync(body)).EnsureSuccessStatusCode();

        using var second = await AddAsync(new { instrumentId = abb, exDate = "2026-09-01", kind = "split", numerator = 1, denominator = 2 });
        using var bonus = await AddAsync(new { instrumentId = abb, exDate = "2026-09-01", kind = "bonus", numerator = 1, denominator = 1 });

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await second.ReadProblemAsync(Ct)).Code.ShouldBe("data.corporate_action_exists");
        bonus.StatusCode.ShouldBe(HttpStatusCode.Conflict); // the other kind on the same day is refused while the first is applied
        (await bonus.ReadProblemAsync(Ct)).Code.ShouldBe("data.corporate_action_conflicts_same_day");
        await using var connection = await OpenAsync();
        var duplicate = await Should.ThrowAsync<Npgsql.PostgresException>(() => ExecuteAsync(
            connection,
            "INSERT INTO market.corporate_actions (id, instrument_id, ex_date, kind, numerator, denominator, factor, status, source, created_at) VALUES (gen_random_uuid(), @id, '2026-09-01', 'split', 1, 5, 0.2, 'suggested', 'detected', now())",
            ("id", abb)));
        duplicate.SqlState.ShouldBe(Npgsql.PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task Bad_values_answer_400_with_every_field_and_an_unknown_instrument_404()
    {
        var abb = await SeedAsync("ABB");

        using var bad = await AddAsync(new { instrumentId = abb, exDate = "soon", kind = "dividend", numerator = 0, denominator = 3 });
        using var empty = await AddAsync(new { });
        using var unknown = await AddAsync(new { instrumentId = Guid.NewGuid(), exDate = "2026-09-01", kind = "split", numerator = 1, denominator = 5 });
        using var same = await AddAsync(new { instrumentId = abb, exDate = "2026-09-01", kind = "split", numerator = 2, denominator = 2 });

        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await bad.ReadProblemAsync(Ct)).Code.ShouldBe("data.invalid");
        (await bad.ReadFieldErrorsAsync(Ct)).Keys.ShouldBe(["exDate", "kind", "numerator"], ignoreOrder: true);
        (await empty.ReadFieldErrorsAsync(Ct)).Keys.ShouldBe(["instrumentId", "exDate", "kind", "numerator", "denominator"], ignoreOrder: true);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await unknown.ReadProblemAsync(Ct)).Code.ShouldBe("data.instrument_not_found");
        (await same.ReadFieldErrorsAsync(Ct)).Keys.ShouldBe(["denominator"]);
        (await ListAsync()).Total.ShouldBe(0);
    }

    [Fact]
    public async Task Editing_confirming_and_ignoring_answer_404_for_an_unknown_action_and_409_for_a_clash()
    {
        var abb = await SeedAsync("ABB");
        (await AddAsync(new { instrumentId = abb, exDate = "2026-09-01", kind = "split", numerator = 1, denominator = 5 })).EnsureSuccessStatusCode();
        using var other = await AddAsync(new { instrumentId = abb, exDate = "2026-08-01", kind = "split", numerator = 1, denominator = 2 });
        var otherId = (await other.Content.ReadFromJsonAsync<CorporateActionView>(Ct))!.Id;

        using var missingPut = await Client.PutJsonAsync($"/api/data/corporate-actions/{Guid.NewGuid()}", new { }, Ct);
        using var missingIgnore = await Client.PostAsync($"/api/data/corporate-actions/{Guid.NewGuid()}/ignore", Ct);
        using var clash = await Client.PutJsonAsync($"/api/data/corporate-actions/{otherId}?acknowledgeCsvAdjusted=true", new { exDate = "2026-09-01" }, Ct);
        using var invalid = await Client.PutJsonAsync($"/api/data/corporate-actions/{otherId}?acknowledgeCsvAdjusted=true", new { exDate = "x", numerator = -1 }, Ct);
        using var edit = await Client.PutJsonAsync($"/api/data/corporate-actions/{otherId}?acknowledgeCsvAdjusted=true", new { exDate = "2026-08-03", kind = "bonus", numerator = 1, denominator = 1, note = "bonus" }, Ct);

        (missingPut.StatusCode, (await missingPut.ReadProblemAsync(Ct)).Code).ShouldBe((HttpStatusCode.NotFound, "data.corporate_action_not_found"));
        (missingIgnore.StatusCode, (await missingIgnore.ReadProblemAsync(Ct)).Code).ShouldBe((HttpStatusCode.NotFound, "data.corporate_action_not_found"));
        (clash.StatusCode, (await clash.ReadProblemAsync(Ct)).Code).ShouldBe((HttpStatusCode.Conflict, "data.corporate_action_exists"));
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await invalid.ReadFieldErrorsAsync(Ct)).Keys.ShouldBe(["exDate", "numerator"], ignoreOrder: true);
        var view = await edit.ReadAsync<CorporateActionView>(Ct);
        (view.ExDate, view.Kind, view.Ratio, view.Factor, view.Note, view.Status).ShouldBe((new DateOnly(2026, 8, 3), "bonus", "1:1", 0.5, "bonus", "applied"));
        var audit = await AuditAsync();
        audit.Select(a => a.Event).ShouldBe(["CorporateActionAdded", "CorporateActionAdded", "CorporateActionEdited"]);
        audit[2].Detail.ShouldBe("Bonus 1:1 NSE:ABB ex 2026-08-03 (manual) (was split 1:2 ex 2026-08-01)");
    }

    [Fact]
    public async Task Confirming_twice_and_ignoring_twice_are_audited_once_each_and_the_list_filters_work()
    {
        await LoadAsync("ABB", ActionFixtures.Series(ratio: 0.2, volumeMultiple: 5));
        var tcs = await SeedAsync("TCS");
        await RunAsync();
        (await AddAsync(new { instrumentId = tcs, exDate = "2026-08-01", kind = "bonus", numerator = 1, denominator = 2 })).EnsureSuccessStatusCode();
        var suggestedId = (await ListAsync("?status=suggested")).Items.Single().Id;

        (await Client.PutJsonAsync($"/api/data/corporate-actions/{suggestedId}?acknowledgeCsvAdjusted=true", new { }, Ct)).EnsureSuccessStatusCode();
        (await Client.PutJsonAsync($"/api/data/corporate-actions/{suggestedId}?acknowledgeCsvAdjusted=true", new { }, Ct)).EnsureSuccessStatusCode();
        (await Client.PostAsync($"/api/data/corporate-actions/{suggestedId}/ignore", Ct)).EnsureSuccessStatusCode();
        (await Client.PostAsync($"/api/data/corporate-actions/{suggestedId}/ignore", Ct)).EnsureSuccessStatusCode();

        (await AuditAsync()).Select(a => a.Event).ShouldBe(["CorporateActionAdded", "CorporateActionConfirmed", "CorporateActionIgnored"]);
        (await ListAsync("?status=ignored")).Items.Single().Id.ShouldBe(suggestedId);
        (await ListAsync("?status=applied")).Items.Single().Symbol.ShouldBe("NSE:TCS");
        (await ListAsync($"?instrumentId={tcs}")).Total.ShouldBe(1);
        var paged = await ListAsync("?page=2&pageSize=1");
        (paged.Items.Count, paged.Total, paged.Page).ShouldBe((1, 2, 2));
        using var bad = await Client.GetAsync("/api/data/corporate-actions?status=weird&pageSize=999", Ct);
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await bad.ReadFieldErrorsAsync(Ct)).Keys.ShouldBe(["status", "pageSize"], ignoreOrder: true);
    }

    [Fact]
    public async Task The_csv_import_previews_then_imports_as_a_text_body_and_a_second_import_skips_the_duplicates()
    {
        var abb = await SeedAsync("ABB");
        await SeedAsync("TCS");
        const string Text = "symbol,exDate,kind,numerator,denominator\nABB,2026-09-01,split,1,5\nNSE:TCS,2026-08-03,bonus,1,1\nNOPE,2026-08-03,split,1,2\nABB,31/08/2026,split,1,2\n";

        using var preview = await ImportAsync(Text, preview: true);
        var previewed = await preview.ReadAsync<ImportCorporateActionsResult>(Ct);
        (await ListAsync()).Total.ShouldBe(0);
        using var real = await ImportAsync(Text);
        var imported = await real.ReadAsync<ImportCorporateActionsResult>(Ct);
        using var again = await ImportAsync(Text);
        var repeated = await again.ReadAsync<ImportCorporateActionsResult>(Ct);

        (previewed.Preview, previewed.Lines, previewed.Importable, previewed.Imported, previewed.Invalid).ShouldBe((true, 4, 2, 0, 1));
        previewed.UnknownSymbols.ShouldBe(["NSE:NOPE"]);
        previewed.Errors.ShouldBe([new ImportLineError(5, "exDate: Use the date as yyyy-MM-dd.")]);
        (imported.Preview, imported.Importable, imported.Imported, imported.Duplicates).ShouldBe((false, 2, 2, 0));
        (repeated.Importable, repeated.Imported, repeated.Duplicates, repeated.Invalid).ShouldBe((0, 0, 2, 1));
        var page = await ListAsync();
        page.Items.Select(i => (i.Symbol, i.Source, i.Status)).ShouldBe([("NSE:ABB", "csv", "applied"), ("NSE:TCS", "csv", "applied")]);
        page.Items.Single(i => i.InstrumentId == abb).Factor.ShouldBe(0.2);
        var audit = await AuditAsync();
        audit.ShouldBe([("CorporateActionsImported", "CSV import: 2 added, 0 duplicates skipped, 1 unknown symbols, 1 invalid lines; NSE:ABB, NSE:TCS", ApiFactory.OwnerEmail)]);
    }

    [Fact]
    public async Task The_csv_import_also_takes_a_multipart_file_and_refuses_empty_oversized_and_headerless_text()
    {
        await SeedAsync("ABB");
        using var form = new MultipartFormDataContent { { new StringContent("symbol,exDate,kind,numerator,denominator\nABB,2026-09-01,split,1,5\n", Encoding.UTF8, "text/csv"), "file", "actions.csv" } };

        using var multipart = await Client.PostAsync(new Uri("/api/data/corporate-actions/import", UriKind.Relative), form, Ct);
        using var empty = await ImportAsync(string.Empty);
        using var headerless = await ImportAsync("ABB,2026-09-01,split,1,5\n");
        using var emptyForm = await Client.PostAsync(new Uri("/api/data/corporate-actions/import", UriKind.Relative), new MultipartFormDataContent(), Ct);
        using var tooMany = await ImportAsync("symbol,exDate,kind,numerator,denominator\n" + string.Concat(Enumerable.Repeat("ABB,2026-09-01,split,1,5\n", 5_001)), preview: true);

        multipart.StatusCode.ShouldBe(HttpStatusCode.OK, await multipart.Content.ReadAsStringAsync(Ct));
        (await multipart.ReadAsync<ImportCorporateActionsResult>(Ct)).Imported.ShouldBe(1);
        (await empty.ReadFieldErrorsAsync(Ct)).Keys.ShouldBe(["csv"]);
        (await headerless.ReadFieldErrorsAsync(Ct)).Keys.ShouldBe(["header"]);
        (await emptyForm.ReadFieldErrorsAsync(Ct)).Keys.ShouldBe(["csv"]);
        tooMany.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await tooMany.ReadProblemAsync(Ct)).Code.ShouldBe("data.invalid");
        (await ListAsync()).Total.ShouldBe(1);
    }

    private async Task<HttpResponseMessage> ImportAsync(string text, bool preview = false) =>
        await Client.PostAsync(new Uri("/api/data/corporate-actions/import" + (preview ? "?preview=true" : string.Empty), UriKind.Relative), new StringContent(text, Encoding.UTF8, "text/csv"), Ct);

    [Fact]
    public async Task The_new_notice_and_counts_are_additive_to_the_existing_wire_format()
    {
        var abb = await SeedAsync("ABB");
        (await AddAsync(new { instrumentId = abb, exDate = "2026-09-01", kind = "split", numerator = 1, denominator = 5 })).EnsureSuccessStatusCode();

        using var overview = JsonDocument.Parse(await (await Client.GetAsync("/api/data/overview", Ct)).Content.ReadAsStringAsync(Ct));
        using var quality = JsonDocument.Parse(await (await Client.GetAsync("/api/data/quality/overview", Ct)).Content.ReadAsStringAsync(Ct));
        using var bars = JsonDocument.Parse(await (await Client.GetAsync($"/api/data/instruments/{abb}/bars", Ct)).Content.ReadAsStringAsync(Ct));

        overview.RootElement.EnumerateObject().Select(p => p.Name).Last().ShouldBe("pendingCorporateActions");
        quality.RootElement.EnumerateObject().Select(p => p.Name).Last().ShouldBe("pendingCorporateActions");
        bars.RootElement.GetProperty("adjustment").ValueKind.ShouldBe(JsonValueKind.Null);
    }
}
