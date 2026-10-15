using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Income;
using XDrishti.Application.Portfolio.Returns;
using static XDrishti.Application.Tests.Portfolio.HoldingFixtures;
using static XDrishti.Application.Tests.Portfolio.Income.IncomeFixtures;

namespace XDrishti.Application.Tests.Portfolio.Income;

/// <summary>
/// Dividends, charges, the total and net returns and the net XIRR (ADR 0026), valued on 2026-10-11. The rates are checked against an independent reference like <see cref="XirrTests"/>: a plain bisection with 60-digit decimal arithmetic in Python 3
/// (<c>python3 -I</c>) on the flows named in each test, sharing no code with the solver under test.
/// </summary>
public sealed class IncomeCalculatorTests
{
    private const double Tolerance = 1e-6;

    private static readonly DateOnly TwoYearsAgo = D(2024, 10, 11);

    private static readonly IReadOnlyCollection<Guid> NoAccountLevel = [];

    private static IncomeFigures? Of(IReadOnlyList<HoldingData> holdings, IncomeBook book, IReadOnlyCollection<Guid>? accountLevel = null) =>
        IncomeCalculator.Of(ReturnsCalculator.ForGroup(holdings, Valuation), holdings, book, accountLevel ?? NoAccountLevel, Valuation);

    private static IncomeFigures Figures(IReadOnlyList<HoldingData> holdings, IncomeBook book, IReadOnlyCollection<Guid>? accountLevel = null) =>
        Of(holdings, book, accountLevel).ShouldNotBeNull();

    private static HoldingData OneLot() => Holding(60m, "AAA", lots: Open(100, 50m, TwoYearsAgo)).With("INE000A01011");

    [Fact]
    public void The_gross_rate_of_the_reference_flows_is_unchanged()
    {
        // flows -5,000 on 2024-10-11, +6,000 on 2026-10-11
        ReturnsCalculator.ForGroup([OneLot()], Valuation).Xirr.Rate.ShouldNotBeNull().ShouldBe(0.095445115010, Tolerance);
    }

    [Fact]
    public void Dividends_and_a_trade_charge_change_the_rate_as_a_spreadsheet_says()
    {
        // flows -5,000 and -12.50 on 2024-10-11, +100 on 2025-09-15, +120 on 2026-09-15, +6,000 on 2026-10-11 → 11.5147946188 %
        var holding = OneLot();
        var book = Book([holding], [Dividend(holding, D(2025, 9, 15), 100m), Dividend(holding, D(2026, 9, 15), 120m)], [Charge(holding, TwoYearsAgo, 12.5m)]);

        var income = Figures([holding], book);

        income.NetXirr.State.ShouldBe(XirrState.Ok);
        income.NetXirr.Rate.ShouldNotBeNull().ShouldBe(0.115147946188, Tolerance);
    }

    [Fact]
    public void Charges_alone_lower_the_rate()
    {
        // flows -5,000 and -40 on 2024-10-11, +6,000 on 2026-10-11 → 9.1089451180 %
        var holding = OneLot();

        var income = Figures([holding], Book([holding], tradeCharges: [Charge(holding, TwoYearsAgo, 40m)]));

        income.NetXirr.Rate.ShouldNotBeNull().ShouldBe(0.091089451180, Tolerance);
        (income.Dividends, income.Charges, income.DividendsFrom).ShouldBe((0m, 40m, null));
    }

    [Fact]
    public void Dividends_alone_raise_the_rate()
    {
        // flows -5,000 on 2024-10-11, +300 on 2025-04-01, +450 on 2026-04-01, +6,000 on 2026-10-11 → 17.2350377493 %
        var holding = OneLot();

        var income = Figures([holding], Book([holding], [Dividend(holding, D(2025, 4, 1), 300m), Dividend(holding, D(2026, 4, 1), 450m)]));

        income.NetXirr.Rate.ShouldNotBeNull().ShouldBe(0.172350377493, Tolerance);
        (income.Dividends, income.Charges, income.ChargesFrom).ShouldBe((750m, 0m, null));
    }

    [Fact]
    public void Closed_lots_and_income_share_the_flows_of_one_holding()
    {
        // 70 shares kept, 30 sold on 2026-06-01 at 90 (bought 2025-01-01 at 50), price 100: flows -3,500 -1,500 -20 on 2025-01-01, +2,700 -15.50 on 2026-06-01, +250 on 2026-07-01, +7,000 on 2026-10-11 → 50.6727125247 %
        var holding = Holding(100m, "AAA", lots: [Open(70, 50m, D(2025, 1, 1)), Closed(30, 50m, D(2025, 1, 1), D(2026, 6, 1), 90m)]).With("INE000A01011");
        var book = Book([holding], [Dividend(holding, D(2026, 7, 1), 250m)], [Charge(holding, D(2025, 1, 1), 20m), Charge(holding, D(2026, 6, 1), 15.5m)]);

        var income = Figures([holding], book);

        income.NetXirr.Rate.ShouldNotBeNull().ShouldBe(0.506727125247, Tolerance);
        (income.Dividends, income.Charges).ShouldBe((250m, 35.5m));
    }

    [Fact]
    public void An_account_adds_its_unallocated_dividends_and_its_ledger_costs_to_the_flows()
    {
        // holding: +100 on 2025-09-15; account: +123.45 on 2025-10-02 (unallocated), -354 on 2025-03-31 and 2026-03-31 (ledger); lot -5,000 → +6,000 → 4.7988240891 %
        var holding = OneLot();
        var book = Book(
            [holding],
            [Dividend(holding, D(2025, 9, 15), 100m), Dividend(null, D(2025, 10, 2), 123.45m)],
            ledgerCharges: [Ledger(D(2025, 3, 31), 354m), Ledger(D(2026, 3, 31), 354m)],
            ledgerStarts: [new IncomeLedgerStart(Account, D(2025, 1, 2))]);

        var account = Figures([holding], book, [Account]);
        var onlyHolding = Figures([holding], book);

        account.NetXirr.Rate.ShouldNotBeNull().ShouldBe(0.047988240891, Tolerance);
        (account.Dividends, account.Unallocated, account.Charges).ShouldBe((223.45m, 123.45m, 708m));
        (onlyHolding.Dividends, onlyHolding.Unallocated, onlyHolding.Charges).ShouldBe((100m, null, 0m));
    }

    [Fact]
    public void Total_return_adds_the_dividends_and_the_net_return_takes_the_charges_off()
    {
        var holding = OneLot();

        var income = Figures([holding], Book([holding], [Dividend(holding, D(2026, 1, 1), 200m)], [Charge(holding, TwoYearsAgo, 30m)]));

        // (0 realised + 1,000 unrealised + 200) / 5,000 and (… - 30) / 5,000
        (income.TotalReturnPct, income.NetTotalReturnPct).ShouldBe((24m, 23.4m));
    }

    [Fact]
    public void Without_dividends_the_total_return_is_the_gross_profit_percent()
    {
        var holding = OneLot();
        var gross = ReturnsCalculator.ForGroup([holding], Valuation);

        var income = Figures([holding], Book([holding], tradeCharges: [Charge(holding, TwoYearsAgo, 30m)]));

        income.TotalReturnPct.ShouldBe(gross.PnlPct);
        income.NetTotalReturnPct.ShouldBe(19.4m);
    }

    [Fact]
    public void Realised_profit_is_part_of_the_total_return()
    {
        // invested 3,500, unrealised 3,500, realised 1,200 (30 shares sold at 90, bought at 50)
        var holding = Holding(100m, "AAA", lots: [Open(70, 50m, D(2025, 1, 1)), Closed(30, 50m, D(2025, 1, 1), D(2026, 6, 1), 90m)]).With("INE000A01011");

        var income = Figures([holding], Book([holding], [Dividend(holding, D(2026, 7, 1), 250m)], [Charge(holding, D(2026, 6, 1), 35.5m)]));

        (income.TotalReturnPct, income.NetTotalReturnPct).ShouldBe((141.4286m, 140.4143m));
    }

    [Fact]
    public void A_scope_with_nothing_recorded_has_no_income_figures()
    {
        var holding = OneLot();

        Of([holding], Book([holding])).ShouldBeNull();
        Of([holding], Book([holding], ledgerStarts: [new IncomeLedgerStart(Account, D(2025, 1, 1))]), [Account]).ShouldBeNull(); // a ledger with no cost in it is not a cost
    }

    [Fact]
    public void A_refund_that_cancels_a_cost_is_a_record_all_the_same()
    {
        var holding = OneLot();

        var income = Figures([holding], Book([holding], ledgerCharges: [Ledger(D(2026, 1, 1), -20m)]), [Account]);

        income.Charges.ShouldBe(-20m);
    }

    [Fact]
    public void A_holding_without_a_price_counts_its_income_but_has_no_returns_to_relate_it_to()
    {
        var holding = Holding(0m, "AAA", lots: Open(10, 100m, TwoYearsAgo)).With("INE000A01011");

        var income = Figures([holding], Book([holding], [Dividend(holding, D(2026, 1, 1), 50m)]));

        (income.Dividends, income.TotalReturnPct, income.NetTotalReturnPct, income.NetXirr.State).ShouldBe((50m, null, null, XirrState.NoPrice));
    }

    [Fact]
    public void In_a_scope_that_mixes_priced_and_unpriced_holdings_the_income_of_the_unpriced_is_in_the_percentages_but_not_in_the_net_xirr()
    {
        // The rule stated on IncomeCalculator (ADR 0030): the unpriced holding's dividend and charge count in the totals and in the percentages (the gross figures are of the priced holding alone: no price, no value), but its flows are not put against the priced holding's lots.
        var priced = OneLot();
        var unpriced = Holding(0m, "BBB", lots: Open(10, 100m, TwoYearsAgo)).With("INE222B01011");
        var pricedIncome = new[] { Dividend(priced, D(2025, 9, 15), 100m) };
        var pricedCharges = new[] { Charge(priced, TwoYearsAgo, 12.5m) };
        var unpricedIncome = new[] { Dividend(unpriced, D(2026, 1, 1), 50m) };
        var unpricedCharges = new[] { Charge(unpriced, D(2025, 1, 1), 5m) };

        var mixed = Figures([priced, unpriced], Book([priced, unpriced], [.. pricedIncome, .. unpricedIncome], [.. pricedCharges, .. unpricedCharges]));
        var pricedAlone = Figures([priced], Book([priced], pricedIncome, pricedCharges));

        // in the totals ...
        (mixed.Dividends, mixed.Charges).ShouldBe((150m, 17.5m));

        // ... and in the percentages: (unrealised 1,000 + dividends 150 - charges 17.5) / invested 5,000
        var gross = ReturnsCalculator.ForGroup([priced, unpriced], Valuation);
        gross.Invested.ShouldBe(5_000m);
        mixed.TotalReturnPct.ShouldBe(Math.Round((gross.RealisedPnl + gross.Pnl!.Value + 150m) / 5_000m * 100m, 4, MidpointRounding.AwayFromZero));
        mixed.NetTotalReturnPct.ShouldBe(Math.Round((gross.RealisedPnl + gross.Pnl.Value + 150m - 17.5m) / 5_000m * 100m, 4, MidpointRounding.AwayFromZero));

        // ... but not in the net XIRR, which is the priced holding's own
        mixed.NetXirr.State.ShouldBe(XirrState.Ok);
        mixed.NetXirr.Rate.ShouldBe(pricedAlone.NetXirr.Rate);
    }

    [Fact]
    public void Nothing_invested_gives_no_percentages_and_no_lots_give_no_rate()
    {
        var closed = Holding(100m, "AAA", "closed", lots: Closed(100, 50m, D(2025, 1, 1), D(2026, 6, 1), 90m)).With("INE000A01011");
        var none = Holding(10m, "BBB", lots: []).With("INE222B01011");

        var sold = Figures([closed], Book([closed], [Dividend(closed, D(2026, 7, 1), 100m)]));
        var noLots = Figures([none], Book([none], [Dividend(none, D(2026, 7, 1), 100m)]));

        (sold.TotalReturnPct, sold.NetTotalReturnPct).ShouldBe((null, null));
        noLots.NetXirr.State.ShouldBe(XirrState.NoData);
    }

    [Fact]
    public void An_account_with_nothing_but_an_unallocated_dividend_has_no_rate_to_give()
    {
        var book = Book([], [Dividend(null, D(2026, 1, 1), 80m)], extraAccounts: [Account]);

        var income = Figures([], book, [Account]);

        (income.Dividends, income.Unallocated, income.TotalReturnPct, income.NetXirr.State).ShouldBe((80m, 80m, null, XirrState.NoData));
    }

    [Fact]
    public void Under_thirty_days_the_net_rate_is_insufficient_like_the_gross_one()
    {
        var holding = Holding(60m, "AAA", lots: Open(10, 50m, Valuation.AddDays(-10))).With("INE000A01011");

        var income = Figures([holding], Book([holding], [Dividend(holding, Valuation.AddDays(-5), 5m)]));

        income.NetXirr.State.ShouldBe(ReturnsCalculator.ForGroup([holding], Valuation).Xirr.State);
        income.NetXirr.State.ShouldBe(XirrState.Insufficient);
    }

    [Fact]
    public void A_gain_beyond_a_thousand_percent_a_year_is_not_meaningful_for_the_net_rate_too()
    {
        var holding = Holding(500m, "AAA", lots: Open(10, 50m, Valuation.AddDays(-60))).With("INE000A01011");

        var income = Figures([holding], Book([holding], [Dividend(holding, Valuation.AddDays(-30), 5m)]));

        income.NetXirr.State.ShouldBe(XirrState.NotMeaningful);
    }

    [Fact]
    public void A_dividend_before_the_first_lot_is_counted_and_the_rate_found_is_a_root_of_the_flows()
    {
        // The lot is dated 2024-10-11 (say, by the import default) while the dividend was paid on 2024-01-05: money in before any money out.
        var holding = OneLot();
        var book = Book([holding], [Dividend(holding, D(2024, 1, 5), 200m)]);

        var income = Figures([holding], book);

        income.Dividends.ShouldBe(200m);
        income.NetXirr.State.ShouldBe(XirrState.Ok);
        var rate = income.NetXirr.Rate.ShouldNotBeNull();
        (200.0 - (5000.0 * Math.Pow(1 + rate, -(TwoYearsAgo.DayNumber - D(2024, 1, 5).DayNumber) / 365.0)) + (6000.0 * Math.Pow(1 + rate, -(Valuation.DayNumber - D(2024, 1, 5).DayNumber) / 365.0))).ShouldBe(0.0, 1e-6);
    }

    [Fact]
    public void A_group_takes_the_income_of_all_its_holdings_and_its_rate_comes_from_the_union_of_the_flows()
    {
        var a = OneLot();
        var b = Holding(180m, "BBB", lots: Open(10, 200m, D(2026, 4, 11))).With("INE222B01011");
        var book = Book([a, b], [Dividend(a, D(2025, 9, 15), 100m), Dividend(b, D(2026, 8, 1), 20m)], [Charge(b, D(2026, 4, 11), 10m)]);

        var income = Figures([a, b], book);
        var onlyA = Figures([a], book);

        (income.Dividends, income.Charges).ShouldBe((120m, 10m));
        (onlyA.Dividends, onlyA.Charges).ShouldBe((100m, 0m));
        income.NetXirr.Rate.ShouldNotBe(onlyA.NetXirr.Rate);
    }

    [Fact]
    public void The_dates_say_from_when_each_part_is_known_and_the_coverage_is_the_latest_start()
    {
        var holding = OneLot();
        var book = Book(
            [holding],
            [Dividend(holding, D(2025, 6, 1), 10m), Dividend(holding, D(2026, 2, 1), 10m)],
            [Charge(holding, D(2025, 3, 3), 1m)],
            [Ledger(D(2025, 5, 5), 2m)],
            [new IncomeLedgerStart(Account, D(2025, 1, 2))]);

        var account = Figures([holding], book, [Account]);
        var holdingOnly = Figures([holding], book);

        (account.DividendsFrom, account.TradeChargesFrom, account.LedgerChargesFrom).ShouldBe((D(2025, 6, 1), D(2025, 3, 3), D(2025, 1, 2)));
        (account.ChargesFrom, account.CoverageFrom).ShouldBe((D(2025, 3, 3), D(2025, 6, 1))); // the later start of the charge sources, then of all sources
        (holdingOnly.DividendsFrom, holdingOnly.ChargesFrom, holdingOnly.CoverageFrom).ShouldBe((D(2025, 6, 1), D(2025, 3, 3), D(2025, 6, 1)));
        (holdingOnly.TradeChargesFrom, holdingOnly.LedgerChargesFrom).ShouldBe((D(2025, 3, 3), null));
    }

    [Fact]
    public void A_dividend_typed_for_2019_with_charges_only_from_2024_is_counted_from_2024()
    {
        var holding = OneLot();
        var book = Book([holding], [Dividend(holding, D(2019, 6, 1), 10m)], [Charge(holding, D(2024, 3, 3), 1m)]);

        var income = Figures([holding], book);

        (income.DividendsFrom, income.TradeChargesFrom, income.ChargesFrom).ShouldBe((D(2019, 6, 1), D(2024, 3, 3), D(2024, 3, 3)));
        income.CoverageFrom.ShouldBe(D(2024, 3, 3)); // never earlier than the start of a source
    }

    [Fact]
    public void Trade_charges_from_2022_and_a_ledger_from_2024_give_charges_from_2024()
    {
        var holding = OneLot();
        var book = Book([holding], tradeCharges: [Charge(holding, D(2022, 5, 5), 1m)], ledgerCharges: [Ledger(D(2024, 4, 4), 2m)], ledgerStarts: [new IncomeLedgerStart(Account, D(2024, 1, 2))]);

        var income = Figures([holding], book, [Account]);

        (income.TradeChargesFrom, income.LedgerChargesFrom, income.ChargesFrom, income.CoverageFrom).ShouldBe((D(2022, 5, 5), D(2024, 1, 2), D(2024, 1, 2), D(2024, 1, 2)));
    }

    [Fact]
    public void The_coverage_is_the_one_source_there_is_and_the_ledger_start_counts_even_without_a_cost()
    {
        var holding = OneLot();

        var dividendsOnly = Figures([holding], Book([holding], [Dividend(holding, D(2021, 1, 1), 5m)]));
        var withLedger = Figures([holding], Book([holding], [Dividend(holding, D(2021, 1, 1), 5m)], ledgerStarts: [new IncomeLedgerStart(Account, D(2025, 7, 7))]), [Account]);

        (dividendsOnly.TradeChargesFrom, dividendsOnly.LedgerChargesFrom, dividendsOnly.ChargesFrom, dividendsOnly.CoverageFrom).ShouldBe((null, null, null, D(2021, 1, 1)));
        (withLedger.ChargesFrom, withLedger.CoverageFrom).ShouldBe((D(2025, 7, 7), D(2025, 7, 7))); // the ledger holds the costs from its start on; a cost paid before was never read
    }

    [Fact]
    public void The_ledger_part_of_the_charges_is_given_apart_for_the_account_level_and_not_below()
    {
        var holding = OneLot();
        var book = Book([holding], tradeCharges: [Charge(holding, D(2025, 1, 1), 10m)], ledgerCharges: [Ledger(D(2025, 2, 2), 354m), Ledger(D(2025, 3, 3), 8m)]);

        var account = Figures([holding], book, [Account]);
        var holdingOnly = Figures([holding], book);
        var ledgerAlone = IncomeCalculator.OfAccounts(book, [Account]).ShouldNotBeNull();

        (account.Charges, account.LedgerCharges).ShouldBe((372m, 362m));
        (holdingOnly.Charges, holdingOnly.LedgerCharges).ShouldBe((10m, null));
        (ledgerAlone.Charges, ledgerAlone.LedgerCharges).ShouldBe((362m, 362m));
    }

    [Fact]
    public void An_account_with_a_ledger_and_no_cost_in_it_has_zero_ledger_charges()
    {
        var holding = OneLot();
        var book = Book([holding], [Dividend(holding, D(2025, 1, 1), 5m)], ledgerStarts: [new IncomeLedgerStart(Account, D(2024, 1, 1))]);

        Figures([holding], book, [Account]).LedgerCharges.ShouldBe(0m);
    }

    [Fact]
    public void A_ledger_that_only_mentions_the_costs_of_trades_adds_nothing_to_the_charges()
    {
        var holding = OneLot();
        var book = Book(
            [holding],
            tradeCharges: [Charge(holding, D(2025, 1, 1), 40m)],
            ledgerCharges: [Ledger(D(2025, 1, 1), 12m, narration: "Brokerage"), Ledger(D(2025, 1, 1), 9m, narration: "STT"), Ledger(D(2025, 1, 1), 49m, narration: "Bill for 01/01/2025")],
            ledgerStarts: [new IncomeLedgerStart(Account, D(2024, 12, 1))]);

        var income = Figures([holding], book, [Account]);

        (income.Charges, income.LedgerCharges).ShouldBe((40m, 0m));
    }

    [Fact]
    public void Only_what_is_recorded_gives_a_date_and_nothing_is_guessed()
    {
        var holding = OneLot();

        var income = Figures([holding], Book([holding], [Dividend(holding, D(2026, 2, 1), 10m)]));

        (income.DividendsFrom, income.ChargesFrom, income.CoverageFrom).ShouldBe((D(2026, 2, 1), null, D(2026, 2, 1)));
    }

    [Fact]
    public void The_accounts_alone_give_the_unallocated_row_and_no_percentages()
    {
        var holding = OneLot();
        var book = Book([holding], [Dividend(holding, D(2026, 1, 1), 999m), Dividend(null, D(2026, 2, 2), 40m)], ledgerCharges: [Ledger(D(2026, 3, 3), 5m)], ledgerStarts: [new IncomeLedgerStart(Account, D(2025, 12, 1))]);

        var row = IncomeCalculator.OfAccounts(book, [Account]).ShouldNotBeNull();

        (row.Dividends, row.Charges, row.Unallocated, row.TotalReturnPct, row.NetXirr.State).ShouldBe((40m, 5m, 40m, null, XirrState.NoData));
        (row.DividendsFrom, row.ChargesFrom).ShouldBe((D(2026, 2, 2), D(2025, 12, 1)));
        IncomeCalculator.OfAccounts(book, [OtherAccount]).ShouldBeNull();
    }

    [Fact]
    public void The_view_gives_the_rate_as_a_percent_number_only_when_it_is_ok()
    {
        var holding = OneLot();
        var ok = IncomeViews.View(Figures([holding], Book([holding], [Dividend(holding, D(2026, 1, 1), 10m)])));
        var young = Holding(60m, "CCC", lots: Open(1, 50m, Valuation.AddDays(-3))).With("INE333C01011");
        var insufficient = IncomeViews.View(Figures([young], Book([young], [Dividend(young, Valuation.AddDays(-1), 1m)])));

        ok.ShouldNotBeNull();
        ok.NetXirrState.ShouldBe("ok");
        ok.NetXirr.ShouldNotBeNull().ShouldBeInRange(9m, 11m);
        ok.NetXirr.ShouldBe(Math.Round(ok.NetXirr.Value, 4));
        insufficient.ShouldNotBeNull();
        (insufficient.NetXirrState, insufficient.NetXirr).ShouldBe(("insufficient", null));
        IncomeViews.View(null).ShouldBeNull();
    }
}
