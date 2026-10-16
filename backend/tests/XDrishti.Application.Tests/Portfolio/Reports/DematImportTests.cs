using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.Reports.ReportFiles;
using static XDrishti.Application.Tests.Portfolio.Reports.ReportScenario;

namespace XDrishti.Application.Tests.Portfolio.Reports;

/// <summary>
/// The demat holding statement as a check and a source of names (ADR 0029, P2-17 criteria 4 and 11): the preview compares the depository's quantities with the synced holdings and lists what differs; nothing changes a holding; the import remembers the names.
/// </summary>
public sealed class DematImportTests
{
    private static Task<ReportScenario> StartAsync() => ReportScenario.StartAsync(s =>
    {
        s.Hold("ZENITHE", Zenith, 30, 12m);
        s.Hold("ORIONSTL", Orion, 10, 100m);
        s.Hold("KESTREL", Kestrel, 5, 200m);
    });

    private static readonly string[] Statement =
    [
        DematRow("ZENITH ENERGY SYSTEMS LIMITED", Zenith, free: "20", locked: "5", mtf: "5"),
        DematRow("ORION STEEL LIMITED", Orion, free: "12"),
        DematRow("NOVA OXIDES & CHEMICALS LTD.", Nova, free: "7"),
        DematRow("SOLD OUT LIMITED", "INE999S01019", free: "0"),
    ];

    private static List<string> Shapes(ReportScenario s) =>
        [.. s.Fx.Data.Holdings.Select(h => $"{h.TradingSymbol}|{h.Qty}|{h.AvgCost}|{string.Join(',', h.Lots.Select(l => $"{l.Qty}@{l.UnitCost}"))}")];

    [Theory]
    [InlineData("trades")]
    [InlineData("ledger")]
    [InlineData("holdings")]
    public async Task No_run_of_the_account_holds_the_import_back_because_it_only_learns_names(string running)
    {
        var s = await StartAsync();
        var today = new DateOnly(2026, 10, 10);
        if (running == "holdings")
        {
            s.Fx.Data.Syncs.Add(HoldingSync.Start(s.Account.Id, SyncTrigger.Manual, PortfolioFixture.Actor, s.Fx.Clock.GetUtcNow()));
        }
        else
        {
            s.Fx.BrokerData.Runs.Add(BrokerSyncRun.Start(s.Account.Id, running == "trades" ? BrokerSyncKind.Trades : BrokerSyncKind.Ledger, BrokerSyncTrigger.Scheduled, today.AddDays(-30), today, "system:broker-history", s.Fx.Clock.GetUtcNow()));
        }

        var result = Ok(await s.ImportAsync(Demat(Statement)));

        result.Status.ShouldBe("imported");
    }

    [Fact]
    public async Task A_broker_history_run_of_another_account_does_not_hold_the_import_back()
    {
        var s = await StartAsync();
        var today = new DateOnly(2026, 10, 10);
        s.Fx.BrokerData.Runs.Add(BrokerSyncRun.Start(Guid.NewGuid(), BrokerSyncKind.Trades, BrokerSyncTrigger.Scheduled, today.AddDays(-30), today, "system:broker-history", s.Fx.Clock.GetUtcNow()));

        var done = await s.ImportOkAsync(Demat(Statement));

        done.Status.ShouldBe("imported");
    }

    [Fact]
    public async Task The_preview_counts_the_securities_that_agree_and_lists_what_differs()
    {
        var s = await StartAsync();

        var preview = await s.PreviewOkAsync(Demat(Statement));

        preview.Kind.ShouldBe("demat_holding");
        var demat = preview.Demat!;
        (demat.Securities, demat.Matching).ShouldBe((4, 1));
        demat.QuantityDifferences.Single().ShouldBe(new XDrishti.Application.Portfolio.Reports.DematDifference(Orion, "ORION STEEL LIMITED", 12m, 10m));
        demat.HeldNotSynced.Single().Isin.ShouldBe(Nova);
        demat.HeldNotSynced.Single().Quantity.ShouldBe(7m);
        demat.SyncedNotInStatement.Single().Symbol.ShouldBe("KESTREL");
        demat.AsOf.ShouldBe(new DateOnly(2026, 10, 15));
        preview.Warnings.ShouldContain(w => w.Code == "holdings_differ" && w.Count == 3);
        (demat.QuantityDifferencesTotal, demat.HeldNotSyncedTotal, demat.SyncedNotInStatementTotal).ShouldBe((1, 1, 1));
    }

    [Fact]
    public async Task A_statement_that_agrees_with_the_holdings_has_no_differences_and_no_warning()
    {
        var s = await StartAsync();
        var text = Demat([DematRow("ZENITH", Zenith, free: "30"), DematRow("ORION", Orion, free: "10"), DematRow("KESTREL", Kestrel, free: "5")]);

        var preview = await s.PreviewOkAsync(text);

        preview.Demat!.Matching.ShouldBe(3);
        preview.Demat.QuantityDifferences.ShouldBeEmpty();
        preview.Warnings.ShouldNotContain(w => w.Code == "holdings_differ");
    }

    [Fact]
    public async Task A_closed_holding_is_not_a_synced_holding_and_a_security_listed_twice_counts_its_sum()
    {
        var s = await StartAsync();
        var text = Demat([DematRow("ZENITH", Zenith, free: "10"), DematRow("ZENITH", Zenith, free: "20"), DematRow("ORION", Orion, free: "10"), DematRow("KESTREL", Kestrel, free: "5")]);

        var preview = await s.PreviewOkAsync(text);

        preview.Demat!.Matching.ShouldBe(3);
        preview.Demat.Securities.ShouldBe(3);
    }

    [Fact]
    public async Task The_import_changes_no_holding_and_no_lot_and_no_trade()
    {
        var s = await StartAsync();
        var lots = Shapes(s);

        var done = await s.ImportOkAsync(Demat(Statement));

        done.Status.ShouldBe("imported");
        Shapes(s).ShouldBe(lots);
        s.Fx.Data.Holdings.Count.ShouldBe(3);
        s.Active().ShouldBeEmpty();
    }

    [Fact]
    public async Task The_import_teaches_the_names_once_and_says_so()
    {
        var s = await StartAsync();

        var preview = await s.PreviewOkAsync(Demat(Statement));
        var done = await s.ImportOkAsync(Demat(Statement));
        var again = await s.ImportOkAsync(Demat(Statement));

        preview.Demat!.Names.ShouldBe(new XDrishti.Application.Portfolio.Reports.NameLearning(0, 4));
        done.Demat!.ShouldBe(new XDrishti.Application.Portfolio.Reports.DematImportResult(4, 0));
        (again.Status, again.Demat!.NamesLearned, again.Demat.NamesKnown).ShouldBe(("nothing_new", 0, 4));
        s.Learned.Aliases.ShouldAllBe(a => a.Source == SecurityAliasSource.Demat);
        s.Fx.Events(AccountEventType.SecurityNamesLearned).Single().Detail.ShouldBe("Security names learned from a demat holding statement: 4 new");
    }

    [Fact]
    public async Task The_import_needs_the_digest_of_the_preview()
    {
        var s = await StartAsync();

        var missing = Ok(await s.ImportAsync(Demat(Statement), noDigest: true));
        var stale = Ok(await s.ImportAsync(Demat(Statement), digest: "stale"));

        missing.Error!.Field.ShouldBe("digest");
        stale.Error!.Code.ShouldBe("portfolio.preview_stale");
        stale.Preview!.Demat.ShouldNotBeNull();
        s.Learned.Aliases.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_digest_goes_stale_when_a_holding_changes_before_the_import()
    {
        var s = await StartAsync();
        var text = Demat(Statement);
        var digest = (await s.PreviewOkAsync(text)).Digest;
        s.Fx.Dhan.Rows.Clear();
        s.Fx.Dhan.Rows.Add(PortfolioFixture.Row("ORIONSTL", 12, 100m, 100m, "SORIONSTL", Orion));
        s.Fx.Dhan.Rows.Add(PortfolioFixture.Row("ZENITHE", 30, 12m, 12m, "SZENITHE", Zenith));
        s.Fx.Dhan.Rows.Add(PortfolioFixture.Row("KESTREL", 5, 200m, 200m, "SKESTREL", Kestrel));
        Ok(await s.Fx.SyncAsync(s.Account.Id));

        var done = Ok(await s.ImportAsync(text, digest: digest));

        done.Error!.Code.ShouldBe("portfolio.preview_stale");
    }

    [Fact]
    public async Task A_statement_for_another_client_code_warns_and_is_refused_unless_ticked()
    {
        var s = await StartAsync();
        s.Remembers(ClientCode);
        var text = Demat(Statement, code: "9999999999");

        var preview = await s.PreviewOkAsync(text);
        var refused = Ok(await s.ImportAsync(text));
        var accepted = Ok(await s.ImportAsync(text, acceptOtherAccount: true));

        preview.Warnings.ShouldContain(w => w.Code == "other_account");
        refused.Error!.Code.ShouldBe("portfolio.other_account");
        accepted.Demat!.NamesLearned.ShouldBe(4);
    }

    [Fact]
    public async Task Nothing_personal_reaches_a_response_an_audit_event_or_a_log()
    {
        var s = await StartAsync();
        s.Remembers(ClientCode);
        var text = Demat(Statement, code: "9999999999");

        var preview = await s.PreviewOkAsync(text);
        var done = Ok(await s.ImportAsync(text, acceptOtherAccount: true));

        var disclosed = s.Disclosed(preview, done);
        disclosed.ShouldNotContain("SENTINEL");
        disclosed.ShouldNotContain("9999999999");
    }

    [Fact]
    public async Task The_differences_are_not_raised_as_a_notice_only_returned_in_the_preview()
    {
        var s = await StartAsync();

        var preview = await s.PreviewOkAsync(Demat(Statement));

        // The attention items are derived from stored facts for all accounts; a comparison with a file is not one. The preview carries it, and says so.
        preview.Demat!.QuantityDifferencesTotal.ShouldBe(1);
        s.Fx.Events(AccountEventType.HoldingsSynced).ShouldBeEmpty();
    }
}
