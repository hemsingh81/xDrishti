using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Attention;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Tests.Portfolio.Heal;

/// <summary>The rules that turn the facts into attention items (ADR 0027), each on its own: when an item is raised, what it says, the actions it carries, and how its id follows the situation. Monday 2026-10-12 17:00 India time.</summary>
public sealed class AttentionDeriverTests
{
    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 12, 17, 0, 0, Ist));
    private readonly FixedHealSettings _settings = new();
    private readonly StaticAttentionFacts _facts;

    public AttentionDeriverTests() => _facts = new StaticAttentionFacts(_clock);

    private DateTimeOffset Now => _clock.GetUtcNow();

    private IReadOnlyList<AttentionItem> Derive() => AttentionDeriver.Derive(_facts.LoadAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult(), _settings);

    private void Fail(Guid account, SyncHealthKind kind, string reason, int days = 1, int attempts = 1, DateTimeOffset? at = null) =>
        _facts.Health[(account, kind)] = new SyncHealth(account, kind, days, reason, at ?? Now.AddHours(-1), attempts, null, false, null, 0);

    // ---- Reconnect Dhan ----

    [Fact]
    public void A_connected_account_with_a_long_login_raises_nothing()
    {
        _facts.Add("Example One");

        Derive().ShouldBeEmpty();
    }

    [Fact]
    public void An_expired_login_is_an_error_that_opens_the_connection_tab()
    {
        var account = _facts.Add("Example One", status: ConnectionStatus.Expired, expiresAt: Now.AddHours(-3));

        var item = Derive().ShouldHaveSingleItem();

        (item.Kind, item.Severity, item.Title).ShouldBe(("reconnect_dhan", "error", "Reconnect Dhan"));
        item.Text.ShouldContain("Example One");
        item.Actions.ShouldBe([new AttentionAction("Reconnect Dhan", "route", $"/accounts?account={account.Id}&tab=connection")]);
        item.AccountId.ShouldBe(account.Id);
    }

    [Fact]
    public void A_missing_login_is_a_warning_that_opens_the_connection_tab()
    {
        _facts.Add("Example One", status: ConnectionStatus.NotConnected);

        var item = Derive().ShouldHaveSingleItem();

        (item.Kind, item.Severity, item.Title).ShouldBe(("reconnect_dhan", "warning", "Connect to Dhan"));
    }

    [Theory]
    [InlineData(30, true)]
    [InlineData(119, true)]
    [InlineData(121, false)]
    public void A_login_that_runs_out_within_two_hours_is_a_warning_naming_the_time(int minutesLeft, bool raised)
    {
        _facts.Add("Example One", expiresAt: Now.AddMinutes(minutesLeft));

        var items = Derive();

        items.Count.ShouldBe(raised ? 1 : 0);
        if (raised)
        {
            (items[0].Severity, items[0].Text).ShouldBe(("warning", $"The Dhan login of Example One runs out at {Now.AddMinutes(minutesLeft).ToOffset(Ist):HH:mm} IST."));
        }
    }

    [Fact]
    public void The_id_follows_the_situation_so_a_renewed_and_a_later_expired_login_are_different_items()
    {
        var account = _facts.Add("Example One", status: ConnectionStatus.Expired, expiresAt: Now.AddHours(-3));
        var first = Derive().Single().Id;
        _facts.Accounts[0] = account with { Status = ConnectionStatus.Expired, ExpiresAt = Now.AddHours(-1) }; // signed in again meanwhile, and that login ran out too

        var second = Derive().Single().Id;

        second.ShouldNotBe(first);
        first.ShouldContain(account.Id.ToString("N"));
    }

    [Fact]
    public void An_account_without_the_portfolio_role_still_raises_its_login_but_nothing_else()
    {
        var account = _facts.Add("Example Two", portfolio: false, status: ConnectionStatus.Expired, expiresAt: Now.AddHours(-3));
        Fail(account.Id, SyncHealthKind.Trades, "rejected", days: 5);
        _facts.Dividends[account.Id] = new PendingDividends(2, Guid.NewGuid());

        Derive().ShouldHaveSingleItem().Kind.ShouldBe("reconnect_dhan");
    }

    // ---- a sync that keeps failing ----

    [Fact]
    public void A_sync_that_failed_on_one_day_raises_nothing_yet()
    {
        var account = _facts.Add("Example One");
        Fail(account.Id, SyncHealthKind.Trades, "rejected", days: 1);

        Derive().ShouldBeEmpty();
    }

    [Fact]
    public void A_sync_that_failed_on_two_days_in_a_row_is_an_error_with_retry_now_and_open()
    {
        var account = _facts.Add("Example One");
        Fail(account.Id, SyncHealthKind.Trades, "rejected", days: 2);

        var item = Derive().ShouldHaveSingleItem();

        (item.Kind, item.Severity, item.Title).ShouldBe(("sync_failing", "error", "Trades are not being read"));
        item.Text.ShouldBe("The trades of Example One have not been updated for 2 days: Dhan refused the request.");
        item.Actions.ShouldBe(
        [
            new AttentionAction("Retry now", "command", $"POST /api/accounts/{account.Id}/sync/broker-history"),
            new AttentionAction("Open", "route", $"/accounts?account={account.Id}&tab=portfolio"),
        ]);
        item.Id.ShouldBe($"attention:sync:{account.Id:N}:trades:rejected");
    }

    [Theory]
    [InlineData(SyncHealthKind.Holdings, "Holdings are not being updated", "POST /api/accounts/{0}/holdings/sync")]
    [InlineData(SyncHealthKind.Trades, "Trades are not being read", "POST /api/accounts/{0}/sync/broker-history")]
    [InlineData(SyncHealthKind.Ledger, "The ledger is not being read", "POST /api/accounts/{0}/sync/broker-history")]
    public void Each_kind_has_its_own_title_and_retries_through_the_existing_endpoint(SyncHealthKind kind, string title, string command)
    {
        var account = _facts.Add("Example One");
        Fail(account.Id, kind, "unexpected", days: 2);

        var item = Derive().ShouldHaveSingleItem();

        item.Title.ShouldBe(title);
        item.Actions[0].ShouldBe(new AttentionAction("Retry now", "command", string.Format(System.Globalization.CultureInfo.InvariantCulture, command, account.Id)));
    }

    [Fact]
    public void The_quote_poll_is_kept_against_the_data_account_and_retries_through_the_services_endpoint()
    {
        var data = _facts.Add("Primary", portfolio: false);
        _facts.DataAccountId = data.Id;
        _facts.DataAccountName = "Primary";
        Fail(data.Id, SyncHealthKind.Quotes, "unavailable", days: 2, attempts: 9);

        var item = Derive().ShouldHaveSingleItem();

        (item.Title, item.Actions[0].Target, item.Actions[1].Target).ShouldBe(("Prices are not being updated", "POST /api/services/holdings-quotes/run", "/services"));
    }

    [Fact]
    public void A_price_poll_failing_on_its_second_day_is_told_at_once_because_nothing_retries_it_quietly()
    {
        var data = _facts.Add("Primary", portfolio: false);
        _facts.DataAccountId = data.Id;
        _facts.DataAccountName = "Primary";
        Fail(data.Id, SyncHealthKind.Quotes, "unavailable", days: 2, attempts: 1, at: Now.AddSeconds(-20)); // the first failure of the day, just now: a trades sync would wait for its retry

        var item = Derive().ShouldHaveSingleItem();

        item.Title.ShouldBe("Prices are not being updated");
    }

    [Fact]
    public void A_reason_only_the_owner_can_fix_is_told_at_once_with_the_reconnect_action()
    {
        var account = _facts.Add("Example One");
        Fail(account.Id, SyncHealthKind.Holdings, "token_refused", days: 1);

        var item = Derive().ShouldHaveSingleItem();

        item.Kind.ShouldBe("sync_failing");
        item.Actions.ShouldBe([new AttentionAction("Reconnect Dhan", "route", $"/accounts?account={account.Id}&tab=connection")]);
    }

    [Theory]
    [InlineData("token_expired")]
    [InlineData("not_connected")]
    public void An_expired_or_missing_login_is_left_to_the_reconnect_item(string reason)
    {
        var account = _facts.Add("Example One", status: ConnectionStatus.Expired, expiresAt: Now.AddHours(-3));
        Fail(account.Id, SyncHealthKind.Trades, reason, days: 3);

        Derive().ShouldHaveSingleItem().Kind.ShouldBe("reconnect_dhan");
    }

    [Fact]
    public void Nothing_is_raised_while_a_quiet_retry_is_still_to_come_even_on_the_second_day()
    {
        var account = _facts.Add("Example One");
        Fail(account.Id, SyncHealthKind.Trades, "unavailable", days: 2, attempts: 1, at: Now.AddSeconds(-20)); // the first attempt of today failed 20 s ago: a retry is due in a minute

        Derive().ShouldBeEmpty();
    }

    [Fact]
    public void The_item_comes_once_the_retries_are_used_up()
    {
        var account = _facts.Add("Example One");
        Fail(account.Id, SyncHealthKind.Trades, "unavailable", days: 2, attempts: 4, at: Now.AddMinutes(-2));

        Derive().ShouldHaveSingleItem().Text.ShouldContain("Dhan could not be reached");
    }

    [Fact]
    public void A_changed_reason_is_a_new_item()
    {
        var account = _facts.Add("Example One");
        Fail(account.Id, SyncHealthKind.Trades, "rejected", days: 2);
        var first = Derive().Single().Id;
        Fail(account.Id, SyncHealthKind.Trades, "history_too_large", days: 3);

        Derive().Single().Id.ShouldNotBe(first);
    }

    [Fact]
    public void The_threshold_is_the_setting()
    {
        var account = _facts.Add("Example One");
        Fail(account.Id, SyncHealthKind.Trades, "rejected", days: 2);
        _settings.PersistDays = 3;

        Derive().ShouldBeEmpty();
    }

    // ---- holdings that need review ----

    private static BrokerSyncCounts Counts(params BrokerNotRebuilt[] left) => BrokerSyncCounts.None with { NotRebuilt = left, NotRebuiltTotal = left.Length, NeedsReview = left.Count(n => n.Reason == "needs_review") };

    [Fact]
    public void Holdings_with_hand_dated_lots_are_one_warning_that_names_them_and_opens_the_first_ones_rebuild_preview()
    {
        var account = _facts.Add("Example One");
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        _facts.TradeCounts[account.Id] = Counts(new BrokerNotRebuilt("BETA", "needs_review", null, b), new BrokerNotRebuilt("ALPHA", "needs_review", null, a));
        _facts.Flags[a] = new HoldingFlags(true, true);
        _facts.Flags[b] = new HoldingFlags(true, true);

        var item = Derive().ShouldHaveSingleItem();

        (item.Kind, item.Severity, item.Title).ShouldBe(("holdings_need_review", "warning", "Holdings need your review"));
        item.Text.ShouldStartWith("2 holdings of Example One have lots with a date you chose");
        item.Text.ShouldContain("(ALPHA, BETA)");
        item.Actions.ShouldBe([new AttentionAction("Review", "route", $"/portfolio?holding={a}&section=trades&rebuild=preview")]);
    }

    [Fact]
    public void A_holding_the_owner_has_fixed_or_closed_since_the_run_is_no_longer_named()
    {
        var account = _facts.Add("Example One");
        var fixedUp = Guid.NewGuid();
        var closed = Guid.NewGuid();
        var open = Guid.NewGuid();
        _facts.TradeCounts[account.Id] = Counts(
            new BrokerNotRebuilt("FIXED", "needs_review", null, fixedUp), new BrokerNotRebuilt("CLOSED", "needs_review", null, closed), new BrokerNotRebuilt("STILL", "needs_review", null, open));
        _facts.Flags[fixedUp] = new HoldingFlags(true, false);
        _facts.Flags[closed] = new HoldingFlags(false, true);
        _facts.Flags[open] = new HoldingFlags(true, true);

        var item = Derive().ShouldHaveSingleItem();

        item.Text.ShouldStartWith("1 holding of Example One has lots with a date you chose");
        item.Text.ShouldContain("(STILL)");
    }

    [Fact]
    public void When_every_holding_is_fixed_the_item_is_gone()
    {
        var account = _facts.Add("Example One");
        var id = Guid.NewGuid();
        _facts.TradeCounts[account.Id] = Counts(new BrokerNotRebuilt("ALPHA", "needs_review", null, id));
        _facts.Flags[id] = new HoldingFlags(true, false);

        Derive().ShouldBeEmpty();
    }

    [Fact]
    public void A_run_recorded_before_the_holding_id_was_kept_opens_the_holding_by_its_symbol()
    {
        var account = _facts.Add("Example One");
        _facts.TradeCounts[account.Id] = Counts(new BrokerNotRebuilt("AL&PHA", "needs_review"));

        Derive().ShouldHaveSingleItem().Actions[0].Target.ShouldBe("/portfolio?q=AL%26PHA");
    }

    [Fact]
    public void The_review_item_has_a_new_id_when_the_set_of_holdings_changes()
    {
        var account = _facts.Add("Example One");
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        _facts.Flags[a] = new HoldingFlags(true, true);
        _facts.Flags[b] = new HoldingFlags(true, true);
        _facts.TradeCounts[account.Id] = Counts(new BrokerNotRebuilt("ALPHA", "needs_review", null, a));
        var one = Derive().Single().Id;
        _facts.TradeCounts[account.Id] = Counts(new BrokerNotRebuilt("ALPHA", "needs_review", null, a), new BrokerNotRebuilt("BETA", "needs_review", null, b));

        Derive().Single().Id.ShouldNotBe(one);
    }

    // ---- trades that do not explain a holding ----

    [Fact]
    public void A_mismatch_that_earlier_history_could_fix_waits_for_the_deepening()
    {
        var account = _facts.Add("Example One");
        var id = Guid.NewGuid();
        _facts.TradeCounts[account.Id] = Counts(new BrokerNotRebuilt("ALPHA", "quantity_mismatch", "negative_opening", id));
        _facts.Flags[id] = new HoldingFlags(true, false);
        _facts.Health[(account.Id, SyncHealthKind.Trades)] = SyncHealth.None(account.Id, SyncHealthKind.Trades) with { DeepenedTo = new DateOnly(2024, 1, 1) };

        Derive().ShouldBeEmpty();
    }

    [Fact]
    public void A_mismatch_still_there_after_the_last_reachable_year_is_a_warning_that_opens_the_trades_section()
    {
        var account = _facts.Add("Example One");
        var id = Guid.NewGuid();
        _facts.TradeCounts[account.Id] = Counts(new BrokerNotRebuilt("ALPHA", "quantity_mismatch", "sell_exceeds_open", id));
        _facts.Flags[id] = new HoldingFlags(true, false);
        _facts.Health[(account.Id, SyncHealthKind.Trades)] = SyncHealth.None(account.Id, SyncHealthKind.Trades) with { DeepenedTo = new DateOnly(2023, 1, 1), DeepenExhausted = true };

        var item = Derive().ShouldHaveSingleItem();

        (item.Kind, item.Severity, item.Title).ShouldBe(("trades_unexplained", "warning", "Trades do not explain a holding"));
        item.Text.ShouldContain("ALPHA");
        item.Text.ShouldContain("Trades back to 2023-01-01 were read.");
        item.Actions.ShouldBe([new AttentionAction("Open trades", "route", $"/portfolio?holding={id}&section=trades")]);
    }

    [Fact]
    public void A_mismatch_earlier_history_cannot_fix_is_raised_at_once()
    {
        var account = _facts.Add("Example One");
        var id = Guid.NewGuid();
        _facts.TradeCounts[account.Id] = Counts(new BrokerNotRebuilt("ALPHA", "quantity_mismatch", "fractional_after_adjustment", id));
        _facts.Flags[id] = new HoldingFlags(true, false);

        Derive().ShouldHaveSingleItem().Kind.ShouldBe("trades_unexplained");
    }

    [Fact]
    public void With_the_deepening_switched_off_a_mismatch_is_raised_at_once()
    {
        var account = _facts.Add("Example One");
        var id = Guid.NewGuid();
        _facts.TradeCounts[account.Id] = Counts(new BrokerNotRebuilt("ALPHA", "quantity_mismatch", "negative_opening", id));
        _facts.Flags[id] = new HoldingFlags(true, false);
        _settings.DeepenMaxYears = 0;

        Derive().ShouldHaveSingleItem().Kind.ShouldBe("trades_unexplained");
    }

    [Fact]
    public void The_text_names_at_most_three_symbols_and_counts_the_rest()
    {
        var account = _facts.Add("Example One");
        var holdings = Enumerable.Range(0, 5).Select(i => (Id: Guid.NewGuid(), Symbol: "SYM" + i)).ToList();
        _facts.TradeCounts[account.Id] = Counts([.. holdings.Select(h => new BrokerNotRebuilt(h.Symbol, "quantity_mismatch", "fractional_after_adjustment", h.Id))]);
        foreach (var h in holdings)
        {
            _facts.Flags[h.Id] = new HoldingFlags(true, false);
        }

        Derive().Single().Text.ShouldContain("SYM0, SYM1, SYM2 and 2 more");
    }

    // ---- dividends and unmatched securities ----

    [Fact]
    public void Pending_dividends_are_an_info_that_opens_the_suggestions_and_follows_the_newest_entry()
    {
        var account = _facts.Add("Example One");
        var newest = Guid.Parse("11111111-2222-7333-8444-555555555555");
        _facts.Dividends[account.Id] = new PendingDividends(3, newest);

        var item = Derive().ShouldHaveSingleItem();

        (item.Kind, item.Severity, item.Title).ShouldBe(("dividends_pending", "info", "Possible dividends in the ledger"));
        item.Text.ShouldStartWith("3 credits in the ledger of Example One");
        item.Actions.ShouldBe([new AttentionAction("Review", "route", $"/portfolio?cash=suggestions&account={account.Id}")]);
        item.Id.ShouldBe($"attention:dividends:{account.Id:N}:11111111");

        _facts.Dividends[account.Id] = new PendingDividends(1, Guid.Parse("99999999-2222-7333-8444-555555555555"));
        Derive().Single().Id.ShouldNotBe(item.Id);
    }

    [Fact]
    public void Unmatched_securities_are_a_warning_that_opens_the_list_and_follows_the_counts()
    {
        var account = _facts.Add("Example One");
        _facts.Unmatched[account.Id] = new UnmatchedCounts(1, 2);

        var item = Derive().ShouldHaveSingleItem();

        (item.Kind, item.Severity).ShouldBe(("securities_unmatched", "warning"));
        item.Text.ShouldStartWith("1 holding and 2 traded securities of Example One have no match");
        item.Actions.ShouldBe([new AttentionAction("Open", "route", $"/portfolio?unmatched=1&account={account.Id}")]);
        item.Id.ShouldEndWith(":1-2");
    }

    // ---- every item ----

    [Fact]
    public void No_item_has_more_than_two_actions_and_none_names_a_token_an_amount_or_a_narration()
    {
        var account = _facts.Add("Example One");
        var id = Guid.NewGuid();
        Fail(account.Id, SyncHealthKind.Trades, "rejected", days: 4);
        Fail(account.Id, SyncHealthKind.Holdings, "unexpected", days: 4);
        _facts.TradeCounts[account.Id] = Counts(new BrokerNotRebuilt("ALPHA", "needs_review", null, id));
        _facts.Flags[id] = new HoldingFlags(true, true);
        _facts.Dividends[account.Id] = new PendingDividends(2, Guid.NewGuid());
        _facts.Unmatched[account.Id] = new UnmatchedCounts(2, 0);

        var items = Derive();

        items.Count.ShouldBe(5);
        items.ShouldAllBe(i => i.Actions.Count >= 1 && i.Actions.Count <= 2);
        items.ShouldAllBe(i => i.Id.Length <= AttentionRecord.MaxIdLength && i.Title.Length <= AttentionRecord.MaxTitleLength && i.Text.Length <= AttentionRecord.MaxTextLength);
        items.ShouldAllBe(i => !i.Text.Contains("Rs", StringComparison.Ordinal) && !i.Text.Contains('₹', StringComparison.Ordinal));
    }
}
