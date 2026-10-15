using XDrishti.Domain.Portfolio;

namespace XDrishti.Domain.Tests.Portfolio;

/// <summary>The run and the counts of the broker history sync (ADR 0025), and the broker trade.</summary>
public sealed class BrokerSyncTests
{
    private static readonly Guid Account = HoldingData.Account;

    private static readonly DateTimeOffset Now = HoldingData.T0;

    private static BrokerSyncRun Run() => BrokerSyncRun.Start(Account, BrokerSyncKind.Trades, BrokerSyncTrigger.Manual, new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 10), " owner@example.com ", Now);

    [Fact]
    public void A_run_starts_running_with_empty_counts_and_a_trimmed_requester()
    {
        var run = Run();

        (run.Status, run.IsRunning, run.StartedBy, run.FinishedAt, run.Error).ShouldBe((SyncStatus.Running, true, "owner@example.com", null, null));
        run.Counts.ShouldBe(BrokerSyncCounts.None with { Attempts = 1 });
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(0, 1)]
    [InlineData(-4, 1)]
    public void A_run_knows_which_attempt_of_the_days_occurrence_it_is(int attempt, int recorded)
    {
        var run = BrokerSyncRun.Start(Account, BrokerSyncKind.Trades, BrokerSyncTrigger.Scheduled, new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 10), "system", Now, attempt);

        run.Counts.Attempts.ShouldBe(recorded);
    }

    [Fact]
    public void The_health_fields_of_the_counts_are_stored_in_the_json_and_older_rows_without_them_read_as_none()
    {
        var counts = BrokerSyncCounts.None with
        {
            Attempts = 3,
            FailureReason = "unavailable",
            DeepenedTo = new DateOnly(2024, 1, 1),
            NotRebuilt = [new BrokerNotRebuilt("EXMPL", "quantity_mismatch", "negative_opening", Guid.Parse("00000000-0000-7000-8000-000000000001"))],
        };
        var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);

        var json = System.Text.Json.JsonSerializer.Serialize(counts, options);
        var again = System.Text.Json.JsonSerializer.Deserialize<BrokerSyncCounts>(json, options)!;
        var older = System.Text.Json.JsonSerializer.Deserialize<BrokerSyncCounts>("""{"windowsDone":2,"windowsTotal":2,"notRebuilt":[{"symbol":"EXMPL","reason":"needs_review"}]}""", options)!;

        (again.Attempts, again.FailureReason, again.DeepenedTo).ShouldBe((3, "unavailable", new DateOnly(2024, 1, 1)));
        again.NotRebuilt.ShouldBe(counts.NotRebuilt);
        (older.Attempts, older.FailureReason, older.DeepenedTo).ShouldBe((0, null, null));
        older.NotRebuilt.ShouldBe([new BrokerNotRebuilt("EXMPL", "needs_review")]);
    }

    [Fact]
    public void A_run_needs_an_account()
    {
        Should.Throw<ArgumentException>(() => BrokerSyncRun.Start(Guid.Empty, BrokerSyncKind.Ledger, BrokerSyncTrigger.First, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 2), "x", Now));
    }

    [Fact]
    public void A_run_that_was_not_attempted_is_recorded_as_skipped_at_once()
    {
        var run = BrokerSyncRun.Skipped(Account, BrokerSyncKind.Trades, BrokerSyncTrigger.Scheduled, new DateOnly(2026, 10, 10), "system:broker-history", "portfolio.token_expired", Now);

        (run.Status, run.IsRunning, run.FinishedAt, run.Error, run.FromDate, run.ToDate).ShouldBe((SyncStatus.Skipped, false, Now, "portfolio.token_expired", new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 10)));
    }

    [Fact]
    public void The_counts_of_windows_add_up_and_the_skipped_reasons_are_merged()
    {
        var counts = BrokerSyncCounts.None with { WindowsTotal = 3 };

        counts = counts.AddWindow(read: 10, stored: 6, duplicates: 2, dropped: 1, new Dictionary<string, int> { ["bse"] = 1, ["derivatives"] = 2 }, halved: false, emptyByNoData: 1);
        counts = counts.AddWindow(read: 5, stored: 5, duplicates: 0, dropped: 0, new Dictionary<string, int> { ["derivatives"] = 1 }, halved: true, emptyByNoData: 2);

        (counts.WindowsDone, counts.WindowsTotal, counts.Halved, counts.Read, counts.Stored, counts.Duplicates, counts.Dropped, counts.Skipped, counts.EmptyByNoData).ShouldBe((2, 3, 1, 15, 11, 2, 1, 4, 3));
        counts.SkippedByReason.ShouldBe(new Dictionary<string, int> { ["bse"] = 1, ["derivatives"] = 3 });
    }

    [Fact]
    public void The_no_data_count_is_stored_in_the_json_of_the_run_and_older_rows_without_it_read_as_zero()
    {
        var web = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);

        var json = System.Text.Json.JsonSerializer.Serialize(BrokerSyncCounts.None with { EmptyByNoData = 2 }, web);
        var older = System.Text.Json.JsonSerializer.Deserialize<BrokerSyncCounts>("""{"stored":4,"windowsDone":1}""", web)!;

        json.ShouldContain("\"emptyByNoData\":2");
        (older.EmptyByNoData, older.Stored).ShouldBe((0, 4));
    }

    [Fact]
    public void A_broker_trade_keeps_the_run_that_read_it_and_the_source_name_is_broker()
    {
        var run = Guid.NewGuid();

        var trade = Transaction.Create(Account, null, null, "EXMPL", "NSE", TradeSide.Sell, new DateOnly(2026, 9, 20), new TimeOnly(10, 0), 3, 130m, 0m, TradeSource.Broker, null, 1, "t:20260920:T3", "system:broker-history", Now, run);

        (trade.Source, trade.RunId, trade.BatchId, trade.Charges).ShouldBe((TradeSource.Broker, run, null, 0m));
        TradeSource.Broker.ToString().ToLowerInvariant().ShouldBe("broker");
        Transaction.Create(Account, null, null, "EXMPL", null, TradeSide.Buy, new DateOnly(2024, 3, 5), null, 1, 1m, 0m, TradeSource.Csv, null, 1, "t:1", "x", Now).RunId.ShouldBeNull();
    }
}
