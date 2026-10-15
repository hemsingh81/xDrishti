using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio;
using XDrishti.Application.Portfolio.Attention;
using XDrishti.Application.Tests.Portfolio.Actions;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Tests.Portfolio.Heal;

/// <summary>
/// The attention items on <c>GET /api/portfolio/notices</c> (ADR 0027): they come with title, at most two actions, since and resolved; they are ordered error, warning, info and newest first; they resolve by themselves, stay for 24 hours resolved and are
/// then dropped; a changed situation is a new item; the facts are read once per read however many accounts there are; and a failure to work them out leaves the older notices alone.
/// </summary>
public sealed class AttentionNoticesTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly ActionsHarness _harness = new();

    private StaticAttentionFacts Facts => _harness.AttentionFacts;

    private async Task<PortfolioNotices> ReadAsync() => await _harness.Notices().HandleAsync(new GetPortfolioNoticesQuery(), Ct);

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
    public async Task Facts_that_cannot_be_read_leave_the_older_notices_alone()
    {
        var instrument = _harness.Data.AddInstrument("INFY");
        var alert = _harness.Data.SeedAlert(instrument.Id, AlertDirection.AtOrBelow, 1200m, _harness.Now.AddDays(-1));
        await _harness.Data.TryTriggerAsync(alert.Id, 1190m, _harness.Now.AddHours(-2), Ct);
        var broken = new BrokenFacts();
        var handler = new GetPortfolioNoticesHandler(_harness.Data, _harness.Evaluator(), _harness.Cache, broken, _harness.AttentionStore, _harness.HealSettings, _harness.Clock, Microsoft.Extensions.Logging.Abstractions.NullLogger<GetPortfolioNoticesHandler>.Instance);

        var notices = await handler.HandleAsync(new GetPortfolioNoticesQuery(), Ct);

        notices.Items.ShouldHaveSingleItem().Kind.ShouldBe("price_alert_triggered");
    }

    [Fact]
    public async Task A_cancelled_read_of_the_facts_is_not_swallowed()
    {
        var handler = new GetPortfolioNoticesHandler(_harness.Data, _harness.Evaluator(), _harness.Cache, new BrokenFacts(new OperationCanceledException(new CancellationToken(true))), _harness.AttentionStore, _harness.HealSettings, _harness.Clock, Microsoft.Extensions.Logging.Abstractions.NullLogger<GetPortfolioNoticesHandler>.Instance);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await handler.HandleAsync(new GetPortfolioNoticesQuery(), cancelled.Token));
    }

    private sealed class BrokenFacts(Exception? failure = null) : IAttentionFactsSource
    {
        public ValueTask<AttentionFacts> LoadAsync(CancellationToken cancellationToken) => throw (failure ?? new InvalidOperationException("the database is down"));
    }
}
