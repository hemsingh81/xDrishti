using System.Text.RegularExpressions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio;
using XDrishti.Application.Portfolio.Attention;
using XDrishti.Application.Tests.Portfolio.Actions;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Tests.Portfolio.Heal;

/// <summary>
/// The <c>rows_dropped</c> item (ADR 0030, ISS-09): per account and kind (trades, ledger), derived from the counts of the latest succeeded run of that kind. Info when the only reason is a line where no money moved, a warning otherwise; the text holds counts and plain
/// reason words only; the id holds a hash of the set of reasons; the item resolves when the latest run of the kind drops nothing. Monday 2026-10-12 17:00 India time.
/// </summary>
public sealed class AttentionRowsDroppedTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 12, 17, 0, 0, Ist));
    private readonly FixedHealSettings _settings = new();
    private readonly StaticAttentionFacts _facts;

    public AttentionRowsDroppedTests() => _facts = new StaticAttentionFacts(_clock);

    private IReadOnlyList<AttentionItem> Derive() => AttentionDeriver.Derive(_facts.LoadAsync(Ct).AsTask().GetAwaiter().GetResult(), _settings);

    private static BrokerSyncCounts Dropped(params (string Reason, int Count)[] reasons) =>
        BrokerSyncCounts.None with { Dropped = reasons.Sum(r => r.Count), DroppedByReason = reasons.ToDictionary(r => r.Reason, r => r.Count) };

    private AttentionAccount Account(string name = "Example One") => _facts.Add(name);

    // ---- when it is raised ----

    [Fact]
    public void A_run_that_dropped_nothing_raises_nothing()
    {
        var account = Account();
        _facts.TradeCounts[account.Id] = BrokerSyncCounts.None;
        _facts.LedgerCounts[account.Id] = BrokerSyncCounts.None with { Read = 40, Stored = 40 };

        Derive().ShouldBeEmpty();
    }

    [Fact]
    public void Trades_that_could_not_be_read_are_a_warning_with_counts_and_plain_words_and_a_route_to_the_sync_line()
    {
        var account = Account();
        _facts.TradeCounts[account.Id] = Dropped((BrokerDropReasons.BadQuantity, 9), (BrokerDropReasons.MissingId, 4), (BrokerDropReasons.BadTime, 2));

        var item = Derive().ShouldHaveSingleItem();

        (item.Kind, item.Severity, item.Title, item.AccountId).ShouldBe(("rows_dropped", "warning", "Some trades could not be read", account.Id));
        item.Text.ShouldBe("The latest trades read of Example One dropped 15 rows: 9 quantity missing or not a whole positive number, 4 no trade id, 2 time unreadable. Open the sync line to see where they were.");
        item.Actions.ShouldBe([new AttentionAction("Open", "route", "/portfolio?focus=sync")]);
    }

    [Fact]
    public void Only_lines_where_no_money_moved_are_an_info_for_the_ledger()
    {
        var account = Account();
        _facts.LedgerCounts[account.Id] = Dropped((BrokerDropReasons.ZeroAmount, 2));

        var item = Derive().ShouldHaveSingleItem();

        (item.Severity, item.Title).ShouldBe(("info", "Rows with no amount were left out"));
        item.Text.ShouldBe("The latest ledger read of Example One dropped 2 rows: 2 zero amount (no money moved, e.g. an opening balance). Open the sync line to see where they were.");
        item.Id.ShouldStartWith($"attention:dropped:{account.Id:N}:ledger:");
    }

    [Fact]
    public void A_zero_amount_next_to_another_reason_is_a_warning()
    {
        var account = Account();
        _facts.LedgerCounts[account.Id] = Dropped((BrokerDropReasons.ZeroAmount, 5), (BrokerDropReasons.BadAmount, 1));

        var item = Derive().ShouldHaveSingleItem();

        (item.Severity, item.Title).ShouldBe(("warning", "Some ledger lines could not be read"));
        item.Text.ShouldContain("5 zero amount");
        item.Text.ShouldContain("1 amount unreadable");
    }

    [Fact]
    public void One_row_is_worded_in_the_singular_and_more_than_three_reasons_end_in_a_count_of_the_rest()
    {
        var account = Account();
        _facts.TradeCounts[account.Id] = Dropped((BrokerDropReasons.BadPrice, 1));
        _facts.LedgerCounts[account.Id] = Dropped((BrokerDropReasons.BadDate, 4), (BrokerDropReasons.BadAmount, 3), (BrokerDropReasons.MissingAmount, 2), (BrokerDropReasons.UnreadableRow, 1), (BrokerDropReasons.StoreRefused, 1));

        var items = Derive();

        items.Single(i => i.Id.Contains(":trades:", StringComparison.Ordinal)).Text.ShouldBe("The latest trades read of Example One dropped 1 row: 1 price unreadable. Open the sync line to see where they were.");
        items.Single(i => i.Id.Contains(":ledger:", StringComparison.Ordinal)).Text.ShouldBe(
            "The latest ledger read of Example One dropped 11 rows: 4 date unreadable, 3 amount unreadable, 2 no amount, +2 more. Open the sync line to see where they were.");
    }

    [Fact]
    public void Trades_and_ledger_are_two_items_and_each_account_has_its_own()
    {
        var one = Account("Example One");
        var two = Account("Example Two");
        _facts.TradeCounts[one.Id] = Dropped((BrokerDropReasons.MissingSide, 1));
        _facts.LedgerCounts[one.Id] = Dropped((BrokerDropReasons.BadDate, 1));
        _facts.TradeCounts[two.Id] = Dropped((BrokerDropReasons.MissingSide, 1));

        var ids = Derive().Select(i => i.Id.Split(':')[2] + ":" + i.Id.Split(':')[3]).ToList();

        ids.Count.ShouldBe(3);
        ids.ShouldContain($"{one.Id:N}:trades");
        ids.ShouldContain($"{one.Id:N}:ledger");
        ids.ShouldContain($"{two.Id:N}:trades");
    }

    [Fact]
    public void An_account_without_the_portfolio_role_raises_nothing()
    {
        var account = _facts.Add("Example One", portfolio: false);
        _facts.TradeCounts[account.Id] = Dropped((BrokerDropReasons.BadPrice, 3));

        Derive().ShouldBeEmpty();
    }

    [Fact]
    public void A_run_recorded_before_the_reasons_were_kept_counts_them_as_other()
    {
        var account = Account();
        _facts.LedgerCounts[account.Id] = BrokerSyncCounts.None with { Dropped = 3 };

        var item = Derive().ShouldHaveSingleItem();

        (item.Severity, item.Text).ShouldBe(("warning", "The latest ledger read of Example One dropped 3 rows: 3 other. Open the sync line to see where they were."));
    }

    // ---- the id ----

    [Fact]
    public void The_id_holds_eight_hex_digits_of_the_set_of_reasons_not_of_their_counts()
    {
        var account = Account();
        _facts.TradeCounts[account.Id] = Dropped((BrokerDropReasons.BadQuantity, 9), (BrokerDropReasons.MissingId, 4));
        var first = Derive().Single().Id;
        _facts.TradeCounts[account.Id] = Dropped((BrokerDropReasons.MissingId, 1), (BrokerDropReasons.BadQuantity, 2));
        var sameSet = Derive().Single().Id;
        _facts.TradeCounts[account.Id] = Dropped((BrokerDropReasons.BadQuantity, 9));
        var otherSet = Derive().Single().Id;

        Regex.IsMatch(first, $"^attention:dropped:{account.Id:N}:trades:[0-9a-f]{{8}}$").ShouldBeTrue(first);
        sameSet.ShouldBe(first);
        otherSet.ShouldNotBe(first);
    }

    // ---- the words ----

    [Fact]
    public void Every_reason_code_has_plain_words_and_an_unknown_code_reads_other()
    {
        foreach (var code in BrokerDropReasons.All.Where(c => c != BrokerDropReasons.Other))
        {
            DropReasonWords.Of(code).ShouldNotBe("other", code);
            DropReasonWords.Of(code).ShouldNotBeNullOrWhiteSpace();
        }

        DropReasonWords.Of("a_code_from_a_later_version").ShouldBe("other");
    }

    [Fact]
    public void The_text_of_every_reason_has_only_the_account_name_counts_and_the_words_of_the_table()
    {
        var account = Account();
        foreach (var code in BrokerDropReasons.All)
        {
            _facts.TradeCounts[account.Id] = Dropped((code, 7));

            var text = Derive().Single().Text;

            text.ShouldBe($"The latest trades read of Example One dropped 7 rows: 7 {DropReasonWords.Of(code)}. Open the sync line to see where they were.", code);
        }
    }

    // ---- resolving (the reconciler remembers and resolves) ----

    private readonly ActionsHarness _harness = new();

    private async Task<List<PortfolioNotice>> ReconcileAndReadAsync()
    {
        await _harness.AttentionReconciler().ReconcileAsync(Ct);
        return [.. (await _harness.Notices().HandleAsync(new GetPortfolioNoticesQuery(), Ct)).Items.Where(i => i.Kind == "rows_dropped")];
    }

    [Fact]
    public async Task The_item_resolves_when_a_later_run_of_the_kind_drops_nothing_and_comes_back_new_when_it_drops_again()
    {
        var account = _harness.AttentionFacts.Add("Example One");
        _harness.AttentionFacts.LedgerCounts[account.Id] = Dropped((BrokerDropReasons.BadAmount, 2));
        var raised = (await ReconcileAndReadAsync()).ShouldHaveSingleItem();
        raised.Resolved.ShouldBeFalse();

        _harness.Advance(TimeSpan.FromHours(1));
        _harness.AttentionFacts.LedgerCounts[account.Id] = BrokerSyncCounts.None with { Read = 30, Stored = 5 }; // a clean later run
        var resolved = (await ReconcileAndReadAsync()).ShouldHaveSingleItem();
        (resolved.Id, resolved.Resolved, resolved.Since).ShouldBe((raised.Id, true, raised.Since));

        _harness.Advance(TimeSpan.FromHours(1));
        _harness.AttentionFacts.LedgerCounts[account.Id] = Dropped((BrokerDropReasons.BadAmount, 1));
        var again = (await ReconcileAndReadAsync()).ShouldHaveSingleItem();
        (again.Resolved, again.Since).ShouldBe((false, _harness.Now));
    }

    [Fact]
    public async Task A_changed_set_of_reasons_is_a_new_item_and_the_old_one_resolves()
    {
        var account = _harness.AttentionFacts.Add("Example One");
        _harness.AttentionFacts.TradeCounts[account.Id] = Dropped((BrokerDropReasons.BadPrice, 2));
        var first = (await ReconcileAndReadAsync()).Single();

        _harness.AttentionFacts.TradeCounts[account.Id] = Dropped((BrokerDropReasons.BadPrice, 2), (BrokerDropReasons.BadTime, 1));
        var items = await ReconcileAndReadAsync();

        items.Count.ShouldBe(2);
        items.Single(i => i.Id == first.Id).Resolved.ShouldBeTrue();
        items.Single(i => i.Id != first.Id).Resolved.ShouldBeFalse();
    }

    [Fact]
    public async Task The_other_kind_keeps_its_item_when_one_kind_resolves()
    {
        var account = _harness.AttentionFacts.Add("Example One");
        _harness.AttentionFacts.TradeCounts[account.Id] = Dropped((BrokerDropReasons.BadPrice, 2));
        _harness.AttentionFacts.LedgerCounts[account.Id] = Dropped((BrokerDropReasons.BadDate, 1));
        await ReconcileAndReadAsync();

        _harness.AttentionFacts.TradeCounts[account.Id] = BrokerSyncCounts.None;
        var items = await ReconcileAndReadAsync();

        items.Single(i => i.Id.Contains(":trades:", StringComparison.Ordinal)).Resolved.ShouldBeTrue();
        items.Single(i => i.Id.Contains(":ledger:", StringComparison.Ordinal)).Resolved.ShouldBeFalse();
    }
}
