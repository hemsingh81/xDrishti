using XDrishti.Application.Portfolio.Dividends;
using XDrishti.Application.Tests.Portfolio.Trades;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.Income.IncomeFixtures;
using static XDrishti.Application.Tests.Portfolio.PortfolioFixture;

namespace XDrishti.Application.Tests.Portfolio.Income;

/// <summary>Adding, voiding and listing the dividends the owner enters (ADR 0026, P2-14 criterion 1). Clock: Saturday 2026-10-10, India; the account holds EXMPL (ISIN INE000A01011).</summary>
public sealed class DividendTests
{
    private static readonly DateOnly PaidOn = D(2026, 6, 15);

    private static DividendInput ForHolding(TradeScenario s, decimal? amount = 500m, DateOnly? date = null, bool? confirm = null, decimal? perShare = null, string? note = null) =>
        new(null, s.Holding.Id, date ?? PaidOn, amount, perShare, note, confirm);

    private static DividendInput Unallocated(TradeScenario s, decimal? amount = 75m, DateOnly? date = null, bool? confirm = null) =>
        new(s.Account.Id, null, date ?? PaidOn, amount, null, null, confirm);

    private static Task<Result<DividendView>> AddAsync(TradeScenario s, DividendInput input) =>
        s.Fx.AddDividend().HandleAsync(new AddDividendCommand(input, Actor), CancellationToken.None).AsTask();

    private static Task<Result<DividendView>> VoidAsync(TradeScenario s, Guid id) =>
        s.Fx.VoidDividend().HandleAsync(new VoidDividendCommand(id, Actor), CancellationToken.None).AsTask();

    [Fact]
    public async Task A_dividend_for_a_holding_is_stored_with_the_holdings_security_and_audited_without_the_amount()
    {
        var s = await TradeScenario.StartAsync();

        var view = Ok(await AddAsync(s, ForHolding(s, 1234.567m, perShare: 12.34567m, note: "  final\tdividend ")));

        (view.AccountId, view.HoldingId, view.Symbol, view.Isin, view.PayDate, view.Amount, view.PerShare, view.Note, view.Source, view.Voided).ShouldBe(
            (s.Account.Id, s.Holding.Id, "EXMPL", TradeScenario.Isin, PaidOn, 1234.57m, 12.3457m, "final dividend", "manual", false));
        s.Fx.Dividends.Dividends.ShouldHaveSingleItem().CreatedBy.ShouldBe(Actor);
        var audit = s.Fx.Events(AccountEventType.DividendAdded).ShouldHaveSingleItem();
        audit.Detail.ShouldBe($"Dividend {view.Id} added");
        audit.Detail!.ShouldNotContain("1234");
        audit.AccountId.ShouldBe(s.Account.Id);
    }

    [Fact]
    public async Task A_dividend_for_an_account_alone_is_unallocated()
    {
        var s = await TradeScenario.StartAsync();

        var view = Ok(await AddAsync(s, Unallocated(s)));

        (view.AccountId, view.HoldingId, view.Symbol, view.Isin, view.Amount).ShouldBe((s.Account.Id, null, null, null, 75m));
    }

    [Fact]
    public async Task A_holding_and_an_account_that_do_not_match_are_refused()
    {
        var s = await TradeScenario.StartAsync();

        var result = await AddAsync(s, new DividendInput(Guid.NewGuid(), s.Holding.Id, PaidOn, 10m, null, null, null));

        FieldsOf(result).ShouldBe(["accountId"]);
        s.Fx.Dividends.Dividends.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_holding_must_exist_and_the_account_too()
    {
        var s = await TradeScenario.StartAsync();

        FieldsOf(await AddAsync(s, new DividendInput(null, Guid.NewGuid(), PaidOn, 10m, null, null, null))).ShouldBe(["holdingId"]);
        FieldsOf(await AddAsync(s, new DividendInput(null, null, PaidOn, 10m, null, null, null))).ShouldBe(["accountId"]);
        (await AddAsync(s, new DividendInput(Guid.NewGuid(), null, PaidOn, 10m, null, null, null))).Error.Code.ShouldBe(PortfolioErrors.NotFoundCode);
    }

    [Theory]
    [InlineData("2026-10-11", 10, "payDate")]
    [InlineData("1989-12-31", 10, "payDate")]
    [InlineData("2026-06-15", 0, "amount")]
    [InlineData("2026-06-15", -5, "amount")]
    [InlineData("2026-06-15", 0.004, "amount")]
    [InlineData("2026-06-15", 2000000000, "amount")]
    public async Task A_dividend_is_validated(string date, double amount, string field)
    {
        var s = await TradeScenario.StartAsync();

        var result = await AddAsync(s, ForHolding(s, (decimal)amount, DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture)));

        result.Error.Code.ShouldBe("portfolio.invalid");
        FieldsOf(result).ShouldBe([field]);
        s.Fx.Dividends.Dividends.ShouldBeEmpty();
    }

    [Fact]
    public async Task Today_is_allowed_and_missing_values_are_all_named()
    {
        var s = await TradeScenario.StartAsync();

        Ok(await AddAsync(s, ForHolding(s, date: D(2026, 10, 10))));
        FieldsOf(await AddAsync(s, new DividendInput(null, s.Holding.Id, null, null, null, null, null))).ShouldBe(["payDate", "amount"]);
    }

    [Fact]
    public async Task The_per_share_amount_and_the_note_are_checked_when_given()
    {
        var s = await TradeScenario.StartAsync();

        FieldsOf(await AddAsync(s, ForHolding(s, perShare: 0m))).ShouldBe(["perShare"]);
        FieldsOf(await AddAsync(s, ForHolding(s, note: new string('x', 201)))).ShouldBe(["note"]);
        Ok(await AddAsync(s, ForHolding(s, note: new string('x', 200)))).Note!.Length.ShouldBe(200);
        Ok(await AddAsync(s, ForHolding(s, amount: 11m, note: "   "))).Note.ShouldBeNull();
    }

    [Fact]
    public async Task The_same_dividend_twice_asks_for_a_confirmation_and_then_both_are_kept()
    {
        var s = await TradeScenario.StartAsync();
        Ok(await AddAsync(s, ForHolding(s)));

        var asked = await AddAsync(s, ForHolding(s));
        var confirmed = Ok(await AddAsync(s, ForHolding(s, confirm: true)));

        (asked.Error.Code, asked.Error.Kind).ShouldBe(("portfolio.possible_duplicate", ErrorKind.Conflict));
        s.Fx.Dividends.Dividends.Count.ShouldBe(2);
        confirmed.Id.ShouldNotBe(s.Fx.Dividends.Dividends[0].Id);
        s.Fx.Events(AccountEventType.DividendAdded).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Another_amount_date_or_holding_is_not_a_duplicate_and_neither_is_a_voided_one()
    {
        var s = await TradeScenario.StartAsync();
        var first = Ok(await AddAsync(s, ForHolding(s)));

        Ok(await AddAsync(s, ForHolding(s, 501m)));
        Ok(await AddAsync(s, ForHolding(s, date: PaidOn.AddDays(1))));
        Ok(await AddAsync(s, Unallocated(s, 500m))); // the same amount and date, but for no holding
        Ok(await VoidAsync(s, first.Id));
        Ok(await AddAsync(s, ForHolding(s)));

        s.Fx.Dividends.Dividends.Count(d => !d.IsVoided).ShouldBe(4);
    }

    [Fact]
    public async Task Two_unallocated_dividends_of_the_same_day_and_amount_also_ask()
    {
        var s = await TradeScenario.StartAsync();
        Ok(await AddAsync(s, Unallocated(s)));

        (await AddAsync(s, Unallocated(s))).Error.Code.ShouldBe(PortfolioErrors.PossibleDuplicateCode);
    }

    [Fact]
    public async Task Voiding_a_dividend_stops_it_counting_keeps_it_and_is_audited_once_without_the_amount()
    {
        var s = await TradeScenario.StartAsync();
        var added = Ok(await AddAsync(s, ForHolding(s, 98765.43m)));

        var voided = Ok(await VoidAsync(s, added.Id));
        var again = Ok(await VoidAsync(s, added.Id));

        (voided.Voided, voided.VoidedBy, again.Voided).ShouldBe((true, Actor, true));
        voided.VoidedAt.ShouldNotBeNull();
        s.Fx.Dividends.Dividends.ShouldHaveSingleItem().IsVoided.ShouldBeTrue();
        var audit = s.Fx.Events(AccountEventType.DividendVoided).ShouldHaveSingleItem();
        audit.Detail.ShouldBe($"Dividend {added.Id} voided");
        audit.Detail!.ShouldNotContain("98765");
    }

    [Fact]
    public async Task Voiding_what_does_not_exist_is_not_found()
    {
        var s = await TradeScenario.StartAsync();

        (await VoidAsync(s, Guid.NewGuid())).Error.Code.ShouldBe(PortfolioErrors.NotFoundCode);
    }

    [Fact]
    public async Task A_holdings_dividends_are_listed_newest_first_with_voided_ones_flagged_and_paged()
    {
        var s = await TradeScenario.StartAsync();
        var oldest = Ok(await AddAsync(s, ForHolding(s, 1m, D(2025, 1, 1))));
        Ok(await AddAsync(s, ForHolding(s, 2m, D(2026, 1, 1))));
        var newest = Ok(await AddAsync(s, ForHolding(s, 3m, D(2026, 9, 1))));
        Ok(await AddAsync(s, Unallocated(s, 4m, D(2026, 9, 9)))); // not this holding's
        Ok(await VoidAsync(s, oldest.Id));

        var all = Ok(await s.Fx.ListHoldingDividends().HandleAsync(new ListHoldingDividendsQuery(s.Holding.Id, null, null), CancellationToken.None));
        var second = Ok(await s.Fx.ListHoldingDividends().HandleAsync(new ListHoldingDividendsQuery(s.Holding.Id, 2, 2), CancellationToken.None));

        all.Items.Select(d => (d.Amount, d.Voided)).ShouldBe([(3m, false), (2m, false), (1m, true)]);
        (all.Page, all.PageSize, all.Total, all.Items[0].Id).ShouldBe((1, 50, 3, newest.Id));
        second.Items.Select(d => d.Amount).ShouldBe([1m]);
        second.Total.ShouldBe(3);
    }

    [Fact]
    public async Task An_accounts_dividends_include_every_holdings_and_the_unallocated_ones()
    {
        var s = await TradeScenario.StartAsync();
        Ok(await AddAsync(s, ForHolding(s, 1m, D(2026, 1, 1))));
        Ok(await AddAsync(s, Unallocated(s, 4m, D(2026, 9, 9))));

        var page = Ok(await s.Fx.ListAccountDividends().HandleAsync(new ListAccountDividendsQuery(s.Account.Id, null, null), CancellationToken.None));

        page.Items.Select(d => (d.Amount, d.HoldingId)).ShouldBe([(4m, (Guid?)null), (1m, s.Holding.Id)]);
    }

    [Fact]
    public async Task The_lists_check_the_paging_and_the_holding_or_account()
    {
        var s = await TradeScenario.StartAsync();

        FieldsOf(await s.Fx.ListHoldingDividends().HandleAsync(new ListHoldingDividendsQuery(s.Holding.Id, 0, 201), CancellationToken.None)).ShouldBe(["page", "pageSize"]);
        (await s.Fx.ListHoldingDividends().HandleAsync(new ListHoldingDividendsQuery(Guid.NewGuid(), null, null), CancellationToken.None)).Error.Code.ShouldBe(PortfolioErrors.NotFoundCode);
        (await s.Fx.ListAccountDividends().HandleAsync(new ListAccountDividendsQuery(Guid.NewGuid(), null, null), CancellationToken.None)).Error.Code.ShouldBe(AccountErrors.NotFoundCode);
        FieldsOf(await s.Fx.ListAccountDividends().HandleAsync(new ListAccountDividendsQuery(s.Account.Id, 1, 0), CancellationToken.None)).ShouldBe(["pageSize"]);
    }
}
