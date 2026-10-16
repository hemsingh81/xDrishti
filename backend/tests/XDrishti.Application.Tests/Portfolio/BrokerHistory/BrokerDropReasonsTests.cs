using System.Text.Json;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.BrokerHistory;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.BrokerHistory.BrokerScenario;
using static XDrishti.Application.Tests.Portfolio.PortfolioFixture;

namespace XDrishti.Application.Tests.Portfolio.BrokerHistory;

/// <summary>
/// Why rows were dropped (roadmap ISS-03): the reasons are added up over pages, halves and windows, the place of the first five is kept with the dates of the request, the page and the row index, the run view, the status and the audit text show counts only,
/// and nothing of a row's content reaches the counts, the audit text or the log.
/// </summary>
public sealed class BrokerDropReasonsTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly DateWindow Month = new(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

    private static SecretValue Token => SecretValue.TryCreate("token-for-tests", out var token, out _) ? token : throw new InvalidOperationException();

    private static async Task<BrokerHistoryStatusView> StatusAsync(BrokerScenario s) =>
        Ok(await s.Status().HandleAsync(new GetBrokerHistoryStatusQuery(s.Account.Id), CancellationToken.None));

    [Fact]
    public async Task The_reasons_of_every_page_are_added_up_and_each_location_carries_its_page()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.PageSize = 1;
        s.Dhan.Trades.AddRange(BaseTrades());
        s.Dhan.DropPage = 1;
        s.Dhan.DropReasonsPerWindow[BrokerDropReasons.BadPrice] = 1;
        s.Dhan.DropReasonsPerWindow[BrokerDropReasons.MissingId] = 2;

        var read = (await s.Reader().ReadTradesAsync(Token, Month, TestContext.Current.CancellationToken)).Value;

        read.Dropped.ShouldBe(3);
        read.Drops.ByReason.ShouldBe(new Dictionary<string, int> { [BrokerDropReasons.BadPrice] = 1, [BrokerDropReasons.MissingId] = 2 });
        read.Drops.Rows.ShouldBe(
        [
            new BrokerDroppedRow(Month.From, Month.To, 1, 0, BrokerDropReasons.BadPrice),
            new BrokerDroppedRow(Month.From, Month.To, 1, 1, BrokerDropReasons.MissingId),
            new BrokerDroppedRow(Month.From, Month.To, 1, 2, BrokerDropReasons.MissingId),
        ]);
    }

    [Fact]
    public async Task A_halved_window_locates_the_rows_by_the_half_that_returned_them_and_adds_both_halves_up()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.TradeFailure = (from, to, _) => to.DayNumber - from.DayNumber + 1 > 15 ? new BrokerFailure(BrokerFailureKind.Rejected, BrokerHistoryReader.TooLargeCode) : null;
        s.Dhan.DropReasonsPerWindow[BrokerDropReasons.BadTime] = 1;

        var read = (await s.Reader().ReadTradesAsync(Token, Month, TestContext.Current.CancellationToken)).Value;

        read.Halved.ShouldBeTrue();
        read.Drops.ByReason.ShouldBe(new Dictionary<string, int> { [BrokerDropReasons.BadTime] = 2 });
        read.Drops.Rows.ShouldBe(
        [
            new BrokerDroppedRow(Month.From, new DateOnly(2026, 9, 15), 0, 0, BrokerDropReasons.BadTime),
            new BrokerDroppedRow(new DateOnly(2026, 9, 16), Month.To, 0, 0, BrokerDropReasons.BadTime),
        ]);
    }

    [Fact]
    public async Task Rows_dropped_without_a_reason_are_counted_as_other_and_the_reasons_always_add_up()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.DroppedPerWindow = 2;
        s.Dhan.DropReasonsPerWindow[BrokerDropReasons.BadQuantity] = 1;

        var read = (await s.Reader().ReadTradesAsync(Token, Month, TestContext.Current.CancellationToken)).Value;

        read.Dropped.ShouldBe(3);
        read.Drops.Total.ShouldBe(3);
        read.Drops.ByReason.ShouldBe(new Dictionary<string, int> { [BrokerDropReasons.BadQuantity] = 1, [BrokerDropReasons.Other] = 2 });
    }

    [Fact]
    public async Task The_ledger_locates_its_rows_on_page_0_of_the_window()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.LedgerDropReasonsPerWindow[BrokerDropReasons.ZeroAmount] = 1;
        s.Dhan.LedgerDropReasonsPerWindow[BrokerDropReasons.BadDate] = 1;

        var read = (await s.Reader().ReadLedgerAsync(Token, Month, TestContext.Current.CancellationToken)).Value;

        read.Drops.ByReason.ShouldBe(new Dictionary<string, int> { [BrokerDropReasons.BadDate] = 1, [BrokerDropReasons.ZeroAmount] = 1 });
        read.Drops.Rows.ShouldBe([new BrokerDroppedRow(Month.From, Month.To, 0, 0, BrokerDropReasons.BadDate), new BrokerDroppedRow(Month.From, Month.To, 0, 1, BrokerDropReasons.ZeroAmount)]);
    }

    [Fact]
    public async Task A_trade_run_adds_the_reasons_up_over_its_windows_and_keeps_the_place_of_the_first_five_only()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Trades.AddRange(BaseTrades());
        s.Dhan.DropReasonsPerWindow[BrokerDropReasons.BadPrice] = 1;
        s.Dhan.DropReasonsPerWindow[BrokerDropReasons.MissingId] = 2;

        var run = (await s.SyncOkAsync()).Trades;

        run.Counts.WindowsDone.ShouldBe(2);
        run.Counts.Dropped.ShouldBe(6);
        run.Counts.DroppedByReason.ShouldBe(new Dictionary<string, int> { [BrokerDropReasons.BadPrice] = 2, [BrokerDropReasons.MissingId] = 4 });
        run.Counts.DroppedByReason.Values.Sum().ShouldBe(run.Counts.Dropped);
        run.Counts.DroppedRows.Count.ShouldBe(BrokerDrops.MaxLocations);
        run.Counts.DroppedRows[0].ShouldBe(new BrokerDroppedRow(From, new DateOnly(2026, 9, 30), 0, 0, BrokerDropReasons.BadPrice));
        run.Counts.DroppedRows[^1].ShouldBe(new BrokerDroppedRow(new DateOnly(2026, 10, 1), s.Fx.Today, 0, 1, BrokerDropReasons.MissingId));
    }

    [Fact]
    public async Task A_trade_the_store_would_refuse_is_dropped_as_failed_checks()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Trades.AddRange(BaseTrades());
        s.Dhan.Trades.Add(Trade("T9", new DateOnly(2026, 9, 10), TradeSide.Buy, 5, 0m)); // a price of 0 is refused by the store's rules

        var run = (await s.SyncOkAsync()).Trades;

        run.Counts.Dropped.ShouldBe(1);
        run.Counts.DroppedByReason.ShouldBe(new Dictionary<string, int> { [BrokerDropReasons.FailedChecks] = 1 });
        run.Counts.DroppedRows.ShouldBeEmpty(); // the place is only known for rows a mapper dropped
    }

    [Fact]
    public async Task A_ledger_run_adds_the_reasons_up_and_the_audit_text_and_the_status_carry_counts_only()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Trades.AddRange(BaseTrades());
        s.Dhan.Ledger.Add(Line(new DateOnly(2026, 9, 2), "V1", "RECEIPT", "FUNDS DEPOSIT", 100m));
        s.Dhan.LedgerDropReasonsPerWindow[BrokerDropReasons.ZeroAmount] = 2;
        s.Dhan.LedgerDropReasonsPerWindow[BrokerDropReasons.BadAmount] = 1;

        var view = await s.SyncOkAsync();
        var status = await StatusAsync(s);

        view.Ledger!.Counts.DroppedByReason.ShouldBe(new Dictionary<string, int> { [BrokerDropReasons.BadAmount] = 2, [BrokerDropReasons.ZeroAmount] = 4 });
        status.Ledger.DroppedByReason.ShouldBe(view.Ledger.Counts.DroppedByReason);
        status.Ledger.DroppedRows.ShouldBe(view.Ledger.Counts.DroppedRows);
        status.Trades.DroppedByReason.ShouldBeEmpty();
        var detail = s.Fx.Events(AccountEventType.BrokerHistorySynced).Select(e => e.Detail!).Single(d => d.StartsWith("Broker ledger", StringComparison.Ordinal));
        detail.ShouldContain("6 dropped (zero_amount×4, bad_amount×2)");
    }

    [Fact]
    public async Task The_trade_audit_text_says_how_many_were_dropped_and_why()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Trades.AddRange(BaseTrades());
        s.Dhan.DropReasonsPerWindow[BrokerDropReasons.BadPrice] = 1;

        await s.SyncOkAsync();

        s.Fx.Events(AccountEventType.BrokerHistorySynced).Select(e => e.Detail!).ShouldContain(d => d.Contains("2 dropped (bad_price×2);", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_status_shows_the_reasons_of_the_latest_run_of_each_kind_and_nothing_before_any_run()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Trades.AddRange(BaseTrades());

        var before = await StatusAsync(s);
        s.Dhan.DropReasonsPerWindow[BrokerDropReasons.BadQuantity] = 3;
        var view = await s.SyncOkAsync();
        var status = await StatusAsync(s);

        before.Trades.DroppedByReason.ShouldBeEmpty();
        before.Trades.DroppedRows.ShouldBeEmpty();
        status.Trades.DroppedByReason.ShouldBe(new Dictionary<string, int> { [BrokerDropReasons.BadQuantity] = 6 });
        status.Trades.DroppedRows.ShouldBe(view.Trades.Counts.DroppedRows);
        status.Trades.DroppedRows.Count.ShouldBe(5);
    }

    [Fact]
    public async Task A_run_that_drops_nothing_has_no_reasons_and_the_mapper_version_reread_still_follows_the_dropped_count()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Trades.AddRange(BaseTrades());

        var clean = (await s.SyncOkAsync()).Trades;

        (clean.Counts.Dropped, clean.Counts.DroppedByReason.Count, clean.Counts.DroppedRows.Count).ShouldBe((0, 0, 0));
        s.Fx.Data.Health.Windows.ShouldBeEmpty();

        s.Dhan.DropReasonsPerWindow[BrokerDropReasons.BadTime] = 1;
        await s.SyncOkAsync();

        s.Fx.Data.Health.Windows.ShouldNotBeEmpty(); // a window that dropped rows is still noted for the re-read
        s.Fx.Data.Health.Windows.ShouldAllBe(w => w.Dropped == 1 && w.NeedsReread);
    }

    [Fact]
    public async Task Nothing_of_a_rows_content_reaches_the_counts_the_audit_text_or_the_log()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.IgnoreDates = true;
        s.Dhan.Trades.AddRange(BaseTrades());
        // A trade the store refuses (dated 1990) with sentinel values everywhere a row has content.
        s.Dhan.Trades.Add(new BrokerTrade("ZZID987654", "ZZORDER111", TradeSide.Buy, "ZZSYMBOLNAME", "INE999Z01017", "ZZSEC4242", 777777, 8888.77m, 3.5m, new DateOnly(1990, 1, 1), new TimeOnly(9, 0)));
        s.Dhan.DropReasonsPerWindow[BrokerDropReasons.BadPrice] = 1;
        var log = new CapturingLogger();
        var handler = new SyncBrokerHistoryHandler(s.Access(), s.Pipeline(log));

        var result = await handler.HandleAsync(new SyncBrokerHistoryCommand(s.Account.Id, false, From, Actor), CancellationToken.None);

        var view = Ok(result);
        var counts = JsonSerializer.Serialize(view.Trades.Counts, Web);
        var texts = s.Fx.Events(AccountEventType.BrokerHistorySynced).Select(e => e.Detail!).Append(counts).Concat(log.Lines).ToList();
        view.Trades.Counts.DroppedByReason.ShouldContainKey(BrokerDropReasons.FailedChecks);
        log.Lines.ShouldContain(l => l.Contains("dropped", StringComparison.Ordinal)); // the final line was captured
        string[] sentinels = ["ZZID987654", "ZZORDER111", "ZZSYMBOLNAME", "INE999Z01017", "ZZSEC4242", "777777", "8888.77", "1990"];
        foreach (var sentinel in sentinels)
        {
            texts.ShouldNotContain(t => t.Contains(sentinel, StringComparison.Ordinal), $"'{sentinel}' leaked");
        }
    }

    [Fact]
    public void The_audit_text_lists_the_three_biggest_reasons_and_keeps_its_tail_within_the_audit_limit()
    {
        // 14 reasons with the longest codes and counts, and every optional part of the line present.
        var reasons = BrokerDropReasons.All.Select((code, i) => (code, count: 99_990 - i)).ToDictionary(p => p.code, p => p.count);
        var counts = BrokerSyncCounts.None with
        {
            WindowsDone = 120,
            WindowsTotal = 120,
            Halved = 12,
            EmptyByNoData = 11,
            Stored = 123_456,
            Duplicates = 654_321,
            Enriched = 4_321,
            Skipped = 99_999,
            Dropped = reasons.Values.Sum(),
            DroppedByReason = reasons,
            Rebuilt = 321,
            NeedsReview = 123,
            DeepenedTo = new DateOnly(2019, 4, 1),
            Superseded = 5_432,
            SupersededDiffering = 987,
        };
        var run = BrokerSyncRun.Start(Guid.NewGuid(), BrokerSyncKind.Trades, BrokerSyncTrigger.Scheduled, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), "system", DateTimeOffset.UnixEpoch);

        var text = BrokerHistoryPipeline.Describe(run, counts);
        var stored = AccountEvent.Record(AccountEventType.BrokerHistorySynced, run.AccountId, text, "system", DateTimeOffset.UnixEpoch).Detail!;

        BrokerHistoryPipeline.Reasons(counts).ShouldBe(" (unreadable_row×99990, missing_id×99989, bad_id×99988, +11 more)");
        text.Length.ShouldBeLessThanOrEqualTo(AccountEvent.MaxDetailLength);
        stored.ShouldBe(text);
        stored.ShouldContain("321 holdings rebuilt, 123 need review");
        stored.ShouldContain("earlier history read back to 2019-04-01");
        stored.ShouldEndWith("5432 bill-level rows of imported reports replaced (987 groups with a different total)");
        counts.DroppedByReason.Count.ShouldBe(14); // the run counts keep every reason
    }

    /// <summary>Collects every formatted line and every structured value of the logged events.</summary>
    private sealed class CapturingLogger : ILogger<BrokerHistoryPipeline>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            Lines.Add(formatter(state, exception));
            if (state is IEnumerable<KeyValuePair<string, object?>> values)
            {
                Lines.AddRange(values.Select(v => $"{v.Key}={v.Value}"));
            }
        }
    }
}
