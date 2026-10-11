using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Market.Refresh;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Tests.Market;

public sealed class ExecuteRefreshHandlerTests
{
    private readonly MarketFixture _f = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_refresh_imports_the_in_scope_rows_and_records_what_the_catalog_did()
    {
        _f.Data.Result = new CatalogApplied(40, 10, 3, 2, 7, 52);
        var run = await _f.RunAsync(_f.Register());

        run.Status.ShouldBe(RefreshStatus.Succeeded);
        run.Phase.ShouldBeNull();
        run.FinishedAt.ShouldBe(_f.Clock.GetUtcNow());
        (run.Added, run.Updated, run.Retired, run.Revived, run.Unchanged, run.TotalActive).ShouldBe((40, 10, 3, 2, 7, 52));
        run.RowsRead.ShouldBe(75);
        run.SkippedByScope.ShouldBe(14 + 1, "rows out of scope plus the expired SBIN future");
        run.Collisions.ShouldBe(0);
        run.KeptByType["share"].ShouldBe(30);
        run.KeptByType["future"].ShouldBe(23);
        run.FnoUnderlyings.ShouldBe(8);
        run.SectorMatched.ShouldBe(13);
        run.SectorUnmatched.ShouldBe(17);
        run.DhanETag.ShouldBe(FakeInstrumentSource.DhanStamp.ETag);
        run.SectorETag.ShouldBe(FakeInstrumentSource.SectorStamp.ETag);
        run.Error.ShouldBeNull();
        run.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_catalog_gets_the_candidates_with_sectors_and_the_time_of_the_run()
    {
        await _f.RunAsync(_f.Register());

        var (candidates, sectorsAvailable, now) = _f.Data.Applied.Single();
        candidates.Count.ShouldBe(60);
        sectorsAvailable.ShouldBeTrue();
        now.ShouldBe(_f.Clock.GetUtcNow());
        candidates.Single(c => c.CanonicalSymbol == "NSE:RELIANCE").Sector.ShouldBe("Oil Gas & Consumable Fuels");
    }

    [Fact]
    public async Task Progress_goes_through_the_phases_in_order()
    {
        await _f.RunAsync(_f.Register());

        _f.Data.Progress.Select(p => p.Phase).Distinct().ShouldBe([RefreshPhase.Downloading, RefreshPhase.Parsing, RefreshPhase.Enriching, RefreshPhase.Saving]);
    }

    [Fact]
    public async Task The_first_refresh_is_not_conditional_and_asks_for_the_whole_file()
    {
        await _f.RunAsync(_f.Register());

        _f.Source.MasterRequests.ShouldHaveSingleItem().ShouldBeNull();
    }

    [Fact]
    public async Task An_unchanged_file_is_skipped_without_any_work_and_the_stamps_are_carried_forward()
    {
        _f.AddFinished();
        _f.Data.ActiveCount = 60;
        var run = await _f.RunAsync(_f.Register());

        run.Status.ShouldBe(RefreshStatus.Unchanged);
        run.TotalActive.ShouldBe(60);
        run.DhanETag.ShouldBe(FakeInstrumentSource.DhanStamp.ETag);
        run.SectorETag.ShouldBe(FakeInstrumentSource.SectorStamp.ETag);
        _f.Source.MasterRequests.ShouldHaveSingleItem()!.ETag.ShouldBe(FakeInstrumentSource.DhanStamp.ETag);
        _f.Source.SectorRequests.ShouldBe(0);
        _f.Data.Applied.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_changed_file_is_imported_again()
    {
        var earlier = _f.AddFinished();
        _f.Source.Master = _ => SourceDownload.WithContent(MasterFixtures.Open(MasterFixtures.Master), new SourceStamp("\"dhan-etag-2\"", null));

        var run = await _f.RunAsync(_f.Register());

        run.Status.ShouldBe(RefreshStatus.Succeeded);
        run.DhanETag.ShouldBe("\"dhan-etag-2\"");
        earlier.DhanETag.ShouldBe(FakeInstrumentSource.DhanStamp.ETag);
    }

    [Fact]
    public async Task A_forced_refresh_does_not_ask_conditionally_and_imports_even_if_nothing_changed()
    {
        _f.AddFinished();

        var run = await _f.RunAsync(_f.Register(RefreshTrigger.Forced, force: true));

        _f.Source.MasterRequests.ShouldHaveSingleItem().ShouldBeNull();
        run.Status.ShouldBe(RefreshStatus.Succeeded);
        _f.Data.Applied.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(RefreshStatus.Failed)]
    public async Task Only_successful_or_unchanged_runs_make_the_next_one_conditional(RefreshStatus earlier)
    {
        _f.AddFinished(earlier);

        await _f.RunAsync(_f.Register());

        _f.Source.MasterRequests.ShouldHaveSingleItem().ShouldBeNull();
    }

    [Fact]
    public async Task A_failed_download_fails_the_run_and_changes_nothing()
    {
        _f.Source.Master = _ => SourceDownload.Failed("Dhan's instrument file answered HTTP 503.");

        var run = await _f.RunAsync(_f.Register());

        run.Status.ShouldBe(RefreshStatus.Failed);
        run.Error.ShouldBe("Dhan's instrument file answered HTTP 503.");
        _f.Data.Applied.ShouldBeEmpty();
        _f.Source.SectorRequests.ShouldBe(0);
    }

    [Fact]
    public async Task A_header_that_changed_fails_the_run_with_a_clear_message_and_changes_nothing()
    {
        _f.Source.Master = _ => FakeInstrumentSource.Content(MasterFixtures.Text(MasterFixtures.Master).Replace("ISIN,", "ISIN_CODE,", StringComparison.Ordinal));

        var run = await _f.RunAsync(_f.Register());

        run.Status.ShouldBe(RefreshStatus.Failed);
        run.Error!.ShouldContain("changed shape");
        run.Error!.ShouldContain("ISIN");
        _f.Data.Applied.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_download_cut_short_fails_the_run_with_the_sources_message()
    {
        _f.Source.Master = _ => SourceDownload.WithContent(new FailingStream("Dhan's instrument file took too long to download."), FakeInstrumentSource.DhanStamp);

        var run = await _f.RunAsync(_f.Register());

        run.Status.ShouldBe(RefreshStatus.Failed);
        run.Error.ShouldBe("Dhan's instrument file took too long to download.");
        _f.Data.Applied.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_file_without_in_scope_instruments_fails_the_run()
    {
        _f.Source.Master = _ => FakeInstrumentSource.Content(MasterFixtures.Text(MasterFixtures.Master).Split('\n')[0] + "\n");

        var run = await _f.RunAsync(_f.Register());

        run.Status.ShouldBe(RefreshStatus.Failed);
        _f.Data.Applied.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_sector_list_that_cannot_be_fetched_only_warns_and_leaves_stored_sectors_alone()
    {
        _f.Source.Sectors = () => SourceDownload.Failed("NSE's sector list answered HTTP 403.");

        var run = await _f.RunAsync(_f.Register());

        run.Status.ShouldBe(RefreshStatus.Succeeded);
        run.Warnings.ShouldContain(w => w.Contains("Sectors were not updated", StringComparison.Ordinal) && w.Contains("HTTP 403", StringComparison.Ordinal));
        _f.Data.Applied.Single().SectorsAvailable.ShouldBeFalse();
        _f.Data.Applied.Single().Candidates.ShouldAllBe(c => c.Sector == null);
        run.SectorETag.ShouldBeNull();
        (run.SectorMatched, run.SectorUnmatched).ShouldBe((0, 0));
    }

    [Fact]
    public async Task A_malformed_sector_list_only_warns()
    {
        _f.Source.Sectors = () => SourceDownload.WithContent(MasterFixtures.AsStream("<html>blocked</html>"), FakeInstrumentSource.SectorStamp);

        var run = await _f.RunAsync(_f.Register());

        run.Status.ShouldBe(RefreshStatus.Succeeded);
        run.Warnings.ShouldContain(w => w.Contains("Sectors were not updated", StringComparison.Ordinal));
        _f.Data.Applied.Single().SectorsAvailable.ShouldBeFalse();
    }

    [Fact]
    public async Task A_sector_list_cut_short_only_warns()
    {
        _f.Source.Sectors = () => SourceDownload.WithContent(new FailingStream("NSE's sector list took too long to download."), FakeInstrumentSource.SectorStamp);

        var run = await _f.RunAsync(_f.Register());

        run.Status.ShouldBe(RefreshStatus.Succeeded);
        run.Warnings.ShouldContain(w => w.Contains("took too long", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_failure_while_saving_fails_the_run_without_leaking_the_exception_text()
    {
        _f.Data.ApplyFailure = new InvalidOperationException("password=hunter2 connection refused");

        var run = await _f.RunAsync(_f.Register());

        run.Status.ShouldBe(RefreshStatus.Failed);
        run.Error!.ShouldNotContain("hunter2");
        run.Error!.ShouldContain("list was not changed");
    }

    [Fact]
    public async Task Stopping_the_application_ends_the_run_as_failed_instead_of_leaving_it_running()
    {
        using var cts = new CancellationTokenSource();
        _f.Source.Master = _ =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        };
        var run = _f.Register();

        await _f.Execute.HandleAsync(new ExecuteRefreshCommand(run.Id), cts.Token);

        run.Status.ShouldBe(RefreshStatus.Failed);
        run.Error!.ShouldContain("interrupted");
    }

    [Fact]
    public async Task Today_for_the_expiry_rule_is_the_date_in_India()
    {
        // 2026-10-27 19:00 UTC is already 2026-10-28 00:30 in India: the October contracts (expiry 27th) are over.
        _f.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 27, 19, 0, 0, TimeSpan.Zero));
        await _f.RunAsync(_f.Register());

        _f.Data.Applied.Single().Candidates.ShouldNotContain(c => c.CanonicalSymbol == "NSE:RELIANCE-2026-10-FUT");
        _f.Data.Applied.Single().Candidates.ShouldContain(c => c.CanonicalSymbol == "NSE:RELIANCE-2026-11-FUT");

        // Earlier the same UTC day (still the 27th in India) they are not.
        var other = new MarketFixture();
        other.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 27, 17, 0, 0, TimeSpan.Zero));
        await other.RunAsync(other.Register());
        other.Data.Applied.Single().Candidates.ShouldContain(c => c.CanonicalSymbol == "NSE:RELIANCE-2026-10-FUT");
    }

    [Fact]
    public async Task A_run_that_does_not_exist_or_has_ended_is_left_alone()
    {
        (await _f.Execute.HandleAsync(new ExecuteRefreshCommand(Guid.NewGuid()), Ct)).IsSuccess.ShouldBeTrue();
        var ended = _f.AddFinished();

        await _f.RunAsync(ended);

        _f.Source.MasterRequests.ShouldBeEmpty();
        ended.Status.ShouldBe(RefreshStatus.Succeeded);
    }

    [Fact]
    public async Task Parser_warnings_and_the_protected_ids_are_passed_on()
    {
        var master = MasterFixtures.Text(MasterFixtures.Master);
        // One broken NSE row among sixty good ones is tolerated (1 of 61 is above 1 %, so add many more good rows).
        var good = string.Join("\n", Enumerable.Range(0, 150).Select(i => $"NSE,E,{9000 + i},INE9{i:D6}01,EQUITY,,GOOD{i},GOOD {i} LTD,Good {i},ES,EQ,1.0,,,,5.0000,NA,N,N,N,NA,A,0,0,0,0,0,0,3.3,100.0,50.0,1000,"));
        var bad = "NSE,E,8001,INE000000001,EQUITY,,BROKEN,BROKEN LTD,Broken,ES,EQ,zero,,,,5.0000,NA,N,N,N,NA,A,0,0,0,0,0,0,3.3,100.0,50.0,1000,";
        _f.Source.Master = _ => FakeInstrumentSource.Content(master + "\n" + good + "\n" + bad + "\n");

        var run = await _f.RunAsync(_f.Register());

        run.Status.ShouldBe(RefreshStatus.Succeeded);
        run.Warnings.ShouldContain("Skipped Dhan id 8001: lot size is not a whole number from 1.");
        _f.Data.AppliedProtected.Single().ShouldHaveSingleItem().ShouldBe(new BrokerIdentity("NSE_EQ", "8001"));
    }

    [Fact]
    public async Task A_list_that_would_lose_more_than_half_of_the_active_instruments_is_not_applied()
    {
        _f.Data.ActiveCount = 130; // the file has 60

        var run = await _f.RunAsync(_f.Register());

        run.Status.ShouldBe(RefreshStatus.Failed);
        run.Error.ShouldBe("The new list has only 60 instruments while 130 are active; it was not applied. If the file is right, run a forced refresh.");
        _f.Data.Applied.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_tripwire_allows_exactly_half_and_ignores_an_empty_list()
    {
        _f.Data.ActiveCount = 120;
        var atHalf = await _f.RunAsync(_f.Register());
        var fresh = new MarketFixture();
        var first = await fresh.RunAsync(fresh.Register());

        atHalf.Status.ShouldBe(RefreshStatus.Succeeded);
        first.Status.ShouldBe(RefreshStatus.Succeeded);
    }

    [Fact]
    public async Task A_forced_refresh_is_not_stopped_by_the_tripwire()
    {
        _f.Data.ActiveCount = 4000;

        var run = await _f.RunAsync(_f.Register(RefreshTrigger.Forced, force: true));

        run.Status.ShouldBe(RefreshStatus.Succeeded);
        _f.Data.Applied.Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_unchanged_shortcut_is_not_used_when_the_list_is_empty()
    {
        _f.AddFinished();
        _f.Data.ActiveCount = 0;

        var run = await _f.RunAsync(_f.Register());

        _f.Source.MasterRequests.ShouldHaveSingleItem().ShouldBeNull();
        run.Status.ShouldBe(RefreshStatus.Succeeded);
    }

    [Fact]
    public async Task The_unchanged_shortcut_asks_the_store_for_the_current_rules_version()
    {
        _f.AddFinished();
        _f.Data.ActiveCount = 60;

        await _f.RunAsync(_f.Register());

        _f.Data.StampRequests.ShouldBe([RefreshRules.Version]);
    }

    [Fact]
    public async Task The_unchanged_shortcut_is_not_used_after_a_run_whose_sectors_were_not_applied()
    {
        var earlier = RefreshRun.Start(RefreshTrigger.Daily, false, MarketFixture.Owner, _f.Clock.GetUtcNow().AddHours(-24));
        earlier.Succeed(earlier.StartedAt, FakeInstrumentSource.DhanStamp, SourceStamp.None, MarketFixture.Outcome() with { SectorsApplied = false });
        _f.Data.Runs.Add(earlier);
        _f.Data.ActiveCount = 60;

        var run = await _f.RunAsync(_f.Register());

        _f.Source.MasterRequests.ShouldHaveSingleItem().ShouldBeNull();
        run.Status.ShouldBe(RefreshStatus.Succeeded);
        run.SectorsApplied.ShouldBeTrue();
    }

    [Fact]
    public async Task The_unchanged_shortcut_is_not_used_after_a_run_under_other_rules()
    {
        var earlier = _f.AddFinished();
        typeof(RefreshRun).GetProperty(nameof(RefreshRun.RulesVersion))!.SetValue(earlier, RefreshRules.Version - 1);
        _f.Data.ActiveCount = 60;

        var run = await _f.RunAsync(_f.Register());

        _f.Source.MasterRequests.ShouldHaveSingleItem().ShouldBeNull();
        run.Status.ShouldBe(RefreshStatus.Succeeded);
        run.RulesVersion.ShouldBe(RefreshRules.Version);
    }

    [Fact]
    public async Task A_sector_source_that_throws_anything_only_costs_the_sectors()
    {
        foreach (var failure in new Exception[]
        {
            new InvalidOperationException("The master-data endpoint GET nsearchives.nseindia.com/x is not on the allow-list."),
            new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"),
            new HttpRequestException("boom"),
            new UnauthorizedAccessException(),
        })
        {
            var fixture = new MarketFixture();
            fixture.Source.Sectors = () => throw failure;

            var run = await fixture.RunAsync(fixture.Register());

            run.Status.ShouldBe(RefreshStatus.Succeeded, failure.GetType().Name);
            run.Warnings.ShouldContain(w => w.StartsWith("Sectors were not updated", StringComparison.Ordinal));
            run.Warnings.ShouldNotContain(w => w.Contains("allow-list", StringComparison.Ordinal));
            fixture.Data.Applied.Single().SectorsAvailable.ShouldBeFalse();
            run.SectorsApplied.ShouldBeFalse();
        }
    }

    [Fact]
    public async Task A_failure_while_saving_the_result_does_not_try_to_end_the_run_twice()
    {
        _f.Data.FailSaveNumber = 1; // the save of the finished run
        var run = _f.Register();

        (await _f.Execute.HandleAsync(new ExecuteRefreshCommand(run.Id), Ct)).IsSuccess.ShouldBeTrue();

        run.Status.ShouldBe(RefreshStatus.Succeeded); // not re-failed, no InvalidOperationException from a second End
        _f.Data.Saves.ShouldBe(1);
    }

    private sealed class FailingStream(string message) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => throw new SourceReadException(message);

        public override int Read(byte[] buffer, int offset, int count) => throw new SourceReadException(message);

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
