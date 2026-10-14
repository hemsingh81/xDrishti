using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Portfolio.BrokerHistory;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.BrokerHistory.BrokerScenario;

namespace XDrishti.Application.Tests.Portfolio.BrokerHistory;

/// <summary>Reading one window of Dhan's trade history or ledger (ADR 0025): paging, the page cap, a window refused as too large and halved once, and every failure that must never become "no data".</summary>
public sealed class BrokerHistoryReaderTests
{
    private static readonly DateWindow Month = new(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

    private static SecretValue Token => SecretValue.TryCreate("token-for-tests", out var token, out _) ? token : throw new InvalidOperationException();

    private readonly FakeBrokerHistory _dhan = new();

    private BrokerHistoryReader Reader => new(_dhan, _dhan);

    private void AddTrades(int count)
    {
        for (var i = 1; i <= count; i++)
        {
            _dhan.Trades.Add(Trade("T" + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture), new DateOnly(2026, 9, 1 + (i % 28)), TradeSide.Buy, 1, 100m));
        }
    }

    private void AddTradesOn(int count, DateOnly date, string prefix)
    {
        for (var i = 0; i < count; i++)
        {
            _dhan.Trades.Add(Trade(prefix + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture), date, TradeSide.Buy, 1, 100m));
        }
    }

    [Fact]
    public async Task Pages_are_read_from_zero_until_the_first_empty_one()
    {
        _dhan.PageSize = 10;
        AddTrades(25);

        var read = await Reader.ReadTradesAsync(Token, Month, TestContext.Current.CancellationToken);

        read.Value.Trades.Count.ShouldBe(25);
        _dhan.TradeCalls.Select(c => c.Page).ShouldBe([0, 1, 2, 3]);
        read.Value.Halved.ShouldBeFalse();
    }

    [Fact]
    public async Task An_empty_first_page_is_an_empty_window()
    {
        var read = await Reader.ReadTradesAsync(Token, Month, TestContext.Current.CancellationToken);

        read.IsSuccess.ShouldBeTrue();
        read.Value.Trades.ShouldBeEmpty();
        read.Value.Rows.ShouldBe(0);
        _dhan.TradeCalls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_counts_of_every_page_are_added_up()
    {
        _dhan.PageSize = 10;
        _dhan.SkippedPerWindow["derivatives"] = 3;
        _dhan.DroppedPerWindow = 2;
        AddTrades(4);

        var read = (await Reader.ReadTradesAsync(Token, Month, TestContext.Current.CancellationToken)).Value;

        (read.Trades.Count, read.Rows, read.Dropped).ShouldBe((4, 9, 2));
        read.Skipped.ShouldBe(new Dictionary<string, int> { ["derivatives"] = 3 });
    }

    [Fact]
    public async Task A_window_that_reaches_the_page_cap_is_halved_once_and_each_half_is_read()
    {
        _dhan.PageSize = 1;
        AddTradesOn(150, new DateOnly(2026, 9, 1), "A");
        AddTradesOn(150, new DateOnly(2026, 9, 20), "B");

        var read = await Reader.ReadTradesAsync(Token, Month, TestContext.Current.CancellationToken);

        read.IsSuccess.ShouldBeTrue();
        read.Value.Halved.ShouldBeTrue();
        read.Value.Trades.Count.ShouldBe(300);
        _dhan.TradeCalls.Count(c => c.From == Month.From && c.To == Month.To).ShouldBe(BrokerHistoryReader.MaxPages);
    }

    [Fact]
    public async Task A_half_that_reaches_the_page_cap_too_fails_the_read_and_nothing_of_the_window_is_used()
    {
        _dhan.PageSize = 1;
        AddTradesOn(300, new DateOnly(2026, 9, 1), "A");

        var read = await Reader.ReadTradesAsync(Token, Month, TestContext.Current.CancellationToken);

        read.IsFailure.ShouldBeTrue();
        read.Error.Code.ShouldBe(PortfolioErrors.HistoryTooLargeCode);
    }

    [Fact]
    public async Task A_window_that_dhan_refuses_as_too_wide_is_halved_once_and_the_halves_are_read()
    {
        _dhan.PageSize = 10;
        _dhan.Trades.Add(Trade("T1", new DateOnly(2026, 9, 3), TradeSide.Buy, 1, 100m));
        _dhan.Trades.Add(Trade("T2", new DateOnly(2026, 9, 20), TradeSide.Buy, 1, 100m));
        _dhan.TradeFailure = (from, to, _) => to.DayNumber - from.DayNumber + 1 > 15 ? new BrokerFailure(BrokerFailureKind.Rejected, BrokerHistoryReader.TooLargeCode) : null;

        var read = await Reader.ReadTradesAsync(Token, Month, TestContext.Current.CancellationToken);

        read.IsSuccess.ShouldBeTrue();
        read.Value.Halved.ShouldBeTrue();
        read.Value.Trades.Select(t => t.TradeId).ShouldBe(["T1", "T2"]);
        _dhan.TradeCalls.Select(c => (c.From, c.To)).Distinct().ShouldBe(
            [(Month.From, Month.To), (Month.From, new DateOnly(2026, 9, 15)), (new DateOnly(2026, 9, 16), Month.To)]);
    }

    [Fact]
    public async Task A_half_that_is_too_wide_too_fails_the_read_and_is_not_halved_again()
    {
        _dhan.TradeFailure = (_, _, _) => new BrokerFailure(BrokerFailureKind.Rejected, BrokerHistoryReader.TooLargeCode);

        var read = await Reader.ReadTradesAsync(Token, Month, TestContext.Current.CancellationToken);

        read.Error.Code.ShouldBe(PortfolioErrors.HistoryTooLargeCode);
        _dhan.TradeCalls.Select(c => (c.From, c.To)).Distinct().Count().ShouldBe(2); // the whole window and its first half
    }

    [Fact]
    public async Task A_single_day_that_is_refused_is_a_broker_error_and_is_not_halved()
    {
        _dhan.TradeFailure = (_, _, _) => new BrokerFailure(BrokerFailureKind.Rejected, BrokerHistoryReader.TooLargeCode);

        var read = await Reader.ReadTradesAsync(Token, new DateWindow(Month.From, Month.From), TestContext.Current.CancellationToken);

        read.Error.Code.ShouldBe("accounts.broker_error");
        _dhan.TradeCalls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_refused_token_is_not_connected_and_is_never_no_data()
    {
        _dhan.TradeFailure = (_, _, _) => new BrokerFailure(BrokerFailureKind.Unauthorized);

        var read = await Reader.ReadTradesAsync(Token, Month, TestContext.Current.CancellationToken);

        read.Error.ShouldBe(PortfolioErrors.TokenRefused);
    }

    [Theory]
    [InlineData(BrokerFailureKind.Rejected, null)] // 429 without a code
    [InlineData(BrokerFailureKind.Rejected, "DH-904")] // rate limit
    [InlineData(BrokerFailureKind.Unavailable, null)] // 5xx, timeout
    public async Task A_rate_limit_or_an_outage_fails_the_read_with_the_broker_error_and_does_not_halve(BrokerFailureKind kind, string? code)
    {
        _dhan.TradeFailure = (_, _, _) => new BrokerFailure(kind, code);

        var read = await Reader.ReadTradesAsync(Token, Month, TestContext.Current.CancellationToken);

        read.Error.Code.ShouldBe("accounts.broker_error");
        _dhan.TradeCalls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_failure_on_a_later_page_fails_the_whole_window()
    {
        _dhan.PageSize = 2;
        AddTrades(5);
        _dhan.TradeFailure = (_, _, page) => page == 2 ? new BrokerFailure(BrokerFailureKind.Unavailable) : null;

        var read = await Reader.ReadTradesAsync(Token, Month, TestContext.Current.CancellationToken);

        read.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task The_ledger_of_a_window_is_one_call_and_a_too_wide_window_is_halved_once()
    {
        _dhan.Ledger.Add(Line(new DateOnly(2026, 9, 3), "V1", "RECEIPT", "FUNDS DEPOSIT", 100m));
        _dhan.Ledger.Add(Line(new DateOnly(2026, 9, 28), "V2", "PAYBLE", "FUNDS WITHDRAWAL", -40m));
        _dhan.LedgerFailure = (from, to) => to.DayNumber - from.DayNumber + 1 > 15 ? new BrokerFailure(BrokerFailureKind.Rejected, BrokerHistoryReader.TooLargeCode) : null;

        var read = await Reader.ReadLedgerAsync(Token, Month, TestContext.Current.CancellationToken);

        read.IsSuccess.ShouldBeTrue();
        read.Value.Halved.ShouldBeTrue();
        read.Value.Rows.Select(r => r.VoucherNo).ShouldBe(["V1", "V2"]);
        _dhan.LedgerCalls.Count.ShouldBe(3);
    }

    [Fact]
    public async Task A_refused_token_fails_the_ledger_read_too()
    {
        _dhan.LedgerFailure = (_, _) => new BrokerFailure(BrokerFailureKind.Unauthorized);

        (await Reader.ReadLedgerAsync(Token, Month, TestContext.Current.CancellationToken)).Error.ShouldBe(PortfolioErrors.TokenRefused);
    }
}
