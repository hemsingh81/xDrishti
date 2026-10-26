#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using NSubstitute;
using XDrishti.Application.Abstractions.ChargeSheet;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Reporting;
using XDrishti.Domain.Research;
using XDrishti.Infrastructure.Reporting;
using XDrishti.Tests.Shared;

namespace XDrishti.Infrastructure.Tests.Reporting;

/// <summary>Spec P8-23 (ADR 0127): the fact the paper comparison reads and the source the reports read are the owner's stored confirmation, judged against the sheet as it is stored now.</summary>
public sealed class StoredCostConfirmationTests
{
    private static readonly string Rates = new('a', 64);

    private static readonly DateOnly On = new(2026, 10, 26);

    private static readonly DateTimeOffset At = new(2026, 10, 26, 6, 0, 0, TimeSpan.Zero);

    private readonly IChargeSheetLog _log = Substitute.For<IChargeSheetLog>();

    private readonly ICostSheetStore _sheets = Substitute.For<ICostSheetStore>();

    private void Newest(ChargeSheetEntry? newest) =>
        _log.ReadAsync(0, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(new ChargeSheetLedger(true, newest, [])));

    private static CostSheet Sheet(string id, bool both) =>
        CostSheet.Create(id, "sheet", "invented", null, both ? [TestCostSheet.Schedule(TestCostSheet.ScheduleA, TestCostSheet.IntradayA, TestCostSheet.DeliveryA), TestCostSheet.Schedule(TestCostSheet.ScheduleB, TestCostSheet.IntradayB, TestCostSheet.DeliveryB)] : [TestCostSheet.Schedule(TestCostSheet.ScheduleA, TestCostSheet.IntradayA, TestCostSheet.DeliveryA)]).Value;

    private static ChargeSheetEntry Confirmed(string id, string rates) => new(5, ChargeSheetActKind.Confirm, id, rates, On, "owner:a@b.c", "checked", At);

    private ChargeSheetConfirmationStore Source() => new(_log, _sheets);

    [Fact]
    public async Task With_no_confirmation_the_source_and_the_fact_say_nothing_is_confirmed()
    {
        Newest(null);

        (await Source().FindAsync(TestContext.Current.CancellationToken)).ShouldBeNull();
        (await new StoredCostConfirmation(Source()).ConfirmedOnAsync("fixture-a", TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task A_confirmation_of_the_stored_rates_is_read_by_the_source_and_gives_its_day_for_that_sheet_only()
    {
        var sheet = Sheet("fixture-a", both: true);
        _sheets.FindAsync("fixture-a", Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(sheet)));
        Newest(Confirmed("fixture-a", sheet.RatesSha256));
        var fact = new StoredCostConfirmation(Source());

        var stored = await Source().FindAsync(TestContext.Current.CancellationToken);

        stored.ShouldBe(new ChargeSheetConfirmation("fixture-a", sheet.RatesSha256, On));
        (await fact.ConfirmedOnAsync("fixture-a", TestContext.Current.CancellationToken)).ShouldBe(On);
        (await fact.ConfirmedOnAsync("flat-10", TestContext.Current.CancellationToken)).ShouldBeNull();
        (await fact.ConfirmedOnAsync("Fixture-A", TestContext.Current.CancellationToken)).ShouldBeNull(); // the id is compared exactly
    }

    [Fact]
    public async Task A_confirmation_of_rates_the_sheet_no_longer_has_reads_as_not_confirmed_in_the_source_and_the_fact()
    {
        var old = Sheet("fixture-a", both: true);
        _sheets.FindAsync("fixture-a", Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(Sheet("fixture-a", both: false))));
        Newest(Confirmed("fixture-a", old.RatesSha256));

        (await Source().FindAsync(TestContext.Current.CancellationToken)).ShouldBeNull();
        (await new StoredCostConfirmation(Source()).ConfirmedOnAsync("fixture-a", TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task A_confirmation_whose_sheet_is_gone_and_a_revoke_read_as_not_confirmed()
    {
        _sheets.FindAsync("fixture-a", Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Failure<CostSheet>(CostErrors.SheetNotFound("fixture-a"))));
        Newest(Confirmed("fixture-a", Rates));
        (await Source().FindAsync(TestContext.Current.CancellationToken)).ShouldBeNull();

        Newest(new ChargeSheetEntry(6, ChargeSheetActKind.Revoke, "fixture-a", Rates, null, "owner:a@b.c", "withdrawn", At));
        (await Source().FindAsync(TestContext.Current.CancellationToken)).ShouldBeNull();
    }
}
