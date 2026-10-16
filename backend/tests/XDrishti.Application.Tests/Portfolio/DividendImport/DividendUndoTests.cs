using XDrishti.Application.Portfolio.DividendImport;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.DividendImport.DividendFiles;
using static XDrishti.Application.Tests.Portfolio.PortfolioFixture;

namespace XDrishti.Application.Tests.Portfolio.DividendImport;

/// <summary>Undoing a dividend import and listing the history (ADR 0028, P2-15 criteria 9 and 11). Clock: 2026-10-15, India.</summary>
public sealed class DividendUndoTests
{
    [Fact]
    public async Task Undo_voids_the_batchs_dividends_marks_it_undone_and_audits_the_count_only()
    {
        var s = await DividendScenario.StartAsync();
        var orion = s.Hold("Orion Steel Limited", "ORION");
        var typed = s.AddManual(orion.Id, new DateOnly(2024, 1, 5), 50m);
        var imported = await s.ImportOkAsync(Csv("13 Jun 2024,Orion Steel,4,25,100.00", "23 Sept 2024,Nobody Knows,2.5,40,100.00"));

        var undo = Ok(await s.UndoAsync(imported.Batch!.Id));

        (undo.AlreadyUndone, undo.Voided, undo.Batch.Status, undo.Batch.ActiveDividends).ShouldBe((false, 2, "undone", 0));
        undo.Batch.UndoneAt.ShouldNotBeNull();
        s.Active().ShouldBe([typed]);
        s.Fx.Dividends.Dividends.Where(d => d.BatchId == imported.Batch.Id).ShouldAllBe(d => d.IsVoided && d.VoidedBy == Actor);
        var audit = s.Fx.Events(AccountEventType.DividendsUndone).ShouldHaveSingleItem();
        audit.Detail.ShouldBe($"Dividend import {imported.Batch.Id} undone: 2 dividends voided");
    }

    [Fact]
    public async Task Undoing_twice_is_a_no_op()
    {
        var s = await DividendScenario.StartAsync();
        var imported = await s.ImportOkAsync(Csv("13 Jun 2024,Orion Steel,4,25,100.00"));
        Ok(await s.UndoAsync(imported.Batch!.Id));

        var twice = Ok(await s.UndoAsync(imported.Batch.Id));

        (twice.AlreadyUndone, twice.Voided, twice.Batch.Status).ShouldBe((true, 0, "undone"));
        s.Fx.Events(AccountEventType.DividendsUndone).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Undoing_an_unknown_import_is_not_found()
    {
        var s = await DividendScenario.StartAsync();

        var result = await s.UndoAsync(Guid.NewGuid());

        result.Error.Code.ShouldBe(PortfolioErrors.NotFoundCode);
    }

    [Fact]
    public async Task Undo_leaves_the_remembered_names_and_the_other_batches_alone()
    {
        var s = await DividendScenario.StartAsync();
        var delta = s.Hold("Delta Power Limited", "DELTA");
        var first = await s.ImportOkAsync(Csv("13 Jun 2024,DPC Ltd,4,25,100.00"), [new DividendChoice("DPC Ltd", HoldingId: delta.Id, Remember: true)]);
        var second = await s.ImportOkAsync(Csv("14 Jun 2025,DPC Ltd,4,25,100.00"));

        Ok(await s.UndoAsync(first.Batch!.Id));

        s.Aliases.Aliases.Count.ShouldBe(1);
        s.Active().Single().BatchId.ShouldBe(second.Batch!.Id);
    }

    [Fact]
    public async Task The_history_lists_batches_newest_first_with_the_account_name_and_their_counts_and_can_be_filtered_by_account()
    {
        var s = await DividendScenario.StartAsync();
        var first = await s.ImportOkAsync(Csv("13 Jun 2024,Orion Steel,4,25,100.00"));
        s.Fx.Clock.Advance(TimeSpan.FromMinutes(5));
        var second = await s.ImportOkAsync(Csv("14 Jun 2025,Orion Steel,4,25,100.00", "15 Jun 2025,Orion Steel,4,25,100.00"));

        var all = Ok(await s.ListBatchesAsync());
        var other = Ok(await s.ListBatchesAsync(Guid.NewGuid()));

        all.Items.Select(i => i.Id).ShouldBe([second.Batch!.Id, first.Batch!.Id]);
        (all.Items[0].AccountName, all.Items[0].Imported, all.Items[0].ActiveDividends, all.Total).ShouldBe((s.Account.Name, 2, 2, 2));
        other.Items.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    [InlineData(100_001, 10)]
    public async Task The_history_refuses_a_page_or_page_size_out_of_range(int page, int pageSize)
    {
        var s = await DividendScenario.StartAsync();

        var result = await s.ListBatchesAsync(page: page, pageSize: pageSize);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PortfolioErrors.InvalidCode);
    }
}
