using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Portfolio.BrokerHistory;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.BrokerHistory.BrokerScenario;

namespace XDrishti.Application.Tests.Portfolio.BrokerHistory;

/// <summary>
/// Dhan's DH-907 ("incorrect parameters or no data present") is "nothing there" and also "a request I did not like", so the reader is careful with it (ADR 0025): after a page of rows it ends the paging; on the first page of a window of several
/// dates it is unclear and the window is halved once; on the first page of a single date or of a half it is an empty answer. Every (half-)window taken as empty only because of it is counted in <c>emptyByNoData</c>.
/// </summary>
public sealed class BrokerNoDataTests
{
    private static readonly DateWindow Month = new(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

    private static SecretValue Token => SecretValue.TryCreate("token-for-tests", out var token, out _) ? token : throw new InvalidOperationException();

    private readonly FakeBrokerHistory _dhan = new();

    private BrokerHistoryReader Reader => new(_dhan, _dhan);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static bool IsWhole(DateOnly from, DateOnly to) => from == Month.From && to == Month.To;

    // ---- trades ----

    [Fact]
    public async Task A_window_of_several_dates_that_answers_no_data_but_has_rows_on_its_halves_is_read_from_the_halves()
    {
        _dhan.Trades.Add(Trade("T1", new DateOnly(2026, 9, 3), TradeSide.Buy, 1, 100m));
        _dhan.Trades.Add(Trade("T2", new DateOnly(2026, 9, 20), TradeSide.Buy, 1, 100m));
        _dhan.TradeNoData = (from, to, page) => IsWhole(from, to) && page == 0;

        var read = await Reader.ReadTradesAsync(Token, Month, Ct);

        read.IsSuccess.ShouldBeTrue();
        read.Value.Trades.Select(t => t.TradeId).ShouldBe(["T1", "T2"]);
        (read.Value.Halved, read.Value.EmptyByNoData).ShouldBe((true, 0));
        _dhan.TradeCalls.Select(c => (c.From, c.To)).Distinct().ShouldBe([(Month.From, Month.To), (Month.From, new DateOnly(2026, 9, 15)), (new DateOnly(2026, 9, 16), Month.To)]);
    }

    [Fact]
    public async Task No_data_on_a_page_after_the_first_ends_the_paging_and_is_not_counted()
    {
        _dhan.PageSize = 2;
        _dhan.Trades.Add(Trade("T1", new DateOnly(2026, 9, 3), TradeSide.Buy, 1, 100m));
        _dhan.Trades.Add(Trade("T2", new DateOnly(2026, 9, 4), TradeSide.Buy, 1, 100m));
        _dhan.TradeNoData = (_, _, page) => page == 1;

        var read = await Reader.ReadTradesAsync(Token, Month, Ct);

        read.Value.Trades.Count.ShouldBe(2);
        (read.Value.Halved, read.Value.EmptyByNoData).ShouldBe((false, 0));
        _dhan.TradeCalls.Select(c => c.Page).ShouldBe([0, 1]);
    }

    [Fact]
    public async Task A_window_whose_halves_both_answer_no_data_is_empty_and_each_half_is_counted()
    {
        _dhan.TradeNoData = (_, _, page) => page == 0;

        var read = await Reader.ReadTradesAsync(Token, Month, Ct);

        read.IsSuccess.ShouldBeTrue();
        read.Value.Trades.ShouldBeEmpty();
        (read.Value.Halved, read.Value.EmptyByNoData).ShouldBe((true, 2));
        _dhan.TradeCalls.Count.ShouldBe(3); // the whole window, then its two halves - never halved again
    }

    [Fact]
    public async Task A_half_that_answers_no_data_next_to_a_half_with_rows_is_counted_once()
    {
        _dhan.Trades.Add(Trade("T2", new DateOnly(2026, 9, 20), TradeSide.Buy, 1, 100m));
        _dhan.TradeNoData = (from, to, page) => page == 0 && (IsWhole(from, to) || to == new DateOnly(2026, 9, 15));

        var read = await Reader.ReadTradesAsync(Token, Month, Ct);

        read.Value.Trades.Select(t => t.TradeId).ShouldBe(["T2"]);
        read.Value.EmptyByNoData.ShouldBe(1);
    }

    [Fact]
    public async Task A_single_day_that_answers_no_data_is_empty_and_counted()
    {
        _dhan.TradeNoData = (_, _, page) => page == 0;

        var read = await Reader.ReadTradesAsync(Token, new DateWindow(Month.From, Month.From), Ct);

        read.IsSuccess.ShouldBeTrue();
        read.Value.Trades.ShouldBeEmpty();
        (read.Value.Halved, read.Value.EmptyByNoData).ShouldBe((false, 1));
        _dhan.TradeCalls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_plain_empty_list_is_empty_and_not_counted()
    {
        var read = await Reader.ReadTradesAsync(Token, Month, Ct);

        (read.Value.Halved, read.Value.EmptyByNoData).ShouldBe((false, 0));
    }

    [Fact]
    public async Task A_refused_token_on_the_probe_is_still_a_refused_token()
    {
        _dhan.TradeNoData = (from, to, page) => IsWhole(from, to) && page == 0;
        _dhan.TradeFailure = (from, to, _) => IsWhole(from, to) ? null : new BrokerFailure(BrokerFailureKind.Unauthorized);

        var read = await Reader.ReadTradesAsync(Token, Month, Ct);

        read.Error.ShouldBe(PortfolioErrors.TokenRefused);
    }

    // ---- ledger ----

    [Fact]
    public async Task A_ledger_window_that_answers_no_data_but_has_lines_on_its_halves_is_read_from_the_halves()
    {
        _dhan.Ledger.Add(Line(new DateOnly(2026, 9, 3), "V1", "RECEIPT", "FUNDS DEPOSIT", 100m));
        _dhan.Ledger.Add(Line(new DateOnly(2026, 9, 28), "V2", "PAYBLE", "FUNDS WITHDRAWAL", -40m));
        _dhan.LedgerNoData = (from, to) => IsWhole(from, to);

        var read = await Reader.ReadLedgerAsync(Token, Month, Ct);

        read.Value.Rows.Select(r => r.VoucherNo).ShouldBe(["V1", "V2"]);
        (read.Value.Halved, read.Value.EmptyByNoData).ShouldBe((true, 0));
        _dhan.LedgerCalls.Count.ShouldBe(3);
    }

    [Fact]
    public async Task A_ledger_window_whose_halves_both_answer_no_data_is_empty_and_each_half_is_counted()
    {
        _dhan.LedgerNoData = (_, _) => true;

        var read = await Reader.ReadLedgerAsync(Token, Month, Ct);

        read.Value.Rows.ShouldBeEmpty();
        (read.Value.Halved, read.Value.EmptyByNoData).ShouldBe((true, 2));
        _dhan.LedgerCalls.Count.ShouldBe(3);
    }

    [Fact]
    public async Task A_single_day_of_the_ledger_that_answers_no_data_is_empty_and_counted()
    {
        _dhan.LedgerNoData = (_, _) => true;

        var read = await Reader.ReadLedgerAsync(Token, new DateWindow(Month.From, Month.From), Ct);

        (read.Value.Halved, read.Value.EmptyByNoData).ShouldBe((false, 1));
        _dhan.LedgerCalls.Count.ShouldBe(1);
    }

    // ---- in a run ----

    [Fact]
    public async Task A_run_adds_the_no_data_windows_up_in_its_counts_for_both_kinds()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.TradeNoData = (_, _, page) => page == 0;
        s.Dhan.LedgerNoData = (_, _) => true;

        var view = await s.SyncOkAsync();

        // two windows (1-30 Sep, 1-10 Oct), each halved once, both halves empty
        (view.Trades.Status, view.Trades.Counts.Halved, view.Trades.Counts.EmptyByNoData).ShouldBe(("succeeded", 2, 4));
        (view.Ledger!.Status, view.Ledger.Counts.Halved, view.Ledger.Counts.EmptyByNoData).ShouldBe(("succeeded", 2, 4));
        s.Fx.Events(AccountEventType.BrokerHistorySynced).Select(e => e.Detail).ShouldAllBe(d => d!.Contains("4 taken as empty on Dhan's no-data code", StringComparison.Ordinal));
    }
}
