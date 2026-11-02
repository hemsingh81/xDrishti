using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Filings;
using XDrishti.Infrastructure.Filings;
using static XDrishti.Api.IntegrationTests.Filings.CalendarTestKit;

namespace XDrishti.Api.IntegrationTests.Filings;

/// <summary>
/// P15-04 end to end on a real TimescaleDB and a real drop folder (criteria 1, 2, 7, 8, 9, 10 and the load-time limit): a synthetic inbox with a board-meeting csv and a shareholding csv goes through the real source and the two loaders; a rerun and a parallel run change
/// nothing; the file is untouched; bad rows are counted; a 4,300-row csv loads within the time limit.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class CalendarFolderTests(ApiFactory factory) : DataTestBase(factory)
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "xd-calendar-it-" + Guid.NewGuid().ToString("N"));

    private string Inbox => Path.Combine(_root, "inbox");

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        Directory.CreateDirectory(Inbox);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(Inbox, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    public override async ValueTask DisposeAsync()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }

        await base.DisposeAsync();
    }

    private void Drop(string name, string content, string kind)
    {
        File.WriteAllText(Path.Combine(Inbox, name), content, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(Inbox, name + ".meta.json"), $$"""{"schema":1,"symbol":"*","kind":"{{kind}}"}""", new UTF8Encoding(false));
    }

    private (LoadCalendarHandler Calendar, LoadShareholdingHandler Shareholding, IServiceScope Scope) Handlers()
    {
        var scope = Factory.Services.CreateScope();
        var p = scope.ServiceProvider;
        var source = new FolderDropFilingsSource(Options.Create(new FilingsOptions { InboxPath = Inbox, FilesPath = Path.Combine(_root, "files") }), TimeProvider.System, NullLogger<FolderDropFilingsSource>.Instance);
        var clock = p.GetRequiredService<TimeProvider>();
        return (
            new LoadCalendarHandler(source, p.GetRequiredService<IFilingInstruments>(), p.GetRequiredService<ICalendarStore>(), clock, NullLogger<LoadCalendarHandler>.Instance),
            new LoadShareholdingHandler(source, p.GetRequiredService<IFilingInstruments>(), p.GetRequiredService<IShareholdingStore>(), clock, NullLogger<LoadShareholdingHandler>.Instance),
            scope);
    }

    private async Task<RowLoadSummary> LoadCalendarAsync()
    {
        var (calendar, _, scope) = Handlers();
        using (scope)
        {
            return (await calendar.HandleAsync(new LoadCalendarCommand(), Ct)).Value;
        }
    }

    private async Task<JsonDocument> ApiAsync(string path)
    {
        using var response = await Client.GetAsync(new Uri(path, UriKind.Relative), Ct);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task A_synthetic_drop_folder_loads_once_the_rerun_and_a_parallel_run_change_nothing_and_the_inbox_is_untouched()
    {
        await SeedAsync("RELIANCE");
        await SeedAsync("TCS");
        var meetings = MeetingHeader + string.Join("\n",
            "RELIANCE,2026-09-30,2026-10-14,results,scheduled,2026-09-28T11:00:00+05:30",
            "RELIANCE,2026-09-30,2026-10-21,results,rescheduled,2026-10-02T09:30:00+05:30",
            "TCS,2026-09-30,2026-10-10,results,scheduled,2026-09-25T10:00:00+05:30",
            "TCS,2026-09-30,2026-10-10,other,scheduled,",
            "NOSUCH,2026-09-30,2026-10-14,results,scheduled,2026-09-28T11:00:00+05:30",
            "TCS,2026-09-30,not-a-date,results,scheduled,2026-09-28T11:00:00+05:30");
        var holding = HoldingHeader + string.Join("\n",
            "RELIANCE,2026-09-30,promoter,50.1,12,2026-10-10T18:00:00+05:30",
            "RELIANCE,2026-09-30,fii,20,,2026-10-10T18:00:00+05:30",
            "RELIANCE,2026-09-30,dii,15,,2026-10-10T18:00:00+05:30",
            "RELIANCE,2026-09-30,public,14.9,,2026-10-10T18:00:00+05:30");
        Drop("meetings.csv", meetings, "board_meeting");
        Drop("holding.csv", holding, "shareholding");
        var before = Directory.GetFiles(Inbox).Select(f => (Path.GetFileName(f), File.ReadAllText(f))).Order().ToList();

        var (calendar, shareholding, scope) = Handlers();
        using (scope)
        {
            var first = (await calendar.HandleAsync(new LoadCalendarCommand(), Ct)).Value;
            var firstHolding = (await shareholding.HandleAsync(new LoadShareholdingCommand(), Ct)).Value;
            var second = (await calendar.HandleAsync(new LoadCalendarCommand(), Ct)).Value;
            var both = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(async () => (await calendar.HandleAsync(new LoadCalendarCommand(), Ct)).Value, Ct)));

            (first.Files, first.Loaded, first.Revisions, first.Unavailable, first.Duplicate).ShouldBe((1, 4, 1, 1, 0));
            first.Rejected.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => $"{r.Key}={r.Value}").ShouldBe(["bad_row=1", "unknown_instrument=1"]);
            (firstHolding.Loaded, firstHolding.SumOff).ShouldBe((1, 0));
            (second.Loaded, second.Duplicate).ShouldBe((0, 4));
            both.ShouldAllBe(s => s.Loaded == 0 && s.Duplicate == 4);
        }

        (await ScalarAsync<long>("SELECT count(*) FROM filings.calendar")).ShouldBe(4);
        (await ScalarAsync<long>("SELECT count(*) FROM filings.shareholding")).ShouldBe(4);
        (await ScalarAsync<long>("SELECT count(*) FROM filings.filings")).ShouldBe(5);
        Directory.GetFiles(Inbox).Select(f => (Path.GetFileName(f), File.ReadAllText(f))).Order().ToList().ShouldBe(before);

        // The route: Monday 2026-10-12 12:00 IST (the pinned clock). RELIANCE moved to 10-21 (published 10-02, long visible); TCS 10-10 (Saturday) has its event session on Monday 10-12 itself.
        using var view = await ApiAsync("/api/calendar/results");
        var meetingsJson = view.RootElement.GetProperty("meetings").EnumerateArray().ToList();
        meetingsJson.Select(m => (m.GetProperty("symbol").GetString(), m.GetProperty("eventSession").GetString(), m.GetProperty("days").GetInt32(), m.GetProperty("revision").GetInt32())).ShouldBe([("TCS", "2026-10-12", 0, 1), ("RELIANCE", "2026-10-21", 7, 2)]);
        view.RootElement.GetProperty("instrumentsWithoutEntry").GetProperty("count").GetInt32().ShouldBe(0);
        using var earlier = await ApiAsync("/api/calendar/results?asOf=2026-10-01T12:00:00%2B05:30");
        earlier.RootElement.GetProperty("meetings").EnumerateArray().Select(m => m.GetProperty("eventSession").GetString()).ShouldBe(["2026-10-12", "2026-10-14"]);
    }

    [Fact]
    public async Task A_4300_row_calendar_csv_loads_within_five_seconds_and_a_reload_inserts_nothing()
    {
        var symbols = Enumerable.Range(0, 215).Select(i => "S" + i.ToString("000", CultureInfo.InvariantCulture)).ToList();
        foreach (var symbol in symbols)
        {
            await SeedAsync(symbol);
        }

        var lines = new List<string>();
        foreach (var symbol in symbols.Take(215))
        {
            for (var q = 0; q < 20; q++)
            {
                var period = new DateOnly(2021, 3, 31).AddMonths(3 * q);
                period = new DateOnly(period.Year, period.Month, DateTime.DaysInMonth(period.Year, period.Month));
                lines.Add($"{symbol},{period:yyyy-MM-dd},{period.AddDays(40):yyyy-MM-dd},results,scheduled,{period.AddDays(30):yyyy-MM-dd}T11:00:00+05:30");
            }
        }

        lines.Count.ShouldBe(4300);
        Drop("big.csv", MeetingHeader + string.Join("\n", lines), "board_meeting");
        var watch = Stopwatch.StartNew();

        var first = await LoadCalendarAsync();
        watch.Stop();
        var second = await LoadCalendarAsync();

        (first.Loaded, first.Rejected.Count).ShouldBe((4300, 0));
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5), $"loading took {watch.Elapsed}");
        (second.Loaded, second.Duplicate).ShouldBe((0, 4300));
    }

    [Fact]
    public async Task The_batch_answer_for_214_instruments_is_fast_and_a_single_answer_is_fast()
    {
        var ids = new List<Guid>();
        for (var i = 0; i < 214; i++)
        {
            ids.Add(await SeedAsync("B" + i.ToString("000", CultureInfo.InvariantCulture)));
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<ICalendarStore>();
            for (var i = 0; i < ids.Count; i += 3)
            {
                await store.InsertAsync(Meeting(ids[i], "b" + i, new DateOnly(2026, 10, 14), At(9, 28, 11)), Ct);
            }
        }

        using var read = Factory.Services.CreateScope();
        var port = read.ServiceProvider.GetRequiredService<IResultsCalendar>();
        _ = await port.DaysToResults(ids, At(10, 12, 12), Ct);
        var watch = Stopwatch.StartNew();
        var batch = await port.DaysToResults(ids, At(10, 12, 12), Ct);
        var batchTime = watch.Elapsed;
        watch.Restart();
        var one = await port.DaysToResults(ids[0], At(10, 12, 12), Ct);
        var oneTime = watch.Elapsed;

        batch.Count.ShouldBe(214);
        batch.Values.Count(a => a.State == "value").ShouldBe(72);
        one.Days.ShouldBe(2);
        batchTime.ShouldBeLessThan(TimeSpan.FromMilliseconds(100));
        oneTime.ShouldBeLessThan(TimeSpan.FromMilliseconds(50));
    }
}
