using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Income;
using static XDrishti.Application.Tests.Portfolio.HoldingFixtures;
using static XDrishti.Application.Tests.Portfolio.Income.IncomeFixtures;

namespace XDrishti.Application.Tests.Portfolio.Income;

/// <summary>Where the recorded dividends and costs go: to the holding they belong to, else to the account, and nowhere twice (ADR 0026).</summary>
public sealed class IncomeBookTests
{
    private static readonly DateOnly Day = D(2026, 3, 3);

    private static HoldingData Aaa(string? isin = "INE000A01011", string status = "open") =>
        Holding(60m, "AAA", status, lots: Open(10, 50m, D(2025, 1, 1))).With(isin);

    [Fact]
    public void A_dividend_goes_to_its_holding_and_an_unallocated_one_to_its_account()
    {
        var aaa = Aaa();
        var book = Book([aaa], [Dividend(aaa, Day, 100m), Dividend(null, Day.AddDays(1), 40m)]);

        book.Of(aaa.Id).Dividends.ShouldBe([new(Day, 100m)]);
        book.Account(Account).Dividends.ShouldBe([new(Day.AddDays(1), 40m)]);
    }

    [Fact]
    public void A_dividend_of_a_holding_the_scope_does_not_know_falls_to_the_account_and_is_not_lost()
    {
        var aaa = Aaa();
        var book = Book([aaa], [new IncomeDividend(Account, Guid.NewGuid(), Day, 25m)]);

        book.Of(aaa.Id).Dividends.ShouldBeEmpty();
        book.Account(Account).Dividends.ShouldBe([new(Day, 25m)]);
    }

    [Fact]
    public void Trade_charges_belong_to_the_holding_of_the_same_isin_and_the_ledger_stays_with_the_account()
    {
        var aaa = Aaa();
        var book = Book([aaa], tradeCharges: [Charge(aaa, Day, 12.5m)], ledgerCharges: [Ledger(Day, 354m)]);

        book.Of(aaa.Id).Charges.ShouldBe([new(Day, 12.5m)]);
        book.Account(Account).LedgerCharges.ShouldBe([new(Day, 354m)]);
        book.Account(Account).TradeCharges.ShouldBeEmpty();
    }

    [Fact]
    public void The_charges_of_a_security_no_holding_has_belong_to_the_account_so_that_nothing_is_dropped()
    {
        var aaa = Aaa();
        var book = Book([aaa], tradeCharges: [new IncomeTradeCharge(Account, "INE999Z01019", "GONE", Day, 3m), Charge(aaa, Day, 1m)]);

        book.Of(aaa.Id).Charges.Sum(f => f.Amount).ShouldBe(1m);
        book.Account(Account).TradeCharges.ShouldBe([new(Day, 3m)]);
    }

    [Fact]
    public void A_trade_without_an_isin_finds_the_holding_by_symbol_but_two_different_isins_are_never_joined()
    {
        var aaa = Aaa();
        var withIsin = Book([aaa], tradeCharges: [new IncomeTradeCharge(Account, null, "NSE:AAA-EQ", Day, 2m)]);
        var otherIsin = Book([aaa], tradeCharges: [new IncomeTradeCharge(Account, "INE111A01011", "AAA", Day, 2m)]);
        var noHoldingIsin = Book([Aaa(isin: null)], tradeCharges: [new IncomeTradeCharge(Account, "INE111A01011", "AAA", Day, 2m)]);

        withIsin.Of(aaa.Id).Charges.ShouldBe([new(Day, 2m)]);
        otherIsin.Of(aaa.Id).Charges.ShouldBeEmpty();
        otherIsin.Account(Account).TradeCharges.ShouldBe([new(Day, 2m)]);
        noHoldingIsin.Account(Account).TradeCharges.ShouldBeEmpty();
    }

    [Fact]
    public void Another_accounts_holding_of_the_same_security_gets_nothing()
    {
        var mine = Aaa();
        var theirs = Aaa().With("INE000A01011", OtherAccount);
        var book = Book([mine, theirs], tradeCharges: [Charge(mine, Day, 5m)]);

        book.Of(mine.Id).Charges.ShouldBe([new(Day, 5m)]);
        book.Of(theirs.Id).Charges.ShouldBeEmpty();
    }

    [Fact]
    public void With_more_than_one_candidate_the_open_holding_takes_the_charge()
    {
        var closed = Aaa(status: "closed");
        var open = Aaa();
        var book = Book([closed, open], tradeCharges: [new IncomeTradeCharge(Account, "INE000A01011", "AAA", Day, 4m)]);

        book.Of(open.Id).Charges.ShouldBe([new(Day, 4m)]);
        book.Of(closed.Id).Charges.ShouldBeEmpty();
    }

    [Fact]
    public void Every_recorded_rupee_is_in_exactly_one_place()
    {
        var aaa = Aaa();
        var bbb = Holding(10m, "BBB", lots: Open(1, 5m, D(2025, 1, 1))).With("INE222B01011");
        var dividends = new[] { Dividend(aaa, Day, 10m), Dividend(bbb, Day, 20m), Dividend(null, Day, 30m) };
        var charges = new[] { Charge(aaa, Day, 1m), Charge(bbb, Day, 2m), new IncomeTradeCharge(Account, null, "ZZZ", Day, 3m) };
        var book = Book([aaa, bbb], dividends, charges, [Ledger(Day, 4m)]);

        var places = new[] { book.Of(aaa.Id), book.Of(bbb.Id) };
        var account = book.Account(Account);

        (places.Sum(p => p.Dividends.Sum(f => f.Amount)) + account.Dividends.Sum(f => f.Amount)).ShouldBe(60m);
        (places.Sum(p => p.Charges.Sum(f => f.Amount)) + account.TradeCharges.Sum(f => f.Amount) + account.LedgerCharges.Sum(f => f.Amount)).ShouldBe(10m);
    }

    [Fact]
    public void The_flows_of_a_place_are_sorted_by_date_so_the_book_does_not_depend_on_the_order_read()
    {
        var aaa = Aaa();
        var book = Book([aaa], [Dividend(aaa, D(2026, 5, 1), 3m), Dividend(aaa, D(2026, 1, 1), 2m), Dividend(aaa, D(2026, 1, 1), 1m)]);

        book.Of(aaa.Id).Dividends.ShouldBe([new(D(2026, 1, 1), 1m), new(D(2026, 1, 1), 2m), new(D(2026, 5, 1), 3m)]);
    }

    [Fact]
    public void The_ledger_start_of_an_account_is_kept_and_an_unknown_place_is_empty()
    {
        var book = Book([Aaa()], ledgerStarts: [new IncomeLedgerStart(Account, D(2025, 2, 2))]);

        book.Account(Account).LedgerFrom.ShouldBe(D(2025, 2, 2));
        book.Account(OtherAccount).ShouldBe(AccountIncome.None);
        book.Of(Guid.NewGuid()).ShouldBe(HoldingIncome.None);
        IncomeBook.Empty.AccountIds.ShouldBeEmpty();
    }

    [Fact]
    public void Only_the_ledgers_costs_of_the_account_as_such_are_counted_and_those_of_trades_are_left_to_the_trade_rows()
    {
        var aaa = Aaa();
        var book = Book(
            [aaa],
            tradeCharges: [Charge(aaa, Day, 12.5m)],
            ledgerCharges:
            [
                Ledger(Day, 354m, narration: "DP charges"),
                Ledger(Day, 590m, narration: "Annual maintenance charges"),
                Ledger(Day, 8.2m, narration: "DPC Interest : 01/02/2026 to 28/02/2026"),
                Ledger(Day, 12.5m, narration: "Brokerage"),
                Ledger(Day, 9m, narration: "STT"),
                Ledger(Day, 40m, narration: "Bill for 03/03/2026: brokerage, STT, GST, stamp duty"),
                Ledger(Day, 3m, narration: "Settlement of trades"),
            ]);

        book.Account(Account).LedgerCharges.ShouldBe([new(Day, 354m + 590m + 8.2m)]); // summed per day
        book.Of(aaa.Id).Charges.ShouldBe([new(Day, 12.5m)]);
        book.Account(Account).TradeCharges.ShouldBeEmpty();
    }

    [Fact]
    public void A_refund_on_a_day_nets_against_that_days_cost_and_a_day_that_nets_to_nothing_is_dropped()
    {
        var book = Book([Aaa()], ledgerCharges: [Ledger(Day, 100m), Ledger(Day, -100m), Ledger(Day.AddDays(1), 50m), Ledger(Day.AddDays(1), -20m)]);

        book.Account(Account).LedgerCharges.ShouldBe([new(Day.AddDays(1), 30m)]);
    }

    [Fact]
    public void Two_open_holdings_of_one_isin_are_ambiguous_as_when_trades_are_attached_and_the_charge_goes_to_the_account()
    {
        var nse = Aaa();
        var bse = Aaa();
        var book = Book([nse, bse], tradeCharges: [new IncomeTradeCharge(Account, "INE000A01011", "AAA", Day, 4m)]);

        book.Of(nse.Id).Charges.ShouldBeEmpty();
        book.Of(bse.Id).Charges.ShouldBeEmpty();
        book.Account(Account).TradeCharges.ShouldBe([new(Day, 4m)]);
    }

    [Fact]
    public void An_open_holding_is_preferred_to_a_closed_one_of_the_same_security()
    {
        var closed = Aaa(status: "closed");
        var open = Aaa();
        var book = Book([closed, open], tradeCharges: [new IncomeTradeCharge(Account, "INE000A01011", "AAA", Day, 4m)]);

        book.Of(open.Id).Charges.ShouldBe([new(Day, 4m)]);
        book.Of(closed.Id).Charges.ShouldBeEmpty();
    }

    [Fact]
    public void A_security_only_a_closed_holding_has_is_the_closed_holdings_when_it_is_the_only_one()
    {
        var closed = Aaa(status: "closed");
        var book = Book([closed], tradeCharges: [new IncomeTradeCharge(Account, "INE000A01011", "AAA", Day, 4m)]);

        book.Of(closed.Id).Charges.ShouldBe([new(Day, 4m)]);
    }

    [Fact]
    public void Two_closed_holdings_of_one_security_and_no_open_one_are_ambiguous_too()
    {
        var first = Aaa(status: "closed");
        var second = Aaa(status: "closed");
        var book = Book([first, second], tradeCharges: [new IncomeTradeCharge(Account, "INE000A01011", "AAA", Day, 4m)]);

        book.Account(Account).TradeCharges.ShouldBe([new(Day, 4m)]);
    }
}
