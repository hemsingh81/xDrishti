using XDrishti.Application.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.PortfolioFixture;

namespace XDrishti.Application.Tests.Portfolio;

/// <summary>
/// The safeguard against a false "Dhan lists nothing": an answer that would close all or most open holdings is not applied without confirmation, because
/// closing and reopening would destroy the owner's acquisition dates (clock: Saturday 2026-10-10, India).
/// </summary>
public sealed class SyncSafeguardTests
{
    private readonly PortfolioFixture _fx = new();

    private static readonly DateOnly Imported = new(2025, 1, 1);

    private static BrokerHolding Numbered(int n) => Row($"S{n:00}", 10 + n, 100m, 110m, (1000 + n).ToString(System.Globalization.CultureInfo.InvariantCulture), $"INE{n:000}A01011");

    private async Task<Guid> AccountWithAsync(int holdings)
    {
        var account = await _fx.ConnectedAsync();
        _fx.Dhan.Rows.AddRange(Enumerable.Range(1, holdings).Select(Numbered));
        await _fx.ImportAllAsync(account.Id, Imported);
        return account.Id;
    }

    private List<DateOnly> LotDates() => [.. _fx.Data.Holdings.SelectMany(h => h.Lots).Select(l => l.AcquiredOn)];

    [Theory]
    [InlineData(0, 1, 1, true)] // empty answer, one open holding
    [InlineData(0, 5, 5, true)]
    [InlineData(0, 0, 0, false)] // nothing open: nothing to protect
    [InlineData(4, 6, 3, false)] // exactly half does not trip
    [InlineData(3, 6, 4, true)]
    [InlineData(3, 7, 4, true)]
    [InlineData(2, 5, 3, true)]
    [InlineData(1, 3, 2, false)] // fewer than 3 would close: never, unless empty
    [InlineData(1, 2, 2, false)]
    [InlineData(1, 100, 2, false)]
    [InlineData(5, 100, 49, false)]
    [InlineData(5, 6, 0, false)]
    public void The_tripwire_needs_an_empty_answer_or_more_than_half_and_at_least_three(int listed, int open, int wouldClose, bool trips) =>
        SyncHoldingsHandler.IsSuspicious(listed, open, wouldClose).ShouldBe(trips);

    [Fact]
    public async Task A_manual_sync_with_an_empty_answer_is_refused_with_the_counts_and_changes_nothing()
    {
        var id = await AccountWithAsync(3);
        _fx.Dhan.Rows.Clear();
        var runsBefore = _fx.Data.Syncs.Count;

        var result = await _fx.SyncAsync(id);

        result.Error.Code.ShouldBe(PortfolioErrors.ConfirmationRequiredCode);
        result.Error.Data.ShouldNotBeNull().ShouldBe(new Dictionary<string, int> { ["listedHoldings"] = 0, ["openHoldings"] = 3, ["wouldClose"] = 3 });
        result.Error.Message.ShouldBe("Dhan listed 0 holdings while 3 are open in xDrishti — nothing was changed; press Sync now to confirm.");
        _fx.Data.Holdings.ShouldAllBe(h => h.Status == HoldingStatus.Open && h.Qty > 0);
        LotDates().ShouldAllBe(d => d == Imported);
        _fx.Data.Syncs.Count.ShouldBe(runsBefore); // a manual refusal leaves no record
        _fx.Events(AccountEventType.HoldingsSynced).ShouldBeEmpty();
    }

    [Fact]
    public async Task With_confirmation_the_closes_are_applied_and_repeating_it_changes_nothing()
    {
        var id = await AccountWithAsync(3);
        _fx.Dhan.Rows.Clear();

        var run = Ok(await _fx.SyncAsync(id, confirm: true, expectedOpen: 3, expectedWouldClose: 3));
        var again = Ok(await _fx.SyncAsync(id)); // nothing open any more: nothing to protect

        (run.Status, run.ClosedCount).ShouldBe(("succeeded", 3));
        _fx.Data.Holdings.ShouldAllBe(h => h.Status == HoldingStatus.Closed);
        (again.ClosedCount, again.UnchangedCount, again.NewCount).ShouldBe((0, 0, 0));
    }

    [Fact]
    public async Task Exactly_half_missing_is_applied()
    {
        var id = await AccountWithAsync(6);
        _fx.Dhan.Rows.RemoveRange(3, 3); // 3 of 6: exactly half

        var half = Ok(await _fx.SyncAsync(id));

        half.ClosedCount.ShouldBe(3);
        _fx.Data.Holdings.Count(h => h.Status == HoldingStatus.Open).ShouldBe(3);
    }

    [Fact]
    public async Task More_than_half_missing_with_at_least_three_is_refused_manually_and_the_dhan_rows_that_are_listed_stay_untouched()
    {
        var id = await AccountWithAsync(6);
        _fx.Dhan.Rows.RemoveRange(2, 4); // 4 of 6 would be closed
        _fx.Dhan.Rows[0] = Numbered(1) with { Qty = 99 }; // a listed holding changed too: still nothing is applied

        var result = await _fx.SyncAsync(id);

        result.Error.Code.ShouldBe(PortfolioErrors.ConfirmationRequiredCode);
        result.Error.Data.ShouldNotBeNull().ShouldBe(new Dictionary<string, int> { ["listedHoldings"] = 2, ["openHoldings"] = 6, ["wouldClose"] = 4 });
        result.Error.Message.ShouldBe("Dhan listed 2 holdings but 4 of your 6 open holdings are missing — nothing was changed; press Sync now to confirm.");
        _fx.HoldingOf("S01").Qty.ShouldBe(11L);
        Ok(await _fx.SyncAsync(id, confirm: true, expectedOpen: 6, expectedWouldClose: 4)).ClosedCount.ShouldBe(4);
        _fx.HoldingOf("S01").Qty.ShouldBe(99L);
    }

    [Fact]
    public async Task Fewer_than_three_missing_never_trips_even_when_it_is_all_that_is_left_open()
    {
        var id = await AccountWithAsync(2);
        _fx.Dhan.Rows.Clear();
        _fx.Dhan.Rows.Add(Numbered(9)); // both known holdings gone, a new one listed: not an empty answer

        var run = Ok(await _fx.SyncAsync(id));

        (run.ClosedCount, run.NewCount).ShouldBe((2, 1));
    }

    [Fact]
    public async Task An_automatic_sync_with_an_empty_answer_changes_nothing_and_is_recorded_as_skipped_with_the_reason()
    {
        var id = await AccountWithAsync(3);
        _fx.Dhan.Rows.Clear();

        var result = await _fx.SyncAsync(id, SyncTrigger.Auto);

        result.Error.Code.ShouldBe(PortfolioErrors.ConfirmationRequiredCode);
        var skipped = _fx.Data.Syncs.Single(s => s.Trigger == SyncTrigger.Auto);
        (skipped.Status, skipped.Error).ShouldBe((SyncStatus.Skipped, "Dhan listed 0 holdings while 3 are open in xDrishti — nothing was changed; press Sync now to confirm."));
        _fx.Data.Holdings.ShouldAllBe(h => h.Status == HoldingStatus.Open);
        LotDates().ShouldAllBe(d => d == Imported);
        _fx.Events(AccountEventType.HoldingsSynced).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_skipped_record_is_not_repeated_within_an_india_day_but_a_new_day_gets_its_own()
    {
        var id = await AccountWithAsync(3);
        _fx.Dhan.Rows.Clear();

        await _fx.SyncAsync(id, SyncTrigger.Auto);
        _fx.Clock.Advance(TimeSpan.FromMinutes(15));
        await _fx.SyncAsync(id, SyncTrigger.Auto);
        _fx.Clock.Advance(TimeSpan.FromMinutes(15));
        await _fx.SyncAsync(id, SyncTrigger.Auto);

        var today = _fx.Data.Syncs.Where(s => s.Status == SyncStatus.Skipped).ToList();
        today.Count.ShouldBe(1);
        today[0].StartedAt.ShouldBe(_fx.Clock.GetUtcNow()); // the record shows the latest check, so the schedule's spacing still works
        _fx.Clock.Advance(TimeSpan.FromHours(10)); // the next India day
        await _fx.SyncAsync(id, SyncTrigger.Auto);
        _fx.Data.Syncs.Count(s => s.Status == SyncStatus.Skipped).ShouldBe(2);
        _fx.Data.Holdings.ShouldAllBe(h => h.Status == HoldingStatus.Open);
    }

    [Fact]
    public async Task A_manual_confirmation_applies_what_an_automatic_run_held_back()
    {
        var id = await AccountWithAsync(4);
        _fx.Dhan.Rows.Clear();
        await _fx.SyncAsync(id, SyncTrigger.Auto);

        var run = Ok(await _fx.SyncAsync(id, confirm: true, expectedOpen: 4, expectedWouldClose: 4));

        run.ClosedCount.ShouldBe(4);
    }

    [Fact]
    public async Task Confirming_needs_both_numbers_from_the_confirmation_request()
    {
        var id = await AccountWithAsync(3);
        _fx.Dhan.Rows.Clear();

        var neither = await _fx.SyncAsync(id, confirm: true);
        var onlyOne = await _fx.SyncAsync(id, confirm: true, expectedOpen: 3);

        neither.Error.Code.ShouldBe(PortfolioErrors.InvalidCode);
        FieldsOf(neither).ShouldBe(["expectedOpenHoldings", "expectedWouldClose"]);
        FieldsOf(onlyOne).ShouldBe(["expectedWouldClose"]);
        _fx.Data.Holdings.ShouldAllBe(h => h.Status == HoldingStatus.Open);
        _fx.Dhan.Tokens.Count.ShouldBe(1); // only the import fetched: nothing was asked of Dhan
    }

    [Fact]
    public async Task A_confirmation_for_other_numbers_than_dhan_gives_now_is_stale_and_changes_nothing()
    {
        var id = await AccountWithAsync(6);
        _fx.Dhan.Rows.RemoveRange(2, 4); // the owner saw: 6 open, 4 would close
        var refused = await _fx.SyncAsync(id);
        refused.Error.Code.ShouldBe(PortfolioErrors.ConfirmationRequiredCode);
        _fx.Dhan.Rows.RemoveAt(1); // meanwhile Dhan lists one holding less: now 5 would close

        var stale = await _fx.SyncAsync(id, confirm: true, expectedOpen: 6, expectedWouldClose: 4);

        stale.Error.Code.ShouldBe(PortfolioErrors.ConfirmationStaleCode);
        stale.Error.Data.ShouldNotBeNull().ShouldBe(new Dictionary<string, int> { ["listedHoldings"] = 1, ["openHoldings"] = 6, ["wouldClose"] = 5 });
        _fx.Data.Holdings.ShouldAllBe(h => h.Status == HoldingStatus.Open);
        _fx.Data.Syncs.Count(s => s.Trigger == SyncTrigger.Manual).ShouldBe(0); // no record of a refusal
        Ok(await _fx.SyncAsync(id, confirm: true, expectedOpen: 6, expectedWouldClose: 5)).ClosedCount.ShouldBe(5);
    }

    [Fact]
    public async Task A_stale_confirmation_is_also_refused_when_the_answer_is_no_longer_suspicious()
    {
        var id = await AccountWithAsync(6);
        _fx.Dhan.Rows.RemoveRange(2, 4);
        _fx.Dhan.Rows.AddRange([Numbered(3), Numbered(4), Numbered(5)]); // now only 1 of 6 is missing

        var stale = await _fx.SyncAsync(id, confirm: true, expectedOpen: 6, expectedWouldClose: 4);

        stale.Error.Code.ShouldBe(PortfolioErrors.ConfirmationStaleCode);
        stale.Error.Data.ShouldNotBeNull()["wouldClose"].ShouldBe(1);
        _fx.Data.Holdings.ShouldAllBe(h => h.Status == HoldingStatus.Open);
    }

    [Fact]
    public async Task An_automatic_run_never_confirms_by_itself()
    {
        var id = await AccountWithAsync(4);
        _fx.Dhan.Rows.Clear();

        var result = await _fx.Sync().HandleAsync(new SyncHoldingsCommand(id, SyncTrigger.Auto, "system:portfolio-refresh", Confirm: false), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        _fx.Data.Holdings.ShouldAllBe(h => h.Status == HoldingStatus.Open);
    }
}
