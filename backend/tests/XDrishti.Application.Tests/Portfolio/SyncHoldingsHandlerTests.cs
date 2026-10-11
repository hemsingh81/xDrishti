using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Accounts;
using XDrishti.Application.Accounts.Connections;
using XDrishti.Application.Portfolio;
using XDrishti.Application.Tests.Accounts;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.PortfolioFixture;

namespace XDrishti.Application.Tests.Portfolio;

/// <summary>"Sync now" and the automatic run: the rules of ADR 0012 end to end on in-memory ports (the clock starts on Saturday 2026-10-10, India).</summary>
public sealed class SyncHoldingsHandlerTests
{
    private readonly PortfolioFixture _fx = new();

    private static readonly DateOnly Imported = new(2025, 1, 1);

    private async Task<(BrokerAccount Account, Guid Id)> ImportedAccountAsync(params BrokerHolding[] rows)
    {
        var account = await _fx.ConnectedAsync();
        _fx.Dhan.Rows.AddRange(rows);
        await _fx.ImportAllAsync(account.Id, Imported);
        return (account, account.Id);
    }

    [Fact]
    public async Task A_holding_dhan_lists_later_gets_a_lot_dated_today_in_the_accounts_default_bucket()
    {
        var (account, _) = await ImportedAccountAsync(Row("OLD", 10, 5m, 6m, "1", "INE000Z00001"));
        _fx.Dhan.Rows.Add(Row("EXMPL", 100, 50m, 60m));

        var run = Ok(await _fx.SyncAsync(account.Id));

        (run.Status, run.Trigger, run.NewCount, run.IncreasedCount, run.DecreasedCount, run.ClosedCount, run.UnchangedCount).ShouldBe(("succeeded", "manual", 1, 0, 0, 0, 1));
        var holding = _fx.HoldingOf("EXMPL");
        var lot = holding.Lots.ShouldHaveSingleItem();
        (lot.AcquiredOn, lot.Source, lot.UsesDefaultDate, lot.Qty, lot.UnitCost).ShouldBe((_fx.Today, LotSource.Added, false, 100L, 50m));
        holding.BucketId.ShouldBe(_fx.Accounts.Buckets.Single(b => b.DefaultForAccountId == account.Id).Id);
        (holding.Qty, holding.AvgCost, holding.LastPrice).ShouldBe((100L, 50m, 60m));
    }

    [Fact]
    public async Task The_first_sync_of_an_account_without_holdings_imports_everything_with_the_default_first_import_date_flagged_as_default()
    {
        var account = await _fx.ConnectedAsync();
        _fx.Dhan.Rows.Add(Row("EXMPL", 100, 50m, 60m));
        _fx.Dhan.Rows.Add(Row("OTHER", 5, 10m, 12m, "1002", "INE000B01012"));

        var run = Ok(await _fx.SyncAsync(account.Id));

        (run.Status, run.Trigger, run.NewCount, run.UnchangedCount).ShouldBe(("succeeded", "import", 2, 0));
        var holding = _fx.HoldingOf("EXMPL");
        var lot = holding.Lots.ShouldHaveSingleItem();
        (lot.AcquiredOn, lot.Source, lot.UsesDefaultDate, lot.Qty, lot.UnitCost).ShouldBe((new DateOnly(2025, 1, 1), LotSource.Import, true, 100L, 50m));
        holding.BucketId.ShouldBe(_fx.Accounts.Buckets.Single(b => b.DefaultForAccountId == account.Id).Id);
        _fx.HoldingOf("OTHER").Lots.Single().AcquiredOn.ShouldBe(new DateOnly(2025, 1, 1));
        _fx.Events(AccountEventType.HoldingsImported).Single().Detail!.ShouldContain("2 imported");
        _fx.Events(AccountEventType.HoldingsSynced).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_first_sync_dates_the_holdings_with_the_stored_first_import_date()
    {
        var account = await _fx.ConnectedAsync();
        await _fx.Connections.Accounts.UpdatePortfolio().HandleAsync(
            new UpdatePortfolioCommand(account.Id, new PortfolioInput(null, null, FirstImportDate: new DateOnly(2023, 7, 1)), Actor), CancellationToken.None);
        _fx.Dhan.Rows.Add(Row());

        Ok(await _fx.SyncAsync(account.Id));

        var lot = _fx.HoldingOf("EXMPL").Lots.Single();
        (lot.AcquiredOn, lot.UsesDefaultDate).ShouldBe((new DateOnly(2023, 7, 1), true));
    }

    [Fact]
    public async Task Changing_the_first_import_date_afterwards_does_not_touch_existing_holdings_and_the_next_sync_is_a_normal_one()
    {
        var (account, id) = await ImportedAccountAsync(Row());
        await _fx.Connections.Accounts.UpdatePortfolio().HandleAsync(
            new UpdatePortfolioCommand(account.Id, new PortfolioInput(null, null, FirstImportDate: new DateOnly(2020, 1, 1)), Actor), CancellationToken.None);
        _fx.Dhan.Rows.Add(Row("NEWCO", 3, 10m, 11m, "1002", "INE000B01012"));

        var run = Ok(await _fx.SyncAsync(id));

        (run.Trigger, run.NewCount, run.UnchangedCount).ShouldBe(("manual", 1, 1));
        _fx.HoldingOf("EXMPL").Lots.Single().AcquiredOn.ShouldBe(Imported);
        _fx.HoldingOf("NEWCO").Lots.Single().AcquiredOn.ShouldBe(_fx.Today);
    }

    [Fact]
    public async Task A_first_sync_that_fails_changes_nothing_and_is_recorded_as_a_failed_import()
    {
        var account = await _fx.ConnectedAsync();
        _fx.Dhan.Failure = new BrokerFailure(BrokerFailureKind.Unavailable);

        var result = await _fx.SyncAsync(account.Id);

        result.IsFailure.ShouldBeTrue();
        _fx.Data.Holdings.ShouldBeEmpty();
        _fx.Data.Syncs.Single().ShouldSatisfyAllConditions(s => s.Status.ShouldBe(SyncStatus.Failed), s => s.Trigger.ShouldBe(SyncTrigger.Import));
    }

    [Fact]
    public async Task New_holdings_go_to_the_custom_bucket_chosen_in_the_portfolio_settings()
    {
        var account = await _fx.ConnectedAsync();
        _fx.Accounts.Buckets.Add(PortfolioBucket.Create("Growth", _fx.Clock.GetUtcNow()).Value);
        await _fx.Connections.Accounts.UpdatePortfolio().HandleAsync(new UpdatePortfolioCommand(account.Id, new PortfolioInput("Growth", true), Actor), CancellationToken.None);
        _fx.Dhan.Rows.Add(Row());

        Ok(await _fx.SyncAsync(account.Id));

        _fx.HoldingOf("EXMPL").BucketId.ShouldBe(_fx.Accounts.Buckets.Single(b => b.Name == "Growth").Id);
    }

    [Fact]
    public async Task A_larger_quantity_adds_a_lot_today_at_the_implied_cost_of_the_added_shares()
    {
        var (_, id) = await ImportedAccountAsync(Row(qty: 100, avgCost: 50m));
        _fx.Dhan.Rows[0] = Row(qty: 130, avgCost: 53m, price: 70m);

        var run = Ok(await _fx.SyncAsync(id));

        (run.NewCount, run.IncreasedCount, run.UnchangedCount).ShouldBe((0, 1, 0));
        var added = _fx.HoldingOf("EXMPL").Lots.Single(l => l.Source == LotSource.Added);
        (added.AcquiredOn, added.Qty, added.UnitCost).ShouldBe((_fx.Today, 30L, 63m));
    }

    [Fact]
    public async Task A_smaller_quantity_closes_lots_oldest_first_at_the_last_price_marked_estimated()
    {
        var (_, id) = await ImportedAccountAsync(Row(qty: 100, avgCost: 50m));
        _fx.Dhan.Rows[0] = Row(qty: 60, avgCost: 50m, price: 75m);

        var run = Ok(await _fx.SyncAsync(id));

        (run.DecreasedCount, run.UnchangedCount).ShouldBe((1, 0));
        var closed = _fx.HoldingOf("EXMPL").Lots.Single(l => !l.IsOpen);
        (closed.Qty, closed.ClosePrice, closed.ClosedOn, closed.Estimated).ShouldBe((40L, 75m, _fx.Today, true));
        _fx.HoldingOf("EXMPL").Lots.Single(l => l.IsOpen).Qty.ShouldBe(60L);
    }

    [Fact]
    public async Task A_holding_dhan_no_longer_lists_is_closed()
    {
        var (_, id) = await ImportedAccountAsync(Row("AAA", securityId: "1", isin: "INE000A00001"), Row("BBB", securityId: "2", isin: "INE000B00002"));
        _fx.Dhan.Rows.RemoveAt(1);

        var run = Ok(await _fx.SyncAsync(id));

        (run.ClosedCount, run.UnchangedCount).ShouldBe((1, 1));
        _fx.HoldingOf("BBB").Status.ShouldBe(HoldingStatus.Closed);
        _fx.HoldingOf("AAA").Status.ShouldBe(HoldingStatus.Open);
    }

    [Fact]
    public async Task An_unchanged_holding_only_gets_new_prices_and_a_second_identical_run_changes_nothing()
    {
        var (_, id) = await ImportedAccountAsync(Row(qty: 100, avgCost: 50m, price: 60m));
        _fx.Dhan.Rows[0] = Row(qty: 100, avgCost: 50m, price: 66m);
        Ok(await _fx.SyncAsync(id));
        var afterFirst = _fx.HoldingOf("EXMPL").Lots.Select(l => (l.AcquiredOn, l.Qty, l.UnitCost, l.ClosedOn)).ToList();
        _fx.Clock.Advance(TimeSpan.FromMinutes(5));

        var second = Ok(await _fx.SyncAsync(id));

        (second.NewCount, second.IncreasedCount, second.DecreasedCount, second.ClosedCount, second.UnchangedCount).ShouldBe((0, 0, 0, 0, 1));
        _fx.HoldingOf("EXMPL").Lots.Select(l => (l.AcquiredOn, l.Qty, l.UnitCost, l.ClosedOn)).ShouldBe(afterFirst);
        _fx.HoldingOf("EXMPL").LastPrice.ShouldBe(66m);
    }

    [Fact]
    public async Task Syncing_the_same_changes_twice_does_not_add_the_lot_twice()
    {
        var (_, id) = await ImportedAccountAsync(Row(qty: 100, avgCost: 50m));
        _fx.Dhan.Rows[0] = Row(qty: 130, avgCost: 53m, price: 70m);

        Ok(await _fx.SyncAsync(id));
        var second = Ok(await _fx.SyncAsync(id));

        second.UnchangedCount.ShouldBe(1);
        _fx.HoldingOf("EXMPL").Lots.Count(l => l.Source == LotSource.Added).ShouldBe(1);
    }

    [Fact]
    public async Task A_holding_that_comes_back_after_being_closed_is_the_same_holding_with_a_new_lot_dated_today()
    {
        var (_, id) = await ImportedAccountAsync(Row());
        _fx.Dhan.Rows.Clear();
        Ok(await _fx.SyncAsync(id, confirm: true, expectedOpen: 1, expectedWouldClose: 1));
        _fx.Dhan.Rows.Add(Row(qty: 25, avgCost: 70m, price: 71m));

        var run = Ok(await _fx.SyncAsync(id));

        run.NewCount.ShouldBe(1);
        _fx.Data.Holdings.ShouldHaveSingleItem().Status.ShouldBe(HoldingStatus.Open);
    }

    [Fact]
    public async Task Holdings_are_mapped_to_the_instrument_master_and_unmapped_ones_are_kept()
    {
        var account = await _fx.ConnectedAsync();
        var instrument = new InstrumentRef(Guid.NewGuid(), "EXMPL", "Example Industries");
        _fx.Dhan.Rows.AddRange([Row("EXMPL"), Row("BSEONLY", securityId: "500", isin: "INE000Z00009")]);
        _fx.Instruments.Known[Row().Key] = instrument;

        Ok(await _fx.SyncAsync(account.Id));

        var mapped = _fx.HoldingOf("EXMPL");
        (mapped.InstrumentId, mapped.Name, mapped.IsMapped).ShouldBe((instrument.Id, "Example Industries", true));
        var unmapped = _fx.HoldingOf("BSEONLY");
        (unmapped.InstrumentId, unmapped.Name, unmapped.IsMapped).ShouldBe(((Guid?)null, "BSEONLY", false));
    }

    [Fact]
    public async Task Rows_of_one_holding_are_added_up_before_they_are_stored()
    {
        var account = await _fx.ConnectedAsync();
        _fx.Dhan.Rows.AddRange([Row(qty: 100, avgCost: 50m), Row(qty: 50, avgCost: 80m, securityId: "1002")]);

        Ok(await _fx.SyncAsync(account.Id));

        var holding = _fx.Data.Holdings.ShouldHaveSingleItem();
        (holding.Qty, holding.AvgCost).ShouldBe((150L, 60m));
    }

    [Fact]
    public async Task The_token_is_read_from_the_vault_and_given_to_dhan()
    {
        var account = await _fx.ConnectedAsync();
        _fx.Dhan.Rows.Add(Row());

        Ok(await _fx.SyncAsync(account.Id));

        _fx.Dhan.Tokens.Single().Reveal().ShouldBe(_fx.Connections.Stored(account.Id, BrokerSecretName.AccessToken));
    }

    [Fact]
    public async Task A_manual_sync_is_audited_with_counts_only_and_no_secret_appears_anywhere()
    {
        var (account, _) = await ImportedAccountAsync(Row());

        Ok(await _fx.SyncAsync(account.Id));

        var audit = _fx.Events(AccountEventType.HoldingsSynced).Single();
        audit.Detail.ShouldBe("Holdings synced (manual): 0 new, 0 increased, 0 decreased, 0 closed, 1 unchanged");
        audit.Actor.ShouldBe(Actor);
        _fx.Accounts.Events.Concat([]).Select(e => e.Detail).ShouldAllBe(d => d == null || !d.Contains(AccountsFixture.Marker));
        _fx.Data.Syncs.Select(s => s.Error).ShouldAllBe(e => e == null || !e.Contains(AccountsFixture.Marker));
    }

    [Fact]
    public async Task An_account_that_is_not_connected_is_refused_without_calling_dhan_or_recording_a_run()
    {
        var account = await _fx.ConnectedAsync(connect: false);

        var result = await _fx.SyncAsync(account.Id);

        result.Error.Code.ShouldBe(PortfolioErrors.NotConnectedCode);
        _fx.Dhan.Tokens.ShouldBeEmpty();
        _fx.Data.Syncs.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_expired_token_counts_as_not_connected()
    {
        var account = await _fx.ConnectedAsync();
        _fx.Clock.Advance(TimeSpan.FromHours(25));

        var result = await _fx.SyncAsync(account.Id);

        result.Error.Code.ShouldBe(PortfolioErrors.NotConnectedCode);
        _fx.Dhan.Tokens.ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_the_portfolio_role_or_for_an_unknown_account_the_sync_is_refused()
    {
        var plain = await _fx.Connections.Accounts.CreatedAsync(Inputs.Account(name: "Plain", clientId: "1000000002"));

        (await _fx.SyncAsync(plain.Id)).Error.Code.ShouldBe(PortfolioErrors.RoleOffCode);
        (await _fx.SyncAsync(Guid.NewGuid())).Error.Code.ShouldBe(AccountErrors.NotFoundCode);
    }

    [Fact]
    public async Task When_dhan_refuses_the_token_the_error_says_to_sign_in_again_and_the_run_is_recorded_as_failed()
    {
        var (_, id) = await ImportedAccountAsync(Row());
        _fx.Dhan.Failure = new BrokerFailure(BrokerFailureKind.Unauthorized, "DH-901");

        var result = await _fx.SyncAsync(id);

        result.Error.Code.ShouldBe(PortfolioErrors.NotConnectedCode);
        result.Error.Message.ShouldContain("Sign in to Dhan again");
        var failed = _fx.Data.Syncs.Last();
        (failed.Status, failed.Trigger).ShouldBe((SyncStatus.Failed, SyncTrigger.Manual));
        failed.Error.ShouldBe(result.Error.Message);
        _fx.HoldingOf("EXMPL").Qty.ShouldBe(100L);
    }

    [Theory]
    [InlineData(BrokerFailureKind.Unavailable, "Try again in a moment.")]
    [InlineData(BrokerFailureKind.Rejected, "refused the request")]
    public async Task A_dhan_failure_is_reported_as_a_broker_error_and_changes_nothing(BrokerFailureKind kind, string text)
    {
        var (_, id) = await ImportedAccountAsync(Row());
        _fx.Dhan.Failure = new BrokerFailure(kind, "DH-905");
        var before = _fx.HoldingOf("EXMPL").Lots.Count;

        var result = await _fx.SyncAsync(id);

        result.Error.Code.ShouldBe(ConnectionErrors.BrokerErrorCode);
        result.Error.Message.ShouldContain(text);
        result.Error.Message.ShouldContain("DH-905");
        _fx.Data.Syncs.Last().Status.ShouldBe(SyncStatus.Failed);
        _fx.HoldingOf("EXMPL").Lots.Count.ShouldBe(before);
    }

    [Fact]
    public async Task A_failure_while_saving_is_recorded_and_reported_without_details()
    {
        var (_, id) = await ImportedAccountAsync(Row());
        _fx.Dhan.Rows[0] = Row(qty: 130, avgCost: 53m);
        _fx.Data.FailTransactionWith = new InvalidOperationException("password=hunter2 " + AccountsFixture.Marker);

        var result = await _fx.SyncAsync(id);

        result.Error.ShouldBe(PortfolioErrors.SyncFailed);
        var failed = _fx.Data.Syncs.Last();
        failed.Status.ShouldBe(SyncStatus.Failed);
        failed.Error.ShouldNotBeNull().ShouldNotContain("hunter2");
        failed.Error.ShouldNotContain(AccountsFixture.Marker);
    }

    [Fact]
    public async Task While_another_sync_of_the_account_runs_a_second_one_is_refused_and_dhan_is_not_called()
    {
        var (_, id) = await ImportedAccountAsync(Row());
        var callsBefore = _fx.Dhan.Tokens.Count;
        _fx.Data.OtherRunInProgress = true;

        var result = await _fx.SyncAsync(id);

        result.Error.ShouldBe(PortfolioErrors.SyncRunning);
        _fx.Dhan.Tokens.Count.ShouldBe(callsBefore);
    }

    [Fact]
    public async Task A_stale_running_record_does_not_block_the_next_sync()
    {
        var account = await _fx.ConnectedAsync();
        _fx.Dhan.Rows.Add(Row());
        var stale = HoldingSync.Start(account.Id, SyncTrigger.Auto, "x", _fx.Clock.GetUtcNow().AddMinutes(-30));
        _fx.Data.Syncs.Add(stale);

        Ok(await _fx.SyncAsync(account.Id));

        stale.Status.ShouldBe(SyncStatus.Failed);
    }

    [Fact]
    public async Task An_automatic_run_that_finds_only_new_prices_leaves_the_audit_trail_alone_but_one_with_changes_is_audited()
    {
        var (_, id) = await ImportedAccountAsync(Row(qty: 100, avgCost: 50m));
        _fx.Dhan.Rows[0] = Row(qty: 100, avgCost: 50m, price: 61m);

        Ok(await _fx.SyncAsync(id, SyncTrigger.Auto));
        _fx.Events(AccountEventType.HoldingsSynced).ShouldBeEmpty();
        _fx.Dhan.Rows[0] = Row(qty: 110, avgCost: 51m, price: 61m);
        Ok(await _fx.SyncAsync(id, SyncTrigger.Auto));

        _fx.Events(AccountEventType.HoldingsSynced).Single().Detail.ShouldStartWith("Holdings synced (auto): 0 new, 1 increased");
        _fx.Data.Syncs.Count(s => s.Trigger == SyncTrigger.Auto && s.Status == SyncStatus.Succeeded).ShouldBe(2);
    }

    [Fact]
    public async Task An_automatic_run_of_an_account_that_is_not_connected_is_skipped_and_recorded_once_per_day()
    {
        var account = await _fx.ConnectedAsync(connect: false);

        await _fx.SyncAsync(account.Id, SyncTrigger.Auto);
        _fx.Clock.Advance(TimeSpan.FromHours(2));
        await _fx.SyncAsync(account.Id, SyncTrigger.Auto);

        var skipped = _fx.Data.Syncs.ShouldHaveSingleItem();
        (skipped.Status, skipped.Trigger, skipped.Error).ShouldBe((SyncStatus.Skipped, SyncTrigger.Auto, "The account is not connected to Dhan."));
        _fx.Clock.Advance(TimeSpan.FromHours(10)); // 2026-10-11 05:30 IST: the next India day
        await _fx.SyncAsync(account.Id, SyncTrigger.Auto);
        _fx.Data.Syncs.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_manual_attempt_without_a_connection_does_not_record_a_skipped_run()
    {
        var account = await _fx.ConnectedAsync(connect: false);

        await _fx.SyncAsync(account.Id);

        _fx.Data.Syncs.ShouldBeEmpty();
    }

    [Fact]
    public async Task Interrupted_runs_of_an_earlier_process_are_marked_failed_when_the_application_starts()
    {
        var account = await _fx.ConnectedAsync();
        var interrupted = HoldingSync.Start(account.Id, SyncTrigger.Manual, "x", _fx.Clock.GetUtcNow().AddMinutes(-1));
        var current = HoldingSync.Start(Guid.NewGuid(), SyncTrigger.Manual, "x", _fx.Clock.GetUtcNow().AddMinutes(1));
        _fx.Data.Syncs.AddRange([interrupted, current]);

        await _fx.Recover().HandleAsync(new RecoverInterruptedSyncsCommand(_fx.Clock.GetUtcNow()), CancellationToken.None);

        (interrupted.Status, current.Status).ShouldBe((SyncStatus.Failed, SyncStatus.Running));
    }

    private HoldingSync Old(Guid accountId, SyncStatus status, int daysAgo, SyncTrigger trigger = SyncTrigger.Auto)
    {
        var run = HoldingSync.Start(accountId, trigger, "x", _fx.Clock.GetUtcNow().AddDays(-daysAgo));
        switch (status)
        {
            case SyncStatus.Succeeded:
                run.Succeed(run.StartedAt.AddSeconds(2), SyncCounts.None);
                break;
            case SyncStatus.Failed:
                run.Fail(run.StartedAt.AddSeconds(2), "e");
                break;
            default:
                run.Skip(run.StartedAt.AddSeconds(2), "s");
                break;
        }

        _fx.Data.Syncs.Add(run);
        return run;
    }

    [Fact]
    public async Task An_automatic_sync_prunes_finished_records_older_than_90_days_but_keeps_failed_and_recent_ones()
    {
        var account = await _fx.ConnectedAsync();
        _fx.Dhan.Rows.Add(Row());
        var oldSucceeded = Old(account.Id, SyncStatus.Succeeded, 91);
        var oldSkipped = Old(account.Id, SyncStatus.Skipped, 120);
        var oldFailed = Old(account.Id, SyncStatus.Failed, 200);
        var recent = Old(account.Id, SyncStatus.Succeeded, 89);
        _fx.Clock.Advance(SyncHoldingsHandler.FirstImportSettleTime + TimeSpan.FromMinutes(1));

        Ok(await _fx.SyncAsync(account.Id, SyncTrigger.Auto));

        _fx.Data.Syncs.ShouldNotContain(oldSucceeded);
        _fx.Data.Syncs.ShouldNotContain(oldSkipped);
        _fx.Data.Syncs.ShouldContain(oldFailed);
        _fx.Data.Syncs.ShouldContain(recent);
    }

    [Fact]
    public async Task A_manual_sync_does_not_prune_and_pruning_is_bounded_by_the_batch_size()
    {
        var account = await _fx.ConnectedAsync();
        _fx.Dhan.Rows.Add(Row());
        var old = Old(account.Id, SyncStatus.Succeeded, 100);

        Ok(await _fx.SyncAsync(account.Id));

        _fx.Data.PruneCalls.ShouldBe(0);
        _fx.Data.Syncs.ShouldContain(old);
        for (var i = 0; i < SyncHoldingsHandler.PruneBatch + 5; i++)
        {
            Old(account.Id, SyncStatus.Skipped, 100 + i % 10, SyncTrigger.Manual);
        }

        var removed = await _fx.Data.PruneAsync(_fx.Clock.GetUtcNow().AddDays(-SyncHoldingsHandler.RetentionDays), SyncHoldingsHandler.PruneBatch, CancellationToken.None);

        removed.ShouldBe(SyncHoldingsHandler.PruneBatch);
    }
}
