using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio;
using XDrishti.Application.Portfolio.Attention;
using XDrishti.Application.Tests.Portfolio.Actions;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Tests.Portfolio.Heal;

/// <summary>
/// The attention items on <c>GET /api/portfolio/notices</c> (ADR 0027, ADR 0030): the notices only read what the reconciler remembered (the healer tick and the end of a sync run write); they come with title, at most two actions, since and resolved; they are ordered error, warning,
/// info and newest first; they resolve by themselves, stay for 24 hours resolved and are then dropped; a changed situation is a new item; the facts are read once per reconciliation however many accounts there are; a failure to work them out or to read them leaves the older notices alone; and
/// the resolved items past their retention are deleted.
/// </summary>
public sealed class AttentionNoticesTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly ActionsHarness _harness = new();

    private StaticAttentionFacts Facts => _harness.AttentionFacts;

    /// <summary>What the healer tick (or the end of a sync run) does, then what the bell asks.</summary>
    private async Task<PortfolioNotices> ReadAsync()
    {
        await _harness.AttentionReconciler().ReconcileAsync(Ct);
        return await GetAsync();
    }

    /// <summary>Only the bell's read: nothing reconciles.</summary>
    private async Task<PortfolioNotices> GetAsync() => await _harness.Notices().HandleAsync(new GetPortfolioNoticesQuery(), Ct);

    private SyncHealth Failing(Guid account, SyncHealthKind kind, string reason, int days = 2) =>
        new(account, kind, days, reason, _harness.Now.AddHours(-1), 1, null, false, null, 0);

    [Fact]
    public async Task An_item_carries_the_new_fields_and_the_older_notices_keep_theirs()
    {
        var account = Facts.Add("Example One", status: ConnectionStatus.Expired, expiresAt: _harness.Now.AddHours(-1));
        var alertInstrument = _harness.Data.AddInstrument("INFY");
        var alert = _harness.Data.SeedAlert(alertInstrument.Id, AlertDirection.AtOrBelow, 1200m, _harness.Now.AddDays(-1));
        await _harness.Data.TryTriggerAsync(alert.Id, 1190m, _harness.Now.AddHours(-2), Ct);

        var notices = await ReadAsync();

        var reconnect = notices.Items.Single(n => n.Kind == "reconnect_dhan");
        (reconnect.Severity, reconnect.Title, reconnect.Resolved, reconnect.AccountId, reconnect.Since).ShouldBe(("error", "Reconnect Dhan", false, account.Id, _harness.Now));
        reconnect.Actions.ShouldBe([new AttentionAction("Reconnect Dhan", "route", $"/accounts?account={account.Id}&tab=connection")]);
        var older = notices.Items.Single(n => n.Kind == "price_alert_triggered");
        (older.Severity, older.AlertId, older.Resolved).ShouldBe(("warn", alert.Id, false)); // the old shape is as it was
        older.Actions.ShouldBe([new AttentionAction("Open", "route", "/portfolio?alerts=triggered")]);
    }

    [Fact]
    public async Task Items_are_ordered_error_then_warning_then_info_and_newest_first_inside_a_level()
    {
        var account = Facts.Add("Example One", status: ConnectionStatus.Expired, expiresAt: _harness.Now.AddHours(-1));
        Facts.Dividends[account.Id] = new PendingDividends(1, Guid.NewGuid());
        await ReadAsync(); // the error and the info are seen now

        _harness.Advance(TimeSpan.FromMinutes(5));
        Facts.Unmatched[account.Id] = new UnmatchedCounts(1, 0); // a warning, newer
        var second = Facts.Add("Example Two", status: ConnectionStatus.NotConnected); // another warning, newer still

        var kinds = (await ReadAsync()).Items.Select(n => (n.Severity, n.Kind, n.AccountId)).ToList();

        kinds.ShouldBe(
        [
            ("error", "reconnect_dhan", account.Id),
            ("warning", "reconnect_dhan", second.Id),
            ("warning", "securities_unmatched", account.Id),
            ("info", "dividends_pending", account.Id),
        ]);
    }

    [Fact]
    public async Task Since_is_when_the_item_was_first_seen_and_does_not_move_on_later_reads()
    {
        Facts.Add("Example One", status: ConnectionStatus.Expired, expiresAt: _harness.Now.AddHours(-1));
        var first = _harness.Now;
        await ReadAsync();

        _harness.Advance(TimeSpan.FromHours(3));
        var later = (await ReadAsync()).Items.Single();

        (later.Since, later.At).ShouldBe((first, first));
    }

    [Fact]
    public async Task An_item_whose_condition_stops_holding_is_resolved_kept_for_a_day_and_then_dropped()
    {
        var account = Facts.Add("Example One");
        Facts.Health[(account.Id, SyncHealthKind.Trades)] = Failing(account.Id, SyncHealthKind.Trades, "rejected");
        (await ReadAsync()).Items.Single().Resolved.ShouldBeFalse();

        Facts.Health.Clear(); // a run succeeded
        _harness.Advance(TimeSpan.FromMinutes(10));
        var resolved = (await ReadAsync()).Items.ShouldHaveSingleItem();

        (resolved.Resolved, resolved.Title, resolved.Kind).ShouldBe((true, "Trades are not being read", "sync_failing"));
        resolved.Actions!.Count.ShouldBe(2); // what it had is still shown

        _harness.Advance(TimeSpan.FromHours(23));
        (await ReadAsync()).Items.ShouldHaveSingleItem().Resolved.ShouldBeTrue();

        _harness.Advance(TimeSpan.FromHours(2));
        (await ReadAsync()).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_resolved_item_comes_back_as_a_new_unresolved_one_with_a_new_since()
    {
        var account = Facts.Add("Example One");
        Facts.Health[(account.Id, SyncHealthKind.Trades)] = Failing(account.Id, SyncHealthKind.Trades, "rejected");
        await ReadAsync();
        Facts.Health.Clear();
        await ReadAsync();

        _harness.Advance(TimeSpan.FromHours(1));
        Facts.Health[(account.Id, SyncHealthKind.Trades)] = Failing(account.Id, SyncHealthKind.Trades, "rejected");
        var again = (await ReadAsync()).Items.ShouldHaveSingleItem();

        (again.Resolved, again.Since).ShouldBe((false, _harness.Now));
    }

    [Fact]
    public async Task A_changed_situation_is_a_new_item_and_the_old_one_resolves()
    {
        var account = Facts.Add("Example One");
        Facts.Health[(account.Id, SyncHealthKind.Trades)] = Failing(account.Id, SyncHealthKind.Trades, "rejected");
        var first = (await ReadAsync()).Items.Single().Id;

        Facts.Health[(account.Id, SyncHealthKind.Trades)] = Failing(account.Id, SyncHealthKind.Trades, "history_too_large");
        var items = (await ReadAsync()).Items;

        items.Count.ShouldBe(2);
        items[0].Id.ShouldNotBe(first);
        (items[0].Resolved, items[1].Resolved, items[1].Id).ShouldBe((false, true, first));
    }

    [Fact]
    public async Task The_facts_are_read_once_and_remembered_in_one_reconciliation_however_many_accounts_there_are()
    {
        for (var i = 0; i < 40; i++)
        {
            var account = Facts.Add("Example " + i);
            Facts.Dividends[account.Id] = new PendingDividends(1, Guid.NewGuid());
        }

        var notices = await ReadAsync();

        notices.Items.Count.ShouldBe(40);
        (Facts.Loads, _harness.AttentionStore.Reconciliations).ShouldBe((1, 1));
    }

    [Fact]
    public async Task The_read_of_the_notices_writes_nothing_and_shows_only_what_was_remembered()
    {
        Facts.Add("Example One", status: ConnectionStatus.Expired, expiresAt: _harness.Now.AddHours(-1));

        var before = await GetAsync();
        await GetAsync();

        before.Items.ShouldBeEmpty(); // the cause is there, but nobody has reconciled yet
        (_harness.AttentionStore.Reconciliations, Facts.Loads, _harness.AttentionStore.Items.Count).ShouldBe((0, 0, 0));

        await _harness.AttentionReconciler().ReconcileAsync(Ct);
        (await GetAsync()).Items.ShouldHaveSingleItem().Kind.ShouldBe("reconnect_dhan");
    }

    [Fact]
    public async Task Reads_between_two_reconciliations_see_the_same_items_whatever_the_facts_do_meanwhile()
    {
        var account = Facts.Add("Example One");
        Facts.Dividends[account.Id] = new PendingDividends(1, Guid.NewGuid());
        var first = await ReadAsync();

        Facts.Dividends.Clear(); // the cause is gone, but the bell shows what was last reconciled until then
        _harness.Advance(TimeSpan.FromSeconds(20));
        var again = await GetAsync();
        var second = await GetAsync();

        Shape(again).ShouldBe(Shape(first));
        Shape(second).ShouldBe(Shape(first));
    }

    [Fact]
    public async Task Facts_that_cannot_be_read_leave_the_remembered_items_as_they_were_and_the_reconciler_does_not_throw()
    {
        var account = Facts.Add("Example One");
        Facts.Dividends[account.Id] = new PendingDividends(1, Guid.NewGuid());
        await ReadAsync();
        var reconciler = new AttentionReconciler(new BrokenFacts(), _harness.AttentionStore, _harness.HealSettings, _harness.Clock, Microsoft.Extensions.Logging.Abstractions.NullLogger<AttentionReconciler>.Instance);

        await reconciler.ReconcileAsync(Ct);

        (await GetAsync()).Items.ShouldHaveSingleItem().Resolved.ShouldBeFalse();
        _harness.AttentionStore.Reconciliations.ShouldBe(1);
    }

    [Fact]
    public async Task A_cancelled_read_of_the_facts_is_not_swallowed_by_the_reconciler()
    {
        var reconciler = new AttentionReconciler(new BrokenFacts(new OperationCanceledException(new CancellationToken(true))), _harness.AttentionStore, _harness.HealSettings, _harness.Clock, Microsoft.Extensions.Logging.Abstractions.NullLogger<AttentionReconciler>.Instance);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await reconciler.ReconcileAsync(cancelled.Token));
    }

    [Fact]
    public async Task A_store_that_cannot_be_read_leaves_the_older_notices_alone()
    {
        var instrument = _harness.Data.AddInstrument("INFY");
        var alert = _harness.Data.SeedAlert(instrument.Id, AlertDirection.AtOrBelow, 1200m, _harness.Now.AddDays(-1));
        await _harness.Data.TryTriggerAsync(alert.Id, 1190m, _harness.Now.AddHours(-2), Ct);
        var handler = new GetPortfolioNoticesHandler(_harness.Data, _harness.Evaluator(), _harness.Cache, new BrokenStore(), _harness.Clock, Microsoft.Extensions.Logging.Abstractions.NullLogger<GetPortfolioNoticesHandler>.Instance);

        var notices = await handler.HandleAsync(new GetPortfolioNoticesQuery(), Ct);

        notices.Items.ShouldHaveSingleItem().Kind.ShouldBe("price_alert_triggered");
    }

    [Fact]
    public async Task A_cancelled_read_of_the_store_is_not_swallowed()
    {
        var handler = new GetPortfolioNoticesHandler(_harness.Data, _harness.Evaluator(), _harness.Cache, new BrokenStore(new OperationCanceledException(new CancellationToken(true))), _harness.Clock, Microsoft.Extensions.Logging.Abstractions.NullLogger<GetPortfolioNoticesHandler>.Instance);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await handler.HandleAsync(new GetPortfolioNoticesQuery(), cancelled.Token));
    }

    // ---- retention (ADR 0030) ----

    [Fact]
    public async Task Resolved_items_older_than_the_retention_are_deleted_and_the_rest_stay()
    {
        var account = Facts.Add("Example One");
        Facts.Dividends[account.Id] = new PendingDividends(1, Guid.NewGuid());
        Facts.Unmatched[account.Id] = new UnmatchedCounts(1, 0);
        await ReadAsync();
        Facts.Dividends.Clear(); // the dividend item is resolved now ...
        _harness.Advance(TimeSpan.FromMinutes(1));
        await ReadAsync();
        var resolvedAt = _harness.Now;

        _harness.Advance(TimeSpan.FromDays(29)); // ... and is 29 days old: kept
        await _harness.AttentionReconciler().ApplyRetentionAsync(Ct);
        _harness.AttentionStore.Items.Count.ShouldBe(2);

        _harness.Advance(TimeSpan.FromDays(1.5)); // 30 and a half days: deleted; the unmatched item still holds and is never deleted
        await _harness.AttentionReconciler().ApplyRetentionAsync(Ct);

        _harness.AttentionStore.Items.Values.ShouldHaveSingleItem().Kind.ShouldBe("securities_unmatched");
        resolvedAt.ShouldBeLessThan(_harness.Now.AddDays(-30));
    }

    [Fact]
    public async Task An_unresolved_item_is_never_deleted_however_old()
    {
        var account = Facts.Add("Example One");
        Facts.Unmatched[account.Id] = new UnmatchedCounts(1, 0);
        await ReadAsync();

        _harness.Advance(TimeSpan.FromDays(400));
        await _harness.AttentionReconciler().ApplyRetentionAsync(Ct);

        _harness.AttentionStore.Items.Values.ShouldHaveSingleItem().ResolvedAt.ShouldBeNull();
    }

    [Fact]
    public async Task The_retention_follows_the_setting()
    {
        var account = Facts.Add("Example One");
        Facts.Unmatched[account.Id] = new UnmatchedCounts(1, 0);
        await ReadAsync();
        Facts.Unmatched.Clear();
        await ReadAsync();
        _harness.HealSettings.AttentionRetentionDays = 7;

        _harness.Advance(TimeSpan.FromDays(6));
        await _harness.AttentionReconciler().ApplyRetentionAsync(Ct);
        _harness.AttentionStore.Items.Count.ShouldBe(1);

        _harness.Advance(TimeSpan.FromDays(2));
        await _harness.AttentionReconciler().ApplyRetentionAsync(Ct);
        _harness.AttentionStore.Items.ShouldBeEmpty();
    }

    private static List<(string Id, string Text, bool Resolved, DateTimeOffset? Since)> Shape(PortfolioNotices notices) => [.. notices.Items.Select(n => (n.Id, n.Text, n.Resolved, n.Since))];

    private sealed class BrokenFacts(Exception? failure = null) : IAttentionFactsSource
    {
        public ValueTask<AttentionFacts> LoadAsync(CancellationToken cancellationToken) => throw (failure ?? new InvalidOperationException("the database is down"));
    }

    private sealed class BrokenStore(Exception? failure = null) : IAttentionStore
    {
        public ValueTask ReconcileAsync(IReadOnlyList<AttentionDraft> current, DateTimeOffset now, CancellationToken cancellationToken) => throw (failure ?? new InvalidOperationException("the database is down"));

        public ValueTask<IReadOnlyList<StoredAttention>> ListAsync(DateTimeOffset now, TimeSpan keepResolved, CancellationToken cancellationToken) => throw (failure ?? new InvalidOperationException("the database is down"));

        public ValueTask<int> DeleteResolvedBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken) => throw (failure ?? new InvalidOperationException("the database is down"));
    }
}
