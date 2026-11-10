using XDrishti.Application.Market.Baskets;
using XDrishti.Domain.Market.Baskets;
using XDrishti.Domain.Market.Baskets.Lists;

namespace XDrishti.Application.Tests.Market.Baskets.Lists;

public sealed class RefreshFlagListsHandlerTests
{
    private readonly ListWorld _w = new();
    private readonly Guid _refreshId = Guid.CreateVersion7();

    private static readonly DateTimeOffset StartOf26Oct = new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.FromMinutes(330)).ToUniversalTime();

    private Task<XDrishti.Domain.Common.Result<FlagListRun>> RunAsync(bool masterOk = true) =>
        _w.Refresh.HandleAsync(new RefreshFlagListsCommand(masterOk, _refreshId, "system:daily-refresh"), ListWorld.Ct).AsTask();

    private void AllSources()
    {
        foreach (var definition in ListBaskets.FromMaster)
        {
            _w.FlagSource(definition);
        }
    }

    [Fact]
    public async Task One_instrument_that_gains_the_futures_flag_is_added_dated_that_day_as_derived_from_the_refresh_and_never_deleted()
    {
        AllSources();
        var existing = Enumerable.Range(0, 212).Select(i => _w.Share($"F{i:000}", fno: true)).ToList();
        foreach (var id in existing)
        {
            _w.Member(ListBaskets.FnoStocks.Id, id, StartOf26Oct.AddDays(-30));
        }

        var added = _w.Share("NEWFNO", fno: true);

        var run = await RunAsync();

        run.IsSuccess.ShouldBeTrue();
        var application = _w.Applier.Applied.Single(a => a.BasketId == ListBaskets.FnoStocks.Id);
        application.Change.Added.ShouldBe([added]);
        application.Change.Removed.ShouldBeEmpty();
        application.EffectiveFrom.ShouldBe(StartOf26Oct);
        (application.Origin!.Provenance, application.Origin.SourceKind, application.Origin.SourceRef).ShouldBe((BasketProvenance.Derived, "refresh", _refreshId));
        application.Fetch.Outcome.ShouldBe(FetchOutcomes.Applied);
    }

    [Fact]
    public async Task A_flag_turned_off_or_a_retired_instrument_is_removed_so_that_the_stay_is_closed()
    {
        AllSources();
        var off = _w.Share("OFF", fno: false);
        var retired = _w.Share("GONE", fno: true, retired: true);
        var stays = _w.Share("STAYS", fno: true);
        foreach (var id in new[] { off, retired, stays })
        {
            _w.Member(ListBaskets.FnoStocks.Id, id, StartOf26Oct.AddDays(-30));
        }

        await RunAsync();

        var application = _w.Applier.Applied.Single(a => a.BasketId == ListBaskets.FnoStocks.Id);
        application.Change.Removed.OrderBy(i => i).ShouldBe(new[] { off, retired }.Order());
        application.Change.Added.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_surveillance_list_holds_the_non_N_flags_with_the_category_as_the_note()
    {
        AllSources();
        var plain = _w.Share("PLAIN", asm: "N");
        var yes = _w.Share("YES", asm: "Y", category: "ASM Stage 1");
        var other = _w.Share("OTHER", asm: "S", category: "GSM Stage 2");

        await RunAsync();

        var application = _w.Applier.Applied.Single(a => a.BasketId == ListBaskets.AsmGsm.Id);
        application.Change.Added.ShouldNotContain(plain);
        application.Change.Added.Order().ShouldBe(new[] { yes, other }.Order());
        (application.Origin!.Notes[yes], application.Origin.Notes[other]).ShouldBe(("ASM Stage 1", "GSM Stage 2"));
    }

    [Fact]
    public async Task An_unchanged_list_writes_only_a_fetch_row_and_the_source_stays_ok()
    {
        AllSources();
        var mtf = _w.Share("MTF1", mtf: 3);
        _w.Member(ListBaskets.Mtf.Id, mtf, StartOf26Oct.AddDays(-1));

        await RunAsync();

        _w.Applier.Applied.ShouldNotContain(a => a.BasketId == ListBaskets.Mtf.Id);
        var recorded = _w.Store.Recorded.Single(r => r.Fetch.BasketId == ListBaskets.Mtf.Id);
        (recorded.Fetch.Outcome, recorded.Source.State, recorded.Source.LastOkAt).ShouldBe((FetchOutcomes.Unchanged, BasketSourceStates.Ok, _w.Clock.GetUtcNow()));
    }

    [Fact]
    public async Task Every_master_list_is_checked_and_nothing_is_requested_from_the_network()
    {
        AllSources();
        _w.Share("ANY", fno: true);

        var run = await RunAsync();

        run.Value.Outcomes.Count.ShouldBe(4);
        _w.Fetcher.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task When_the_master_run_failed_the_lists_keep_their_members_and_the_sources_show_failed()
    {
        AllSources();
        var member = _w.Share("KEPT", fno: true);
        _w.Member(ListBaskets.FnoStocks.Id, member, StartOf26Oct.AddDays(-3));

        var run = await RunAsync(masterOk: false);

        run.Value.Outcomes.ShouldAllBe(o => o.Outcome == FetchOutcomes.Failed);
        _w.Applier.Applied.ShouldBeEmpty();
        _w.Master.Reads.ShouldBe(0);
        _w.Store.Recorded.ShouldAllBe(r => r.Source.State == BasketSourceStates.Failed && r.Source.FailCount == 1);
        (await _w.Baskets.ListActiveMembersAsync(ListBaskets.FnoStocks.Id, ListWorld.Ct)).Select(m => m.InstrumentId).ShouldBe([member]);
    }

    [Fact]
    public async Task A_concurrent_change_is_a_failed_check_and_nothing_is_claimed()
    {
        AllSources();
        _w.Share("A", fno: true);
        _w.Applier.Succeeds = false;

        var run = await RunAsync();

        run.Value.Outcomes.Single(o => o.BasketId == ListBaskets.FnoStocks.Id).Outcome.ShouldBe(FetchOutcomes.Failed);
    }
}
