#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
using NSubstitute;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Sizing;
using XDrishti.Domain.Confidence;

namespace XDrishti.Application.Tests.Sizing;

/// <summary>Characterisation of the live read (BK-10) after the core was shared with the system replay: with everything off and no budget nothing is read at all, not a bar and not the calendar.</summary>
public sealed class SizingInputSourceTests
{
    [Fact]
    public async Task With_everything_off_and_no_budget_no_bar_and_no_calendar_is_read()
    {
        var bars = Substitute.For<IBarQueries>();
        var calendars = Substitute.For<ITradingCalendar>();
        var classifications = Substitute.For<IHoldingClassifications>();
        var source = new SizingInputSource(new FakeSizingStore(), bars, calendars, classifications, new CostStressOptions());

        var inputs = await source.ReadAsync(new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 6), [new ContextInstrument(Guid.NewGuid(), "AAA")], TestContext.Current.CancellationToken);

        inputs.Symbols.ShouldBeEmpty();
        inputs.Version.ShouldBe(0);
        calendars.ReceivedCalls().ShouldBeEmpty();
        bars.ReceivedCalls().ShouldBeEmpty();
        classifications.ReceivedCalls().ShouldBeEmpty();
    }
}
