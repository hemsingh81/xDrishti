using XDrishti.Domain.Paper;

namespace XDrishti.Application.Tests.Paper;

/// <summary>Spec P10-02 criteria 7, 8, 9: what the plan reads from the paper book, and when it reads nothing (capture behind, no epoch, no capital).</summary>
public sealed class PaperBookReaderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_finished_capture_gives_the_hand_book_per_account_and_position()
    {
        var f = new PaperLedgerFixture();

        var read = await f.Reader().ReadAsync(PaperLedgerFixture.Last, f.Inputs, Ct);

        var book = read.ShouldBeOfType<PaperBook>();
        var account = book.Accounts.ShouldHaveSingleItem();
        account.Equity.ShouldBe(99_989.00);
        account.FundsAvailable.ShouldBe(97_989.00);
        Math.Round(account.OpenRiskPct!.Value, 3).ShouldBe(0.100);
        Math.Round(account.DailyLossPct!.Value, 4).ShouldBe(0.1199);
        account.HeldSymbols.ShouldBe(["CCC"]);
        var position = book.Positions.ShouldHaveSingleItem();
        (position.Symbol, position.Side, position.Style, position.Sector).ShouldBe(("CCC", "BUY", "swing", "IT"));
        Math.Round(position.RiskPct, 3).ShouldBe(0.100);
        position.Baskets.ShouldBe([]);
        book.Notes.ShouldContain(PaperWording.BookNote);
    }

    [Fact]
    public async Task One_taken_trade_per_counted_ticket_however_many_accounts_were_sized_for_it()
    {
        var f = new PaperLedgerFixture();
        var second = PaperLedgerFixture.Make("1000000012", 50_000m);
        f.Active = [PaperLedgerFixture.PaperOne, second];
        f.Trades = [PaperLedgerFixture.A(), PaperLedgerFixture.A(second.Id) with { EntryId = 9 }, PaperLedgerFixture.B(), PaperLedgerFixture.C()];

        var book = (PaperBook)await f.Reader().ReadAsync(PaperLedgerFixture.Last, f.Inputs, Ct);

        book.Taken.Count.ShouldBe(2); // A once, B once; C is open
        book.Taken.Select(t => t.TradeId).ShouldBe(["ORB|AAA|BUY|2026-11-02", "ORB|BBB|SELL|2026-11-04"]);
        book.Taken[0].ClosedOn.ShouldBe(PaperLedgerFixture.Day1);
        book.Taken[0].R.ShouldBe(1.8);
        book.Accounts.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_reserve_trade_is_not_a_taken_trade()
    {
        var f = new PaperLedgerFixture();
        f.Trades = [PaperLedgerFixture.A() with { TicketGroup = "reserve" }];

        ((PaperBook)await f.Reader().ReadAsync(PaperLedgerFixture.Last, f.Inputs, Ct)).Taken.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_empty_session_with_a_done_row_is_available_with_an_empty_known_book()
    {
        var f = new PaperLedgerFixture();
        f.Trades = [];

        var book = (PaperBook)await f.Reader().ReadAsync(PaperLedgerFixture.Last, f.Inputs, Ct);

        book.Positions.ShouldBeEmpty();
        book.Taken.ShouldBeEmpty();
        book.Accounts.ShouldHaveSingleItem().OpenRiskPct.ShouldBe(0.0);
    }

    [Fact]
    public async Task A_session_without_a_finished_capture_makes_the_book_unavailable_and_names_the_date()
    {
        var f = new PaperLedgerFixture();
        f.Finished.Remove(new DateOnly(2026, 11, 4)); // a missing row, a partial row and a done row with pending 1 all look like this to the reader

        var read = await f.Reader().ReadAsync(PaperLedgerFixture.Last, f.Inputs, Ct);

        read.ShouldBeOfType<PaperBookUnavailable>().Reason.ShouldBe("paper book unavailable: capture is behind (sessions without a finished capture: 2026-11-04)");
    }

    [Fact]
    public async Task The_weekend_needs_no_capture_row()
    {
        var f = new PaperLedgerFixture();
        f.Finished.Clear();
        f.Finished.UnionWith([PaperLedgerFixture.Day1, new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 6), new DateOnly(2026, 11, 9)]);

        (await f.Reader().ReadAsync(new DateOnly(2026, 11, 9), f.Inputs, Ct)).ShouldBeOfType<PaperBook>();
    }

    [Fact]
    public async Task No_epoch_yet_is_unavailable()
    {
        var f = new PaperLedgerFixture();
        f.CurrentEpoch = null;

        (await f.Reader().ReadAsync(PaperLedgerFixture.Last, f.Inputs, Ct)).ShouldBeOfType<PaperBookUnavailable>().Reason.ShouldStartWith("paper book unavailable:");
    }

    [Fact]
    public async Task An_account_without_capital_has_unknown_limits_and_its_positions_are_not_in_the_book()
    {
        var f = new PaperLedgerFixture();
        f.Active = [PaperLedgerFixture.Make("1000000013", null)];
        f.Trades = [PaperLedgerFixture.C() with { AccountId = f.Active[0].Id }];

        var book = (PaperBook)await f.Reader().ReadAsync(PaperLedgerFixture.Last, f.Inputs, Ct);

        var account = book.Accounts.ShouldHaveSingleItem();
        (account.Equity, account.OpenRiskPct, account.FundsAvailable, account.DailyLossPct).ShouldBe((null, null, null, null));
        account.HeldSymbols.ShouldBe(["CCC"]);
        book.Positions.ShouldBeEmpty();
        book.Notes.ShouldContain(PaperWording.NoCapitalNote);
    }
}
