using XDrishti.Application.Portfolio.Dividends;
using XDrishti.Application.Tests.Portfolio.Trades;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.Income.IncomeFixtures;
using static XDrishti.Application.Tests.Portfolio.PortfolioFixture;

namespace XDrishti.Application.Tests.Portfolio.Income;

/// <summary>Suggested dividends from ledger entries of class dividend (ADR 0026, P2-14 criterion 2): nothing is created without the owner.</summary>
public sealed class DividendSuggestionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);

    private static LedgerEntry Entry(Guid account, string voucher, LedgerClass entryClass, decimal amount, DateOnly? date = null) =>
        LedgerEntry.Create(account, date ?? D(2026, 9, 12), voucher, "RECEIPT", "DIVIDEND CREDIT EXMPL <b>FINAL</b> DIV", "NSE-CAPITAL", amount, entryClass, null, "x-" + voucher, Guid.NewGuid(), Now);

    private static LedgerEntry Seed(TradeScenario s, string voucher = "V1", LedgerClass entryClass = LedgerClass.Dividend, decimal amount = 1500m, Guid? account = null, DateOnly? date = null)
    {
        var entry = Entry(account ?? s.Account.Id, voucher, entryClass, amount, date);
        s.Fx.BrokerData.Entries.Add(entry);
        return entry;
    }

    private static Task<XDrishti.Domain.Common.Result<DividendSuggestionPage>> ListAsync(TradeScenario s) =>
        s.Fx.ListDividendSuggestions().HandleAsync(new ListDividendSuggestionsQuery(s.Account.Id, null, null), CancellationToken.None).AsTask();

    private static Task<XDrishti.Domain.Common.Result<DividendView>> AcceptAsync(TradeScenario s, Guid entry, AcceptSuggestionInput? input = null, Guid? account = null) =>
        s.Fx.AcceptDividendSuggestion().HandleAsync(new AcceptDividendSuggestionCommand(account ?? s.Account.Id, entry, input, Actor), CancellationToken.None).AsTask();

    private static Task<XDrishti.Domain.Common.Result<SuggestionDecisionView>> DismissAsync(TradeScenario s, Guid entry, Guid? account = null) =>
        s.Fx.DismissDividendSuggestion().HandleAsync(new DismissDividendSuggestionCommand(account ?? s.Account.Id, entry, Actor), CancellationToken.None).AsTask();

    [Fact]
    public async Task Only_undecided_dividend_entries_of_the_account_are_suggested_newest_first_and_nothing_is_created()
    {
        var s = await TradeScenario.StartAsync();
        var older = Seed(s, "V1", date: D(2026, 8, 1));
        var newer = Seed(s, "V2", date: D(2026, 9, 1));
        Seed(s, "V3", LedgerClass.Charges, -10m);
        Seed(s, "V4", LedgerClass.FundsIn, 100m);
        Seed(s, "V5", account: Guid.NewGuid());

        var page = Ok(await ListAsync(s));

        page.Items.Select(e => e.Id).ShouldBe([newer.Id, older.Id]);
        (page.Total, page.Items[0].Class, page.Items[0].Amount).ShouldBe((2, "dividend", 1500m));
        s.Fx.Dividends.Dividends.ShouldBeEmpty();
        s.Fx.Dividends.Decisions.ShouldBeEmpty();
    }

    [Fact]
    public async Task Accepting_creates_a_dividend_of_the_entrys_amount_and_date_and_the_entry_is_not_offered_again()
    {
        var s = await TradeScenario.StartAsync();
        var entry = Seed(s, amount: 1500.5m, date: D(2026, 9, 12));

        var view = Ok(await AcceptAsync(s, entry.Id, new AcceptSuggestionInput(s.Holding.Id, " from the ledger ")));

        (view.AccountId, view.HoldingId, view.Symbol, view.PayDate, view.Amount, view.Note, view.Source).ShouldBe((s.Account.Id, s.Holding.Id, "EXMPL", D(2026, 9, 12), 1500.5m, "from the ledger", "manual"));
        var decision = s.Fx.Dividends.Decisions.ShouldHaveSingleItem();
        (decision.Id, decision.State, decision.DividendId, decision.AccountId).ShouldBe((entry.Id, DividendSuggestionState.Accepted, view.Id, s.Account.Id));
        Ok(await ListAsync(s)).Items.ShouldBeEmpty();
        var audit = s.Fx.Events(AccountEventType.DividendSuggestionAccepted).ShouldHaveSingleItem();
        audit.Detail.ShouldBe($"Suggested dividend {entry.Id} accepted as dividend {view.Id}");
        audit.Detail!.ShouldNotContain("1500");
    }

    [Theory]
    [InlineData(1_000_000_000.01)]
    [InlineData(2_500_000_000)]
    public async Task An_amount_a_dividend_cannot_have_is_a_field_error_not_a_crash_and_is_not_offered(double amount)
    {
        var s = await TradeScenario.StartAsync();
        var entry = Seed(s, amount: (decimal)amount);

        var accepted = await AcceptAsync(s, entry.Id);
        var offered = Ok(await ListAsync(s));

        FieldsOf(accepted).ShouldBe(["amount"]);
        offered.Items.ShouldBeEmpty();
        s.Fx.Dividends.Dividends.ShouldBeEmpty();
        s.Fx.Dividends.Decisions.ShouldBeEmpty();
        Ok(await DismissAsync(s, entry.Id)).ShouldNotBeNull(); // the owner can still say no to it
    }

    [Fact]
    public async Task The_highest_amount_and_a_paisa_can_be_accepted()
    {
        var s = await TradeScenario.StartAsync();
        var high = Seed(s, "V1", amount: DividendRules.MaxAmount);
        var low = Seed(s, "V2", amount: 0.01m);

        Ok(await AcceptAsync(s, high.Id)).Amount.ShouldBe(DividendRules.MaxAmount);
        Ok(await AcceptAsync(s, low.Id)).Amount.ShouldBe(0.01m);
    }

    [Fact]
    public async Task Accepting_without_a_holding_gives_an_unallocated_dividend()
    {
        var s = await TradeScenario.StartAsync();
        var entry = Seed(s);

        var view = Ok(await AcceptAsync(s, entry.Id));

        (view.HoldingId, view.Symbol).ShouldBe((null, null));
    }

    [Fact]
    public async Task The_holding_chosen_must_exist_and_belong_to_the_account_and_a_long_note_is_refused()
    {
        var s = await TradeScenario.StartAsync();
        var entry = Seed(s);

        FieldsOf(await AcceptAsync(s, entry.Id, new AcceptSuggestionInput(Guid.NewGuid(), null))).ShouldBe(["holdingId"]);
        FieldsOf(await AcceptAsync(s, entry.Id, new AcceptSuggestionInput(null, new string('n', 201)))).ShouldBe(["note"]);
        s.Fx.Dividends.Decisions.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_decision_is_final_for_accept_and_dismiss_alike()
    {
        var s = await TradeScenario.StartAsync();
        var accepted = Seed(s, "V1");
        var dismissed = Seed(s, "V2");
        Ok(await AcceptAsync(s, accepted.Id));
        Ok(await DismissAsync(s, dismissed.Id));

        (await AcceptAsync(s, accepted.Id)).Error.Code.ShouldBe(PortfolioErrors.ConflictCode);
        (await DismissAsync(s, accepted.Id)).Error.Code.ShouldBe(PortfolioErrors.ConflictCode);
        (await AcceptAsync(s, dismissed.Id)).Error.Code.ShouldBe(PortfolioErrors.ConflictCode);
        (await DismissAsync(s, dismissed.Id)).Error.Code.ShouldBe(PortfolioErrors.ConflictCode);
        s.Fx.Dividends.Dividends.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Voiding_the_dividend_made_from_a_suggestion_does_not_bring_the_suggestion_back()
    {
        var s = await TradeScenario.StartAsync();
        var entry = Seed(s);
        var view = Ok(await AcceptAsync(s, entry.Id));

        Ok(await s.Fx.VoidDividend().HandleAsync(new VoidDividendCommand(view.Id, Actor), CancellationToken.None));

        Ok(await ListAsync(s)).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dismissing_hides_the_entry_and_creates_no_dividend()
    {
        var s = await TradeScenario.StartAsync();
        var entry = Seed(s);

        var decision = Ok(await DismissAsync(s, entry.Id));

        (decision.LedgerEntryId, decision.State, decision.DividendId).ShouldBe((entry.Id, "dismissed", null));
        Ok(await ListAsync(s)).Items.ShouldBeEmpty();
        s.Fx.Dividends.Dividends.ShouldBeEmpty();
        s.Fx.Events(AccountEventType.DividendSuggestionDismissed).ShouldHaveSingleItem().Detail.ShouldBe($"Suggested dividend {entry.Id} dismissed");
    }

    [Fact]
    public async Task What_is_not_a_dividend_entry_of_the_account_is_not_found()
    {
        var s = await TradeScenario.StartAsync();
        var charge = Seed(s, "V1", LedgerClass.Charges, -10m);
        var foreign = Seed(s, "V2", account: Guid.NewGuid());
        var debit = Seed(s, "V3", amount: -5m);

        foreach (var id in new[] { charge.Id, foreign.Id, debit.Id, Guid.NewGuid() })
        {
            (await AcceptAsync(s, id)).Error.Code.ShouldBe(PortfolioErrors.NotFoundCode);
            (await DismissAsync(s, id)).Error.Code.ShouldBe(PortfolioErrors.NotFoundCode);
        }

        (await AcceptAsync(s, charge.Id, account: Guid.NewGuid())).Error.Code.ShouldBe(AccountErrors.NotFoundCode);
        (await ListAsync(s)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_decision_made_meanwhile_leaves_nothing_behind()
    {
        var s = await TradeScenario.StartAsync();
        var entry = Seed(s);
        s.Fx.Data.CollidingSaves = 1;

        var result = await AcceptAsync(s, entry.Id);

        result.Error.Code.ShouldBe(PortfolioErrors.ConflictCode);
        s.Fx.Dividends.Dividends.ShouldBeEmpty();
        s.Fx.Dividends.Decisions.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_suggestions_of_an_unknown_account_and_bad_paging_are_refused()
    {
        var s = await TradeScenario.StartAsync();

        (await s.Fx.ListDividendSuggestions().HandleAsync(new ListDividendSuggestionsQuery(Guid.NewGuid(), null, null), CancellationToken.None)).Error.Code.ShouldBe(AccountErrors.NotFoundCode);
        FieldsOf(await s.Fx.ListDividendSuggestions().HandleAsync(new ListDividendSuggestionsQuery(s.Account.Id, 0, null), CancellationToken.None)).ShouldBe(["page"]);
    }
}
