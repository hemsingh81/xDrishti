using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Actions;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Market.Actions;
using XDrishti.Application.Market.Baskets;
using XDrishti.Application.Market.Search;
using XDrishti.Application.Research;
using XDrishti.Application.Tests.Market.Bars;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Application.Tests.Research;

/// <summary>
/// The handler of P3-04a with every port faked: the extended <see cref="FakeBarStore"/>, NSubstitute queries and <see cref="FakeSimulationEngine"/>. An instrument is registered with <see cref="Add"/>; the request helpers build the
/// requests of the tests (a three-day window of the F days by default).
/// </summary>
internal sealed class RunFixture
{
    public static readonly DateOnly D1 = ReplayDays.D1;

    public static readonly DateOnly D2 = ReplayDays.D2;

    public static readonly DateOnly D3 = ReplayDays.D3;

    public FakeBarStore Store { get; } = new();

    public IBarQueries BarQueries { get; } = Substitute.For<IBarQueries>();

    public IBasketQueries Baskets { get; } = Substitute.For<IBasketQueries>();

    public IAdjustmentFactors Factors { get; } = Substitute.For<IAdjustmentFactors>();

    public ICorporateActionQueries Actions { get; } = Substitute.For<ICorporateActionQueries>();

    public ITradingCalendar Calendar { get; } = Substitute.For<ITradingCalendar>();

    public FakeSimulationEngine Engine { get; } = new();

    public FakeStrategyStore Strategies { get; } = new();

    public FakeCostSheetStore CostSheets { get; } = new();

    public FakeTimeProvider Time { get; } = new();

    public StructuredLogger<RunSimulationHandler> Log { get; } = new();

    /// <summary>What the CSV query answers: instrument to the date its loaded files reach.</summary>
    public Dictionary<Guid, DateOnly> CsvThrough { get; } = [];

    public RunFixture()
    {
        Factors.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(AdjustmentFactors.None);
        Actions.ListCsvAdjustedThroughAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns((IReadOnlyDictionary<Guid, DateOnly>)CsvThrough);
        Calendar.GetAsync(Arg.Any<CancellationToken>()).Returns(new TradingCalendar());
        Store.ForbidMerge = true;
    }

    public RunSimulationHandler Handler => new(Store, BarQueries, Baskets, Factors, Actions, Calendar, Engine, Strategies, CostSheets, Time, Log);

    /// <summary>Registers an instrument with a row (the tick is in paise, as the master has it) and its stored bars.</summary>
    public Guid Add(string symbol, IEnumerable<Bar>? bars = null, decimal? tickPaise = 5m, Guid? id = null)
    {
        var instrument = id ?? Guid.CreateVersion7();
        BarQueries.FindInstrumentAsync(instrument, Arg.Any<CancellationToken>()).Returns(new BarsInstrument(instrument, symbol, symbol + " Ltd", tickPaise));
        if (bars is not null)
        {
            Store.Stored[instrument] = [.. bars];
        }

        return instrument;
    }

    public ValueTask<Result<SimulationRun>> RunAsync(SimulationRequest request, CancellationToken cancellationToken = default) =>
        Handler.HandleAsync(new RunSimulationQuery(request), cancellationToken);

    public static SimulationRequest List(IReadOnlyList<Guid> ids, DateOnly? from = null, DateOnly? to = null, CostChoice? costs = null) =>
        new("ORB", null, "15m", from ?? D1, to ?? D3, ids, null, costs ?? CostChoice.Gross);

    public static SimulationRequest Basket(Guid basketId, DateOnly? from = null, DateOnly? to = null, CostChoice? costs = null) =>
        new("ORB", null, "15m", from ?? D1, to ?? D3, null, basketId, costs ?? CostChoice.Gross);

    /// <summary>A manual basket of the instruments, in the order of the positions given.</summary>
    public void SetBasket(Guid basketId, string name, DateTimeOffset? archivedAt, params (Guid Id, string Symbol, int Position, decimal? TickPaise, bool Retired)[] members)
    {
        var items = members.Select(m => new BasketMemberView(
            Guid.CreateVersion7(), Summary(m.Id, m.Symbol, m.TickPaise, m.Retired), m.Position, DateTimeOffset.UnixEpoch, null, null, "owner", null, null)).ToList();
        Baskets.MembersAsync(basketId, null, null, Arg.Any<CancellationToken>()).Returns(new BasketMembers(basketId, name, archivedAt, null, items.Count, items));
    }

    public static InstrumentSummary Summary(Guid id, string symbol, decimal? tickPaise, bool retired) =>
        new(id, symbol, symbol + " Ltd", symbol, "share", null, "EQ", null, 1, tickPaise, null, false, false, retired, TrackedInfo.Never);

    /// <summary>The F1 day and the flat days around it: bars for d1, d2 and d3 as in P3-04's criterion 1.</summary>
    public static Bar[] ThreeDays() => ReplayDays.Join(ReplayDays.Flat(D1, 99.00), ReplayDays.F1(D2), ReplayDays.Flat(D3, 105.00));

    /// <summary>An applied 1:2 split with the ex-date.</summary>
    public static AdjustmentFactors Split(DateOnly exDate) =>
        AdjustmentFactors.From([new AppliedAction(exDate, CorporateActionKind.Split, 1, 2, CorporateActionRatio.Factor(CorporateActionKind.Split, 1, 2))]);

    /// <summary>Nothing was read from any port: the basket, the calendar, the bars, the factors, the actions and the instrument rows.</summary>
    public void ShouldHaveReadNothing()
    {
        Store.Reads.ShouldBeEmpty();
        Baskets.ReceivedCalls().ShouldBeEmpty();
        Calendar.ReceivedCalls().ShouldBeEmpty();
        Factors.ReceivedCalls().ShouldBeEmpty();
        Actions.ReceivedCalls().ShouldBeEmpty();
        BarQueries.ReceivedCalls().ShouldBeEmpty();
        Engine.Replays.ShouldBeEmpty();
    }
}
