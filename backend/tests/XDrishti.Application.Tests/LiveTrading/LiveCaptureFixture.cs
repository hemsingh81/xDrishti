using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.LiveTrading;
using XDrishti.Application.Planning;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Tests.LiveTrading;

/// <summary>The hand-worked session of spec P11-01 (2026-11-03) on fake ports: one manual-live account, four approved tickets, an in-memory insert-only store with the real unique keys.</summary>
internal sealed class LiveCaptureFixture
{
    public static readonly DateOnly Day = new(2026, 11, 3);
    public static readonly DateTimeOffset Midday = new(new DateTime(2026, 11, 3, 11, 0, 0), TimeSpan.FromMinutes(330));
    private static readonly string Hash = new('a', 64);

    public BrokerAccount Account { get; } = Live();
    public InMemoryLiveStore Store { get; } = new();
    public FakeTimeProvider Time { get; }
    public IBrokerTradeBook Trades { get; } = Substitute.For<IBrokerTradeBook>();
    public IBrokerOrderBook Orders { get; } = Substitute.For<IBrokerOrderBook>();
    public IBrokerTrades History { get; } = Substitute.For<IBrokerTrades>();
    public IAccountStore Accounts { get; } = Substitute.For<IAccountStore>();
    public IPlanStore Plans { get; } = Substitute.For<IPlanStore>();
    public IGradeStore Grades { get; } = Substitute.For<IGradeStore>();
    public ITradeReferenceData Reference { get; } = Substitute.For<ITradeReferenceData>();
    public ITradingCalendar Calendar { get; } = Substitute.For<ITradingCalendar>();
    public ICommandHandler<GradePlansCommand, GradeOutcome> Grading { get; } = Substitute.For<ICommandHandler<GradePlansCommand, GradeOutcome>>();
    public FakeAccess Access { get; } = new();
    public Dictionary<long, StoredGrade> StoredGrades { get; } = [];
    public bool OrderBookOn { get; set; } = true;

    public sealed class FakeAccess : ILiveAccountAccess
    {
        public BrokerFailureKind? Reason { get; set; }

        public ValueTask<(SecretValue? Token, BrokerFailureKind? Reason)> TokenAsync(Guid accountId, CancellationToken cancellationToken)
        {
            SecretValue.TryCreate("token-for-tests-only", out var token, out _);
            return ValueTask.FromResult<(SecretValue?, BrokerFailureKind?)>(Reason is null ? (token, null) : (null, Reason));
        }
    }

    private sealed class Settings(LiveCaptureFixture f) : ILiveCaptureSettings
    {
        public bool Enabled => false;

        public bool OrderBookEnabled => f.OrderBookOn;

        public int PollSeconds => 120;

        public MatchTolerances Tolerances => MatchTolerances.Default;

        public TimeSpan ProtectWithin => TimeSpan.FromSeconds(300);
    }

    public LiveCaptureFixture(DateTimeOffset? at = null)
    {
        Time = new FakeTimeProvider(at ?? Midday);
        Accounts.ListActiveAsync(Arg.Any<CancellationToken>()).Returns([Account]);
        Accounts.FindReadOnlyAsync(Account.Id, Arg.Any<CancellationToken>()).Returns(Account);
        Calendar.GetAsync(Arg.Any<CancellationToken>()).Returns(new TradingCalendar());
        Reference.FindInstrumentsAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<TradeInstrument>());
        Reference.FindInstrumentsByDhanIdsAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<string, TradeInstrument>());
        History.FetchPageAsync(Arg.Any<SecretValue>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(BrokerResponse.Ok(BrokerTradePage.NoDataAnswer));
        Trades.ReadAsync(Arg.Any<SecretValue>(), Arg.Any<CancellationToken>()).Returns(BrokerResponse.Ok(TradeBook()));
        Orders.ReadAsync(Arg.Any<SecretValue>(), Arg.Any<CancellationToken>()).Returns(BrokerResponse.Ok(OrderBook()));
        Plans.ListAsync(Day, null, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([new PlanRow(1, new NewPlan("k", Day, new DateTimeOffset(2026, 11, 2, 21, 0, 0, TimeSpan.FromMinutes(330)), 1, Hash, "cell statistics only", true, "test", DateTimeOffset.UnixEpoch, []), PlanState.Published, 4, 0, 0)]);
        Plans.ItemsAsync(1, Arg.Any<CancellationToken>()).Returns([Item(1, "NSE:RELIANCE", 40, 1250m, 1240m), Item(2, "NSE:TCS", 10, 3400m, null), Item(3, "NSE:INFY", 30, 1500m, 1480m), Item(4, "NSE:HDFCBANK", 25, 1600m, null)]);
        Plans.HistoryAsync(1, Arg.Any<CancellationToken>()).Returns([.. Enumerable.Range(1, 4).Select(i => new PlanEventRow(i, i, "proposed", "approved", "owner:me", "ok", 1, new DateTimeOffset(2026, 11, 2, 21, 5, 0, TimeSpan.FromMinutes(330))))]);
        Grades.ForPlansAsync(Arg.Any<IReadOnlyList<long>>(), Arg.Any<CancellationToken>()).Returns(_ => StoredGrades);
        Grading.HandleAsync(Arg.Any<GradePlansCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success(new GradeOutcome(1, 0, 0, 0, 0, 0)));
        Grade(1, GradeStatus.Graded, 0.5);
        Grade(2, GradeStatus.Graded, 0.8);
        Grade(3, GradeStatus.Graded, 1.20);
        Grade(4, GradeStatus.NotTriggered, null);
    }

    public CaptureLiveSessionHandler Capture() => new(Accounts, Access, Trades, Orders, Store, Reference, Calendar, new Settings(this), Time, NullLogger<CaptureLiveSessionHandler>.Instance);

    public SettleLiveSessionHandler Settle() => new(Accounts, Access, History, Store, new LiveTicketSource(Plans), Grades, Grading, Calendar, new Settings(this), Time, NullLogger<SettleLiveSessionHandler>.Instance);

    public GetLiveCaptureHandler Get() => new(Accounts, Store, new LiveTicketSource(Plans), new Settings(this), Time);

    public void Grade(long item, GradeStatus status, double? r) =>
        StoredGrades[item] = new StoredGrade(item, new NewGrade(1, item, Day, new TicketGrade(status, r, null, null, null, null, null, "gross, no charges", "test"), null, DateTimeOffset.UnixEpoch));

    public static LiveBookRead<LiveBrokerFill> TradeBook() => new(
        [
            new LiveBrokerFill("T-1", "o1", Side.Buy, "RELIANCE", null, null, "CNC", 25, 1250.40m, Day, new TimeOnly(9, 41, 10)),
            new LiveBrokerFill("T-2", "o1", Side.Buy, "RELIANCE", null, null, "CNC", 15, 1250.60m, Day, new TimeOnly(9, 41, 30)),
            new LiveBrokerFill("T-3", "o2", Side.Sell, "RELIANCE", null, null, "CNC", 40, 1262m, Day, new TimeOnly(11, 5, 0)),
            new LiveBrokerFill("T-4", "o9", Side.Buy, "SBIN", null, null, "CNC", 100, 800m, Day, new TimeOnly(10, 15, 0)),
        ],
        4, 0, new Dictionary<string, int>(), BrokerReadDrops.None);

    public static LiveBookRead<LiveBrokerOrder> OrderBook(string status = "traded", long filled = 40) => new(
        [
            new LiveBrokerOrder("o1", "RELIANCE", null, null, Side.Buy, "CNC", "LIMIT", "DAY", 40, 1251m, null, status, filled, 1250.475m, At("09:40:00"), null, null, null, null, null),
            new LiveBrokerOrder("o2", "RELIANCE", null, null, Side.Sell, "CNC", "STOP_LOSS_MARKET", "DAY", 40, null, 1240m, "pending", 0, null, At("09:44:02"), null, null, null, null, null),
            new LiveBrokerOrder("o3", "TCS", null, null, Side.Buy, "CNC", "LIMIT", "DAY", 10, 3401m, null, "rejected", 0, null, At("09:50:00"), null, null, null, null, "insufficient margin"),
        ],
        3, 0, new Dictionary<string, int>(), BrokerReadDrops.None);

    private static DateTimeOffset At(string time) => new(Day.ToDateTime(TimeOnly.Parse(time, System.Globalization.CultureInfo.InvariantCulture)), TimeSpan.FromMinutes(330));

    private PlanItemRow Item(long id, string symbol, int qty, decimal entry, decimal? stop) =>
        new(id, 1, new NewPlanItem((int)id, TicketGroup.Primary, symbol, "BUY", "ORB", Hash, "t", "s", "g", "v", [], new PlanEvidence("c", 30, "r", "b", null, null, [], null), [new NewAllocation(Account.Id, "manual_live", qty, 0.25, 1)], new TicketLevels(entry, Day, stop, stop is null ? null : StopAnchor.Level, null, null)), TicketState.Armed);

    private static BrokerAccount Live()
    {
        var now = Midday;
        var account = BrokerAccount.Create("Live A", "1100000001", new DateOnly(2026, 10, 1), "#112233", null, new DateOnly(2026, 11, 3), now).Value;
        account.ConfigureTrading(new TradingSpec(TradingMode.ManualLive, Guid.NewGuid(), CapitalSource.Fixed, 500_000m, true, false, Instruments.EquityIntraday, Instruments.None, Instruments.None, TradingOverrides.None), now).IsSuccess.ShouldBeTrue();
        account.ChangeRoles(new RolesChange(null, true, null), null, null, now).IsSuccess.ShouldBeTrue();
        return account;
    }
}
