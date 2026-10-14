using XDrishti.Application.Portfolio.DividendImport;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.DividendImport.DividendFiles;
using static XDrishti.Application.Tests.Portfolio.PortfolioFixture;

namespace XDrishti.Application.Tests.Portfolio.DividendImport;

/// <summary>The dividend import (ADR 0028, P2-15 criteria 5, 9 and 11): one batch of dividends with identity and quantity, choices and remembered names, digest staleness, includeDuplicates, audit with counts only. Clock: 2026-10-15, India.</summary>
public sealed class DividendImportTests
{
    private const string Orion = "13 Jun 2024,Orion Steel,4,25,100.00";

    private const string Nobody = "23 Sept 2024,Nobody Knows,2.5,40,100.00";

    [Fact]
    public async Task The_rows_to_add_are_stored_as_one_batch_with_source_csv_identity_quantity_and_the_holding()
    {
        var s = await DividendScenario.StartAsync();
        var orion = s.Hold("Orion Steel Limited", "ORION", "INE000A01011", instrument: Guid.NewGuid());

        var result = await s.ImportOkAsync(Csv(Orion, Nobody));

        var stored = s.Active().OrderBy(d => d.PayDate).ToList();
        stored.Count.ShouldBe(2);
        var first = stored[0];
        (first.Source, first.HoldingId, first.InstrumentId, first.Isin, first.TradingSymbol, first.Quantity, first.PerShare, first.Amount, first.PayDate, first.ScripName).ShouldBe(
            (DividendSource.Csv, orion.Id, orion.InstrumentId, "INE000A01011", "ORION", 25L, 4m, 100m, new DateOnly(2024, 6, 13), null));
        first.ExternalId!.ShouldStartWith("d:");
        first.BatchId.ShouldBe(result.Batch!.Id);
        first.CreatedBy.ShouldBe(Actor);
        var unallocated = stored[1];
        (unallocated.HoldingId, unallocated.InstrumentId, unallocated.ScripName, unallocated.Source).ShouldBe((null, null, "Nobody Knows", DividendSource.Csv));
        s.Batches.Batches.ShouldHaveSingleItem().Imported.ShouldBe(2);
    }

    [Fact]
    public async Task The_result_counts_what_was_stored_and_how_it_was_matched()
    {
        var s = await DividendScenario.StartAsync();
        s.Hold("Orion Steel Limited", "ORION");
        s.ListInMaster("ZEPHYR MOTORS LTD", "ZEPHYR", "INE222C01033");

        var result = await s.ImportOkAsync(Csv(Orion, "01 Jul 2024,Zephyr Motors,1,10,10.00", Nobody));

        (result.Imported, result.Amount, result.Allocated, result.InstrumentOnly, result.Unallocated, result.AlreadyImported, result.PossibleDuplicatesSkipped, result.AliasesSaved).ShouldBe((3, 210m, 1, 1, 1, 0, 0, 0));
        result.Batch.ShouldNotBeNull().ShouldSatisfyAllConditions(
            b => b.Status.ShouldBe("imported"),
            b => b.AccountName.ShouldBe(s.Account.Name),
            b => b.ActiveDividends.ShouldBe(3),
            b => b.Amount.ShouldBe(210m),
            b => b.Sha256.Length.ShouldBe(64));
        s.Active().Single(d => d.TradingSymbol == "ZEPHYR").ScripName.ShouldBe("Zephyr Motors");
    }

    [Fact]
    public async Task The_batch_records_the_file_the_counts_and_who_and_when()
    {
        var s = await DividendScenario.StartAsync();
        var csv = Csv(Orion, Orion, "bad,Orion Steel,1,1,1.00");

        var result = await s.ImportOkAsync(csv);

        var batch = s.Batches.Batches.Single();
        (batch.FileName, batch.Parsed, batch.Imported, batch.Duplicates, batch.Bad, batch.Amount, batch.Status, batch.CreatedBy, batch.CreatedAt).ShouldBe(
            ("dividends.csv", 2, 1, 1, 1, 100m, DividendBatchStatus.Imported, Actor, s.Fx.Clock.GetUtcNow()));
        (result.Imported, result.AlreadyImported).ShouldBe((1, 0));
    }

    [Fact]
    public async Task Importing_the_same_file_again_stores_nothing_and_makes_no_batch()
    {
        var s = await DividendScenario.StartAsync();
        s.Hold("Orion Steel Limited", "ORION");
        var csv = Csv(Orion, Nobody);
        await s.ImportOkAsync(csv);

        var again = await s.ImportOkAsync(csv);

        (again.Imported, again.AlreadyImported, again.Batch).ShouldBe((0, 2, null));
        s.Active().Count.ShouldBe(2);
        s.Batches.Batches.Count.ShouldBe(1);
        s.Fx.Events(AccountEventType.DividendsImported).Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_corrected_file_adds_only_what_is_new()
    {
        var s = await DividendScenario.StartAsync();
        await s.ImportOkAsync(Csv(Orion));

        var corrected = await s.ImportOkAsync(Csv(Orion, Nobody));

        (corrected.Imported, corrected.AlreadyImported).ShouldBe((1, 1));
        s.Active().Count.ShouldBe(2);
        s.Batches.Batches.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Importing_after_an_undo_stores_the_file_again()
    {
        var s = await DividendScenario.StartAsync();
        var csv = Csv(Orion, Nobody);
        var first = await s.ImportOkAsync(csv);
        Ok(await s.UndoAsync(first.Batch!.Id));

        var again = await s.ImportOkAsync(csv);

        (again.Imported, again.AlreadyImported).ShouldBe((2, 0));
        s.Active().Count.ShouldBe(2);
        s.Fx.Dividends.Dividends.Count.ShouldBe(4);
        s.Batches.Batches.Select(b => b.Status).ShouldBe([DividendBatchStatus.Undone, DividendBatchStatus.Imported]);
    }

    [Fact]
    public async Task Two_payments_of_one_name_on_one_day_are_both_stored_and_a_row_repeated_identically_once()
    {
        var s = await DividendScenario.StartAsync();

        var csv = Csv("18 Mar 2025,Lumen Oils,5,100,500.00", "18 Mar 2025,Lumen Oils,4.5,100,450.00", "18 Mar 2025,Lumen Oils,4.5,100,450.00");

        var preview = await s.PreviewOkAsync(csv);
        var result = await s.ImportOkAsync(csv);

        (result.Imported, result.Amount).ShouldBe((2, 950m));
        preview.File.Repeated.ShouldBe(1);
    }

    [Fact]
    public async Task The_digest_is_required()
    {
        var s = await DividendScenario.StartAsync();
        var handler = new ImportDividendsHandler(s.Workspace, s.Fx.Dividends, s.Batches, s.Aliases, s.Fx.Accounts, s.Fx.Data, s.Fx.Clock, Microsoft.Extensions.Logging.Abstractions.NullLogger<ImportDividendsHandler>.Instance);

        var result = await handler.HandleAsync(new ImportDividendsCommand(s.Account.Id, "d.csv", DividendScenario.Content(Csv(Orion)), "  ", [], [], Actor), CancellationToken.None);

        FieldsOf(result).ShouldBe(["digest"]);
        s.Active().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_digest_from_before_a_change_of_holdings_is_stale_nothing_is_stored_and_the_fresh_report_comes_with_the_error()
    {
        var s = await DividendScenario.StartAsync();
        var csv = Csv(Orion);
        var digest = (await s.PreviewOkAsync(csv)).Digest;
        s.Hold("Orion Steel Limited", "ORION");

        var result = await s.ImportAsync(csv, digest: digest);

        result.IsFailure.ShouldBeTrue();
        (result.Error.Code, result.Error.Kind).ShouldBe((PortfolioErrors.PreviewStaleCode, XDrishti.Domain.Common.ErrorKind.Conflict));
        var fresh = (DividendPreviewReport)result.Error.Payload!["report"];
        fresh.Digest.ShouldNotBe(digest);
        fresh.Items.Single().Match.Kind.ShouldBe("name");
        s.Active().ShouldBeEmpty();
        s.Batches.Batches.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_digest_is_stale_after_an_alias_a_stored_dividend_or_a_typed_in_one_changed_the_rows()
    {
        var s = await DividendScenario.StartAsync();
        s.Hold("Orion Steel Limited", "ORION");
        var other = s.Hold("Delta Power Limited", "DELTA");
        var csv = Csv(Orion);
        var afterAlias = (await s.PreviewOkAsync(csv)).Digest;
        s.Aliases.Add(DividendHoldings.AliasToHolding("Orion Steel", other));
        s.Aliases.ForgetCollected();

        var staleAlias = await s.ImportAsync(csv, digest: afterAlias);
        var beforeManual = (await s.PreviewOkAsync(csv)).Digest;
        s.AddManual(other.Id, new DateOnly(2024, 6, 13), 100m);
        var staleManual = await s.ImportAsync(csv, digest: beforeManual);

        staleAlias.Error.Code.ShouldBe(PortfolioErrors.PreviewStaleCode);
        staleManual.Error.Code.ShouldBe(PortfolioErrors.PreviewStaleCode);
    }

    [Fact]
    public async Task A_change_that_does_not_touch_any_row_does_not_make_the_preview_stale()
    {
        var s = await DividendScenario.StartAsync();
        s.Hold("Orion Steel Limited", "ORION");
        var csv = Csv(Orion);
        var digest = (await s.PreviewOkAsync(csv)).Digest;
        s.Hold("Zephyr Motors Limited", "ZEPHYR");

        var result = await s.ImportAsync(csv, digest: digest);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_possible_duplicate_is_left_out_unless_its_line_is_ticked()
    {
        var s = await DividendScenario.StartAsync();
        var orion = s.Hold("Orion Steel Limited", "ORION");
        s.AddManual(orion.Id, new DateOnly(2024, 6, 13), 100m);
        var csv = Csv(Orion, "14 Jun 2024,Orion Steel,1,10,10.00");

        var left = await s.ImportOkAsync(csv);
        var ticked = await s.ImportOkAsync(csv, includeDuplicates: [2, 77]);

        (left.Imported, left.PossibleDuplicatesSkipped, left.AlreadyImported).ShouldBe((1, 1, 0));
        (ticked.Imported, ticked.PossibleDuplicatesSkipped, ticked.AlreadyImported).ShouldBe((1, 0, 1));
        s.Active().Count(d => d.Source == DividendSource.Csv).ShouldBe(2);
    }

    [Fact]
    public async Task A_choice_gives_a_name_a_holding_and_remembering_it_saves_an_alias_that_the_next_file_uses_without_a_question()
    {
        var s = await DividendScenario.StartAsync();
        var delta = s.Hold("Delta Power Limited", "DELTA");
        var first = Csv("13 Jun 2024,DPC Ltd,4,25,100.00");

        var previewBefore = await s.PreviewOkAsync(first);
        var result = await s.ImportOkAsync(first, [new DividendChoice("DPC Ltd", HoldingId: delta.Id, Remember: true)]);
        var next = await s.PreviewOkAsync(Csv("14 Jun 2025,DPC,4,25,100.00"));

        previewBefore.Items.Single().Status.ShouldBe("needs_match");
        (result.Allocated, result.Unallocated, result.AliasesSaved).ShouldBe((1, 0, 1));
        s.Active().Single().HoldingId.ShouldBe(delta.Id);
        var alias = s.Aliases.Aliases.ShouldHaveSingleItem();
        (alias.AccountId, alias.NameKey, alias.HoldingId, alias.CreatedBy).ShouldBe((s.Account.Id, "dpc", delta.Id, Actor));
        (next.Items.Single().Match.Kind, next.Items.Single().Match.HoldingId, next.Items.Single().Status).ShouldBe(("alias", delta.Id, "new"));
        var audit = s.Fx.Events(AccountEventType.DividendAliasSaved).ShouldHaveSingleItem();
        audit.Detail.ShouldBe("Dividend aliases saved: 1 new, 0 pointed elsewhere");
    }

    [Fact]
    public async Task A_choice_without_remember_changes_this_import_only()
    {
        var s = await DividendScenario.StartAsync();
        var delta = s.Hold("Delta Power Limited", "DELTA");
        var csv = Csv("13 Jun 2024,DPC Ltd,4,25,100.00");

        var result = await s.ImportOkAsync(csv, [new DividendChoice("DPC Ltd", HoldingId: delta.Id)]);

        result.AliasesSaved.ShouldBe(0);
        s.Aliases.Aliases.ShouldBeEmpty();
        s.Fx.Events(AccountEventType.DividendAliasSaved).ShouldBeEmpty();
    }

    [Fact]
    public async Task Choosing_again_for_a_remembered_name_points_the_alias_elsewhere_and_leaves_stored_dividends_alone()
    {
        var s = await DividendScenario.StartAsync();
        var delta = s.Hold("Delta Power Limited", "DELTA");
        var orion = s.Hold("Orion Steel Limited", "ORION");
        await s.ImportOkAsync(Csv("13 Jun 2024,DPC Ltd,4,25,100.00"), [new DividendChoice("DPC Ltd", HoldingId: delta.Id, Remember: true)]);
        var aliasId = s.Aliases.Aliases.Single().Id;

        var again = await s.ImportOkAsync(Csv("14 Jun 2025,DPC Ltd,4,25,100.00"), [new DividendChoice("DPC Ltd", HoldingId: orion.Id, Remember: true)]);

        again.AliasesSaved.ShouldBe(1);
        var alias = s.Aliases.Aliases.ShouldHaveSingleItem();
        (alias.Id, alias.HoldingId).ShouldBe((aliasId, orion.Id));
        s.Active().OrderBy(d => d.PayDate).Select(d => d.HoldingId).ShouldBe([delta.Id, orion.Id]);
        s.Fx.Events(AccountEventType.DividendAliasSaved).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Choosing_the_same_thing_again_saves_nothing_new()
    {
        var s = await DividendScenario.StartAsync();
        var delta = s.Hold("Delta Power Limited", "DELTA");
        await s.ImportOkAsync(Csv("13 Jun 2024,DPC Ltd,4,25,100.00"), [new DividendChoice("DPC Ltd", HoldingId: delta.Id, Remember: true)]);

        var again = await s.ImportOkAsync(Csv("14 Jun 2025,DPC Ltd,4,25,100.00"), [new DividendChoice("DPC Ltd", HoldingId: delta.Id, Remember: true)]);

        again.AliasesSaved.ShouldBe(0);
        s.Fx.Events(AccountEventType.DividendAliasSaved).Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_remembered_nothing_keeps_a_name_unallocated_without_asking_again()
    {
        var s = await DividendScenario.StartAsync();
        s.Hold("Delta Power Limited", "DELTA");
        s.Hold("DELTA POWER LTD", "DELTA-B");
        await s.ImportOkAsync(Csv("13 Jun 2024,Delta Power,4,25,100.00"), [new DividendChoice("Delta Power", None: true, Remember: true)]);

        var next = await s.PreviewOkAsync(Csv("14 Jun 2025,Delta Power,4,25,100.00"));

        (next.Items.Single().Status, next.Items.Single().Match.Kind).ShouldBe(("new", "alias"));
    }

    [Fact]
    public async Task A_choice_of_an_instrument_stores_the_instrument_without_a_holding()
    {
        var s = await DividendScenario.StartAsync();
        var share = s.ListInMaster("ZEPHYR MOTORS PASSENGER VEHICLES LTD", "ZEPHYRPV", "INE222C01033");

        var result = await s.ImportOkAsync(Csv("13 Jun 2024,Zephyr Motors,4,25,100.00"), [new DividendChoice("Zephyr Motors", InstrumentId: share.Id, Remember: true)]);

        var stored = s.Active().Single();
        (stored.HoldingId, stored.InstrumentId, stored.Isin, stored.TradingSymbol, stored.ScripName).ShouldBe((null, share.Id, "INE222C01033", "ZEPHYRPV", "Zephyr Motors"));
        (result.InstrumentOnly, result.AliasesSaved).ShouldBe((1, 1));
    }

    [Fact]
    public async Task Names_remembered_for_one_account_are_not_used_for_another()
    {
        var s = await DividendScenario.StartAsync();
        var delta = s.Hold("Delta Power Limited", "DELTA");
        await s.ImportOkAsync(Csv("13 Jun 2024,DPC Ltd,4,25,100.00"), [new DividendChoice("DPC Ltd", HoldingId: delta.Id, Remember: true)]);
        var other = await s.Fx.ConnectedAsync("Example Two", "1000000077");

        var report = Ok(await s.PreviewAsync(Csv("13 Jun 2024,DPC Ltd,4,25,100.00"), other.Id));

        report.Items.Single().Status.ShouldBe("needs_match");
    }

    [Theory]
    [InlineData("nobody")]
    [InlineData("holding")]
    public async Task A_choice_for_a_name_not_in_the_file_or_a_holding_of_another_account_is_refused_and_nothing_is_stored(string kind)
    {
        var s = await DividendScenario.StartAsync();
        var foreign = s.Hold("Foreign Limited", "FOREIGN", account: Guid.NewGuid());
        var choice = kind == "nobody" ? new DividendChoice("Not In The File", None: true) : new DividendChoice("DPC Ltd", HoldingId: foreign.Id);

        var result = await s.ImportAsync(Csv("13 Jun 2024,DPC Ltd,4,25,100.00"), [choice]);

        FieldsOf(result).ShouldBe(["choices"]);
        s.Active().ShouldBeEmpty();
        s.Aliases.Aliases.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_save_that_collides_with_a_concurrent_import_stores_nothing_and_answers_a_conflict()
    {
        var s = await DividendScenario.StartAsync();
        var delta = s.Hold("Delta Power Limited", "DELTA");
        s.Fx.Data.CollidingSaves = 1;

        var result = await s.ImportAsync(Csv("13 Jun 2024,DPC Ltd,4,25,100.00"), [new DividendChoice("DPC Ltd", HoldingId: delta.Id, Remember: true)]);

        result.Error.Code.ShouldBe(PortfolioErrors.ConflictCode);
        s.Fx.Dividends.Dividends.ShouldBeEmpty();
        s.Batches.Batches.ShouldBeEmpty();
        s.Aliases.Aliases.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_audit_has_counts_and_ids_but_no_file_name_scrip_name_or_amount()
    {
        var s = await DividendScenario.StartAsync();
        s.Hold("Orion Steel Limited", "ORION");

        var result = await s.ImportOkAsync(Csv("13 Jun 2024,Orion Steel,4,25,100.00", "13 Jun 2024,Secret Holdings,2.5,400,1000.00"));

        var audit = s.Fx.Events(AccountEventType.DividendsImported).ShouldHaveSingleItem();
        audit.Detail.ShouldBe($"Dividends imported: batch {result.Batch!.Id}, 2 valid rows, 2 stored, 0 not stored, 0 bad");
        (audit.Detail!.Contains("Secret", StringComparison.Ordinal) || audit.Detail.Contains("1000", StringComparison.Ordinal) || audit.Detail.Contains("dividends.csv", StringComparison.Ordinal)).ShouldBeFalse();
        (audit.AccountId, audit.Actor).ShouldBe((s.Account.Id, Actor));
    }

    [Fact]
    public async Task An_import_of_a_tradebook_a_missing_account_or_a_role_less_account_is_refused()
    {
        var s = await DividendScenario.StartAsync();

        var trades = await s.ImportAsync(CsvWithHeader("date,symbol,side,quantity,price", "2024-03-05,EXMPL,buy,10,90"), digest: "x");
        var unknown = await s.ImportAsync(Csv(Orion), accountId: Guid.NewGuid(), digest: "x");

        trades.Error.Code.ShouldBe(PortfolioErrors.WrongFileKindCode);
        unknown.Error.Code.ShouldBe(PortfolioErrors.NotFoundCode);
    }
}
