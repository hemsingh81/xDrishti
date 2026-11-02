using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Domain.Filings;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Infrastructure.FilingArchive;
using static XDrishti.Api.IntegrationTests.Filings.CalendarTestKit;

namespace XDrishti.Api.IntegrationTests.Filings;

/// <summary>
/// P15-04 on a real TimescaleDB (criteria 1, 3, 5, 6, 7, 10): the calendar and shareholding stores (one row or group is one transaction, idempotent, the supersede chain per purpose, parallel writers), the rows read back through the ports, and the answers of the
/// ports equal to the domain rules over all stored rows at many instants, whatever the insert order.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class RowStoresDatabaseTests(ApiFactory factory) : DataTestBase(factory)
{
    private Guid _reliance;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        _reliance = await SeedAsync("RELIANCE");
    }

    private async Task<T> WithAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        using var scope = Factory.Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    private Task<FilingInsertResult> InsertAsync(NewMeeting meeting) => WithAsync(p => p.GetRequiredService<ICalendarStore>().InsertAsync(meeting, Ct).AsTask());

    private Task<FilingInsertResult> InsertAsync(NewShareholding group) => WithAsync(p => p.GetRequiredService<IShareholdingStore>().InsertAsync(group, Ct).AsTask());

    private Task<ResultsAnswer> DaysAsync(Guid instrument, DateTimeOffset t) => WithAsync(p => p.GetRequiredService<IResultsCalendar>().DaysToResults(instrument, t, Ct).AsTask());

    private Task<IReadOnlyList<CalendarRow>> AllRowsAsync() => WithAsync(p => p.GetRequiredService<ICalendarRows>().ResultsRowsAsync(null, new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero), Ct).AsTask());

    [Fact]
    public async Task A_row_is_one_envelope_and_one_calendar_row_with_its_own_time_and_a_second_load_inserts_nothing()
    {
        var meeting = Meeting(_reliance, "a", new DateOnly(2026, 8, 12), At(8, 3, 11));

        var first = await InsertAsync(meeting);
        var again = await InsertAsync(meeting);

        (first.Outcome, again.Outcome).ShouldBe((FilingInsertOutcome.Stored, FilingInsertOutcome.Duplicate));
        (await ScalarAsync<long>("SELECT count(*) FROM filings.filings")).ShouldBe(1);
        (await ScalarAsync<long>("SELECT count(*) FROM filings.calendar")).ShouldBe(1);
        (await ScalarAsync<string>("SELECT kind || '/' || broadcast_state || '/' || to_char(broadcast_ts AT TIME ZONE 'UTC', 'YYYY-MM-DD HH24:MI') FROM filings.filings")).ShouldBe("board_meeting/ok/2026-08-03 05:30");
        (await ScalarAsync<string>("SELECT meeting_date || '/' || purpose || '/' || status || '/' || period_end FROM filings.calendar")).ShouldBe("2026-08-12/results/scheduled/2026-06-30");
    }

    [Fact]
    public async Task A_row_without_an_intimation_time_is_stored_unavailable_and_is_never_read_back_for_a_question()
    {
        await InsertAsync(Meeting(_reliance, "undated", new DateOnly(2026, 8, 12), null));

        var answer = await DaysAsync(_reliance, At(8, 7, 10));

        (await ScalarAsync<string>("SELECT broadcast_state || '/' || unavailable_reason FROM filings.filings")).ShouldBe("unavailable/missing");
        (answer.State, answer.Reason).ShouldBe(("unavailable", "no_entry"));
        (await AllRowsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_chain_is_kept_per_purpose_a_later_time_supersedes_an_earlier_one_stays_unlinked_and_an_equal_time_is_ambiguous()
    {
        var first = Meeting(_reliance, "a", new DateOnly(2026, 8, 12), At(8, 3, 11));
        await InsertAsync(first);

        var moved = await InsertAsync(Meeting(_reliance, "b", new DateOnly(2026, 8, 20), At(8, 5, 10), "rescheduled"));
        var early = await InsertAsync(Meeting(_reliance, "c", new DateOnly(2026, 8, 1), At(7, 20, 10)));
        var tie = await InsertAsync(Meeting(_reliance, "d", new DateOnly(2026, 8, 21), At(8, 5, 10)));
        var other = await InsertAsync(Meeting(_reliance, "e", new DateOnly(2026, 8, 12), At(8, 6, 10), purpose: "other"));

        (moved.Outcome, moved.Supersedes).ShouldBe((FilingInsertOutcome.Stored, first.Filing.Id));
        (early.Outcome, early.Supersedes).ShouldBe((FilingInsertOutcome.StoredOutOfOrder, null));
        tie.Outcome.ShouldBe(FilingInsertOutcome.AmbiguousOrder);
        (other.Outcome, other.Supersedes).ShouldBe((FilingInsertOutcome.Stored, null));
        (await ScalarAsync<long>("SELECT count(*) FROM filings.calendar")).ShouldBe(4);
    }

    [Fact]
    public async Task Eight_parallel_loads_of_the_same_row_store_it_once_and_parallel_reschedules_make_one_linear_chain()
    {
        var row = Meeting(_reliance, "p", new DateOnly(2026, 8, 12), At(8, 3, 11));
        var same = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => InsertAsync(row), Ct)));

        var revisions = Enumerable.Range(1, 6).Select(i => Meeting(_reliance, "r" + i, new DateOnly(2026, 8, 12).AddDays(i), At(8, 3, 11).AddDays(i), "rescheduled")).ToList();
        var results = await Task.WhenAll(revisions.Select(r => Task.Run(() => InsertAsync(r), Ct)));

        same.Count(r => r.Outcome == FilingInsertOutcome.Stored).ShouldBe(1);
        same.Count(r => r.Outcome == FilingInsertOutcome.Duplicate).ShouldBe(7);
        results.ShouldAllBe(r => r.Outcome == FilingInsertOutcome.Stored || r.Outcome == FilingInsertOutcome.StoredOutOfOrder);
        (await ScalarAsync<long>("SELECT count(*) FROM filings.calendar")).ShouldBe(7);
        (await ScalarAsync<long>("SELECT count(*) FROM (SELECT supersedes FROM filings.filings WHERE supersedes IS NOT NULL GROUP BY supersedes HAVING count(*) > 1) x")).ShouldBe(0);
        (await DaysAsync(_reliance, At(8, 10))).MeetingDate.ShouldBe(new DateOnly(2026, 8, 18));
    }

    [Fact]
    public async Task The_look_ahead_rule_holds_in_the_database_at_the_second_and_for_a_reschedule()
    {
        await InsertAsync(Meeting(_reliance, "a", new DateOnly(2026, 8, 12), At(8, 3, 11)));
        await InsertAsync(Meeting(_reliance, "b", new DateOnly(2026, 8, 20), At(8, 5, 10), "rescheduled"));

        var before = await DaysAsync(_reliance, new DateTimeOffset(2026, 8, 3, 11, 4, 59, Ist));
        var at = await DaysAsync(_reliance, new DateTimeOffset(2026, 8, 3, 11, 5, 0, Ist));
        var friday = await DaysAsync(_reliance, At(8, 7, 10));
        var movedBefore = await DaysAsync(_reliance, new DateTimeOffset(2026, 8, 5, 10, 4, 59, Ist));
        var movedAt = await DaysAsync(_reliance, new DateTimeOffset(2026, 8, 5, 10, 5, 0, Ist));

        (before.State, before.Reason).ShouldBe(("unavailable", "no_entry"));
        (at.State, at.Days).ShouldBe(("value", 7));
        friday.Days.ShouldBe(9);
        movedBefore.EventSession.ShouldBe(new DateOnly(2026, 8, 12));
        movedAt.EventSession.ShouldBe(new DateOnly(2026, 8, 20));
    }

    [Fact]
    public async Task The_port_equals_the_domain_rule_over_all_stored_rows_at_many_instants_for_every_insert_order()
    {
        var tcs = await SeedAsync("TCS");
        var rows = new List<(string Tag, DateOnly Period, DateOnly Meeting, DateTimeOffset? Ts, string Status, string Purpose)>
        {
            ("a1", new DateOnly(2026, 3, 31), new DateOnly(2026, 5, 20), At(5, 1, 10), "scheduled", "results"),
            ("a2", Q1, new DateOnly(2026, 8, 12), At(8, 3, 11), "scheduled", "results"),
            ("a3", Q1, new DateOnly(2026, 8, 20), At(8, 5, 10), "rescheduled", "results"),
            ("a4", Q1, new DateOnly(2026, 8, 14), At(8, 4, 10), "rescheduled", "results"),
            ("a5", Q1, new DateOnly(2026, 8, 25), At(8, 9, 10), "cancelled", "results"),
            ("a6", Q1, new DateOnly(2026, 9, 2), At(8, 12, 10), "scheduled", "results"),
            ("a7", new DateOnly(2026, 9, 30), new DateOnly(2026, 11, 5), null, "scheduled", "results"),
            ("a8", new DateOnly(2026, 9, 30), new DateOnly(2026, 11, 6), At(10, 20, 10), "scheduled", "other"),
            ("a9", new DateOnly(2026, 9, 30), new DateOnly(2026, 11, 7), At(10, 21, 10), "scheduled", "results"),
        };
        foreach (var r in rows)
        {
            await InsertAsync(Meeting(_reliance, r.Tag, r.Meeting, r.Ts, r.Status, r.Purpose, r.Period));
        }

        foreach (var r in rows.AsEnumerable().Reverse())
        {
            await InsertAsync(Meeting(tcs, r.Tag + "-tcs", r.Meeting, r.Ts, r.Status, r.Purpose, r.Period));
        }

        var stored = (await AllRowsAsync()).Where(r => r.Filing.InstrumentId == _reliance).ToList();
        var instants = new List<DateTimeOffset>();
        var random = new Random(15);
        for (var i = 0; i < 60; i++)
        {
            instants.Add(At(4, 25).AddMinutes(random.Next(0, 200 * 24 * 60)));
        }

        foreach (var row in stored)
        {
            instants.Add(row.Filing.BroadcastTs!.Value + TimeSpan.FromMinutes(5));
            instants.Add(row.Filing.BroadcastTs!.Value + TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1));
        }

        var calendar = new TradingCalendar();
        var lag = TimeSpan.FromMinutes(5);
        var seen = 0;
        foreach (var t in instants)
        {
            var viaDb = await DaysAsync(_reliance, t);
            var viaRule = ResultsDays.Compute(stored, t, lag, calendar);
            var viaOther = await DaysAsync(tcs, t);

            viaDb.ShouldBe(viaRule, $"t {t:O}");
            viaDb.ShouldBe(viaOther, $"t {t:O} (same rows inserted in the other order)");
            seen += viaDb.State == "value" ? 1 : 0;
        }

        seen.ShouldBeGreaterThan(20);
        seen.ShouldBeLessThan(instants.Count);
        var batch = await WithAsync(p => p.GetRequiredService<IResultsCalendar>().DaysToResults([_reliance, tcs], At(8, 7, 10), Ct).AsTask());
        batch[_reliance].ShouldBe(batch[tcs]);
    }

    [Fact]
    public async Task A_group_is_one_envelope_with_its_lines_a_blank_pct_is_unavailable_and_a_later_group_supersedes_from_its_own_time()
    {
        var first = Holding(_reliance, "q1", Q1, At(7, 20, 18), 50.25m, 12.5m);
        var second = Holding(_reliance, "q1b", Q1, At(8, 20, 18), 51m);
        var older = Holding(_reliance, "q4", new DateOnly(2026, 3, 31), At(4, 20, 18), 49m);

        var one = await InsertAsync(first);
        var again = await InsertAsync(first);
        var two = await InsertAsync(second);
        var prev = await InsertAsync(older);
        var tie = await InsertAsync(Holding(_reliance, "tie", Q1, At(8, 20, 18), 60m));

        (one.Outcome, again.Outcome, two.Outcome, two.Supersedes, prev.Outcome).ShouldBe((FilingInsertOutcome.Stored, FilingInsertOutcome.Duplicate, FilingInsertOutcome.Stored, first.Filing.Id, FilingInsertOutcome.Stored));
        tie.Outcome.ShouldBe(FilingInsertOutcome.AmbiguousOrder);
        (await ScalarAsync<long>("SELECT count(*) FROM filings.shareholding")).ShouldBe(9);
        (await ScalarAsync<string>("SELECT state || '/' || coalesce(pct::text, 'null') FROM filings.shareholding WHERE category = 'dii' LIMIT 1")).ShouldBe("unavailable/null");
        (await ScalarAsync<string>($"SELECT pct || '/' || pledged_pct FROM filings.shareholding WHERE filing_id = '{first.Filing.Id}' AND category = 'promoter'")).ShouldBe("50.25/12.5");
    }

    [Fact]
    public async Task The_shareholding_port_equals_the_domain_rule_and_returns_the_current_and_the_previous_quarter()
    {
        await InsertAsync(Holding(_reliance, "q4", new DateOnly(2026, 3, 31), At(4, 20, 18), 49m));
        await InsertAsync(Holding(_reliance, "q1", Q1, At(7, 20, 18), 50m));
        await InsertAsync(Holding(_reliance, "q1b", Q1, At(8, 20, 18), 51m));
        await InsertAsync(Holding(_reliance, "undated", new DateOnly(2026, 9, 30), null, 70m));

        async Task<ShareholdingAnswer> LatestAsync(DateTimeOffset t) => await WithAsync(p => p.GetRequiredService<IShareholdingAsOf>().Latest(_reliance, t, Ct).AsTask());

        var none = await LatestAsync(At(4, 20, 18));
        var q4 = await LatestAsync(At(4, 20, 18, 5));
        var q1 = await LatestAsync(At(8, 1));
        var restated = await LatestAsync(At(8, 20, 18, 5));
        var future = await LatestAsync(At(12, 1));

        (none.State, none.Reason).ShouldBe(("unavailable", "no_entry"));
        (q4.Current!.PeriodEnd, q4.Previous).ShouldBe((new DateOnly(2026, 3, 31), null));
        (q1.Current!.PeriodEnd, q1.Previous!.PeriodEnd, q1.Current.Lines[0].Pct).ShouldBe((Q1, new DateOnly(2026, 3, 31), 50m));
        restated.Current!.Lines[0].Pct.ShouldBe(51m);
        future.Current!.PeriodEnd.ShouldBe(Q1);
        restated.Current.Lines.Select(l => l.Category).ShouldBe(["promoter", "fii", "dii"]);
        restated.Current.Lines[2].IsValue.ShouldBeFalse();
    }

    private Task<FilingInsertResult> InsertFileAsync(NewFiling filing) => WithAsync(p => p.GetRequiredService<IFilingStore>().InsertAsync(filing, Ct).AsTask());

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_shareholding_file_envelope_of_the_same_period_is_another_group_than_the_csv_rows_in_either_order(bool fileFirst)
    {
        var file = FilingTestKit.Ok(_reliance, "shareholding.pdf", Q1, At(7, 20, 18), FilingKind.Shareholding);
        var csv = Holding(_reliance, "q1", Q1, At(7, 20, 18), 50m);
        var revision = Holding(_reliance, "q1b", Q1, At(8, 20, 18), 51m);
        FilingInsertResult fileResult;
        FilingInsertResult csvResult;
        if (fileFirst)
        {
            fileResult = await InsertFileAsync(file);
            csvResult = await InsertAsync(csv);
        }
        else
        {
            csvResult = await InsertAsync(csv);
            fileResult = await InsertFileAsync(file);
        }

        var revised = await InsertAsync(revision);
        var answer = await WithAsync(p => p.GetRequiredService<IShareholdingAsOf>().Latest(_reliance, At(7, 21), Ct).AsTask());
        var later = await WithAsync(p => p.GetRequiredService<IShareholdingAsOf>().Latest(_reliance, At(9, 1), Ct).AsTask());

        (fileResult.Outcome, csvResult.Outcome).ShouldBe((FilingInsertOutcome.Stored, FilingInsertOutcome.Stored));
        (revised.Outcome, revised.Supersedes).ShouldBe((FilingInsertOutcome.Stored, csv.Filing.Id));
        answer.Current!.Lines.Count.ShouldBe(3);
        later.Current!.Lines[0].Pct.ShouldBe(51m);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_board_meeting_file_envelope_of_the_same_period_never_joins_the_calendar_chain_and_a_reschedule_still_loads(bool fileFirst)
    {
        var file = FilingTestKit.Ok(_reliance, "board-meeting.pdf", Q1, At(8, 4, 10), FilingKind.BoardMeeting);
        var first = Meeting(_reliance, "a", new DateOnly(2026, 8, 12), At(8, 3, 11));
        var moved = Meeting(_reliance, "b", new DateOnly(2026, 8, 20), At(8, 5, 10), "rescheduled");
        if (fileFirst)
        {
            await InsertFileAsync(file);
        }

        var one = await InsertAsync(first);
        if (!fileFirst)
        {
            await InsertFileAsync(file);
        }

        var two = await InsertAsync(moved);
        var again = await InsertAsync(moved);

        (one.Outcome, two.Outcome, two.Supersedes, again.Outcome).ShouldBe((FilingInsertOutcome.Stored, FilingInsertOutcome.Stored, first.Filing.Id, FilingInsertOutcome.Duplicate));
        (await DaysAsync(_reliance, At(8, 6, 10))).EventSession.ShouldBe(new DateOnly(2026, 8, 20));
        (await ScalarAsync<long>("SELECT count(*) FROM filings.filings WHERE supersedes IS NOT NULL")).ShouldBe(1);
    }

    [Fact]
    public async Task A_second_revision_of_one_row_that_the_database_refuses_comes_back_as_a_refusal_and_not_an_exception()
    {
        var first = Meeting(_reliance, "a", new DateOnly(2026, 8, 12), At(8, 3, 11));
        await InsertAsync(first);
        await InsertAsync(Meeting(_reliance, "b", new DateOnly(2026, 8, 20), At(8, 5, 10), "rescheduled"));
        var clash = Meeting(_reliance, "c", new DateOnly(2026, 8, 21), At(8, 6, 10), "rescheduled").Filing;
        using var scope = Factory.Services.CreateScope();
        var dataSource = scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var connection = await dataSource.OpenConnectionAsync(Ct);
        await using var transaction = await connection.BeginTransactionAsync(Ct);

        var written = await RowStoreBatches.TryWriteAsync(connection, transaction, [RowStoreBatches.Envelope(clash, first.Filing.Id)], Ct);

        written.ShouldBeNull();
    }
}
