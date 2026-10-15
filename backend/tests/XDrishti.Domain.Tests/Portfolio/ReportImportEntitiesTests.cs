using XDrishti.Domain.Portfolio;

namespace XDrishti.Domain.Tests.Portfolio;

/// <summary>What the entities of the report imports guard (ADR 0029, P2-17): bill-level trades and their supersession, costs imported from a depository-charges sheet, cost batches and learned security names.</summary>
public sealed class ReportImportEntitiesTests
{
    private static readonly Guid Account = HoldingData.Account;

    private static readonly DateTimeOffset Now = HoldingData.T0;

    private static Transaction Trade(TradeSource source, decimal price = 23.209999m) =>
        Transaction.Create(Account, null, "INE000A01011", "EXMPL", "NSE", TradeSide.Buy, new DateOnly(2026, 4, 7), null, 650, price, 2.57m, source, null, 3, "g:abc", "owner", Now);

    [Fact]
    public void A_bill_level_price_is_kept_to_six_decimals_so_quantity_times_price_gives_the_value_back_to_the_paisa()
    {
        var trade = Trade(TradeSource.Bill, 23.2099999m);

        trade.Price.ShouldBe(23.21m);
        Math.Round(trade.Qty * Transaction.Create(Account, null, null, "X", null, TradeSide.Buy, new DateOnly(2026, 4, 7), null, 650, 15086.50m / 650, 0m, TradeSource.Bill, null, 0, "g:y", "owner", Now).Price, 2).ShouldBe(15086.50m);
        TradeRules.RoundPrice(1.2345675m).ShouldBe(1.234568m);
        TradeRules.PriceDecimals.ShouldBe(6);
    }

    [Fact]
    public void A_bill_row_is_superseded_once_and_then_carries_the_reason_and_the_run()
    {
        var trade = Trade(TradeSource.Bill);
        var run = Guid.NewGuid();

        var first = trade.Supersede(run, Now.AddHours(1));
        var second = trade.Supersede(Guid.NewGuid(), Now.AddHours(2));

        (first, second).ShouldBe((true, false));
        (trade.IsVoided, trade.VoidedBy, trade.SupersededBy, trade.VoidedAt).ShouldBe((true, Transaction.SupersededReason, run, Now.AddHours(1)));
        Transaction.SupersededReason.ShouldBe("superseded");
    }

    [Theory]
    [InlineData(TradeSource.Csv)]
    [InlineData(TradeSource.Manual)]
    [InlineData(TradeSource.Broker)]
    public void Only_a_bill_row_can_be_superseded(TradeSource source)
    {
        var trade = Trade(source);

        trade.Supersede(Guid.NewGuid(), Now).ShouldBeFalse();

        trade.IsVoided.ShouldBeFalse();
        trade.SupersededBy.ShouldBeNull();
    }

    [Fact]
    public void A_voided_bill_row_is_not_superseded_and_keeps_who_voided_it()
    {
        var trade = Trade(TradeSource.Bill);
        trade.Void("owner", Now);

        trade.Supersede(Guid.NewGuid(), Now.AddHours(1)).ShouldBeFalse();

        trade.VoidedBy.ShouldBe("owner");
        trade.SupersededBy.ShouldBeNull();
    }

    [Fact]
    public void An_imported_cost_is_a_charge_entry_with_a_batch_a_security_and_no_run()
    {
        var batch = Guid.NewGuid();

        var entry = LedgerEntry.FromImport(Account, new DateOnly(2026, 6, 4), "DP charges", "DP charges - Other Fees", 12.505m, "ine245a01021", 11, batch, "d:abc", Now);

        (entry.EntryClass, entry.Source, entry.Amount, entry.Isin, entry.Quantity, entry.BatchId, entry.RunId, entry.VoucherNo, entry.Exchange).ShouldBe((LedgerClass.Charges, LedgerSource.Import, -12.51m, "INE245A01021", 11L, batch, null, string.Empty, null));
        entry.IsVoided.ShouldBeFalse();
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(5, 0)]
    [InlineData(5, -3)]
    public void An_imported_cost_is_above_zero_and_has_a_positive_quantity_when_it_has_one(double paid, long quantity)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => LedgerEntry.FromImport(Account, new DateOnly(2026, 6, 4), "DP charges", "n", (decimal)paid, "INE245A01021", quantity, Guid.NewGuid(), "d:abc", Now));
    }

    [Fact]
    public void An_imported_cost_needs_an_account_a_batch_and_an_identity_and_may_have_no_quantity()
    {
        Should.Throw<ArgumentException>(() => LedgerEntry.FromImport(Guid.Empty, new DateOnly(2026, 6, 4), "d", "n", 1m, null, null, Guid.NewGuid(), "d:abc", Now));
        Should.Throw<ArgumentException>(() => LedgerEntry.FromImport(Account, new DateOnly(2026, 6, 4), "d", "n", 1m, null, null, Guid.Empty, "d:abc", Now));
        Should.Throw<ArgumentException>(() => LedgerEntry.FromImport(Account, new DateOnly(2026, 6, 4), "d", "n", 1m, null, null, Guid.NewGuid(), " ", Now));
        LedgerEntry.FromImport(Account, new DateOnly(2026, 6, 4), "d", "n", 1m, null, null, Guid.NewGuid(), "d:abc", Now).Quantity.ShouldBeNull();
    }

    [Fact]
    public void Only_an_imported_cost_is_voided_and_only_once()
    {
        var imported = LedgerEntry.FromImport(Account, new DateOnly(2026, 6, 4), "DP charges", "n", 12.5m, "INE245A01021", 11, Guid.NewGuid(), "d:abc", Now);
        var read = LedgerEntry.Create(Account, new DateOnly(2026, 6, 4), "V1", "DP charges", "n", "NSE", -12.5m, LedgerClass.Charges, null, "x1", Guid.NewGuid(), Now);

        var first = imported.Void("owner", Now.AddHours(1));
        var second = imported.Void("owner", Now.AddHours(2));
        var fromDhan = read.Void("owner", Now);

        (first, second, fromDhan).ShouldBe((true, false, false));
        (imported.IsVoided, imported.VoidedBy, imported.VoidedAt).ShouldBe((true, "owner", Now.AddHours(1)));
        read.IsVoided.ShouldBeFalse();
        read.Source.ShouldBe(LedgerSource.Broker);
        read.RunId.ShouldNotBeNull();
    }

    [Fact]
    public void A_cost_batch_starts_imported_with_its_counts_and_is_undone_once()
    {
        var batch = CostBatch.Start(Guid.NewGuid(), Account, "C:\\reports\\dp.xls", new string('a', 64), 4, 2, 2, 1, 42.505m, " owner ", Now);

        (batch.FileName, batch.Status, batch.Amount, batch.CreatedBy, batch.Parsed, batch.Imported, batch.Duplicates, batch.Bad).ShouldBe(("dp.xls", CostBatchStatus.Imported, 42.51m, "owner", 4, 2, 2, 1));
        batch.Undo(Now.AddHours(1)).ShouldBeTrue();
        batch.Undo(Now.AddHours(2)).ShouldBeFalse();
        (batch.Status, batch.UndoneAt).ShouldBe((CostBatchStatus.Undone, Now.AddHours(1)));
    }

    [Fact]
    public void A_cost_batch_guards_its_identity_hash_and_counts()
    {
        Should.Throw<ArgumentException>(() => CostBatch.Start(Guid.Empty, Account, "a", new string('a', 64), 0, 0, 0, 0, 0m, "o", Now));
        Should.Throw<ArgumentException>(() => CostBatch.Start(Guid.NewGuid(), Account, "a", "short", 0, 0, 0, 0, 0m, "o", Now));
        Should.Throw<ArgumentOutOfRangeException>(() => CostBatch.Start(Guid.NewGuid(), Account, "a", new string('a', 64), -1, 0, 0, 0, 0m, "o", Now));
        CostBatch.Start(Guid.NewGuid(), Account, " ", new string('A', 64), 0, 0, 0, 0, 0m, " ", Now).FileName.ShouldBe("dp-charges.xls");
    }

    [Fact]
    public void A_learned_name_keeps_its_key_isin_and_cleaned_name()
    {
        var alias = SecurityAlias.Create(Account, " nova oxides and chemicals ", " ine903c01013 ", "NOVA\tOXIDES\n& CHEMICALS", SecurityAliasSource.Demat, "owner", Now);

        (alias.NameKey, alias.Isin, alias.Name, alias.Source, alias.CreatedBy).ShouldBe(("nova oxides and chemicals", "INE903C01013", "NOVA OXIDES & CHEMICALS", SecurityAliasSource.Demat, "owner"));
    }

    [Fact]
    public void A_learned_name_needs_an_account_a_key_and_an_isin()
    {
        Should.Throw<ArgumentException>(() => SecurityAlias.Create(Guid.Empty, "k", "INE903C01013", "n", SecurityAliasSource.Dp, "o", Now));
        Should.Throw<ArgumentException>(() => SecurityAlias.Create(Account, " ", "INE903C01013", "n", SecurityAliasSource.Dp, "o", Now));
        Should.Throw<ArgumentException>(() => SecurityAlias.Create(Account, new string('k', 201), "INE903C01013", "n", SecurityAliasSource.Dp, "o", Now));
        Should.Throw<ArgumentException>(() => SecurityAlias.Create(Account, "k", " ", "n", SecurityAliasSource.Dp, "o", Now));
    }

    [Fact]
    public void The_broker_run_counts_add_up_what_was_replaced_window_by_window()
    {
        var counts = BrokerSyncCounts.None
            .AddWindow(3, 3, 0, 0, new Dictionary<string, int>(), false, superseded: 2, supersededDiffering: 1)
            .AddWindow(1, 1, 0, 0, new Dictionary<string, int>(), false, superseded: 1, supersededDiffering: 0);

        (counts.Superseded, counts.SupersededDiffering).ShouldBe((3, 1));
    }
}
