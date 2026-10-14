using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.BrokerHistory;
using XDrishti.Application.Tests.Portfolio.Trades;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.PortfolioFixture;

namespace XDrishti.Application.Tests.Portfolio.BrokerHistory;

/// <summary>
/// The broker history use cases on in-memory ports and a scripted Dhan (ADR 0025). The account and its one holding come from <see cref="TradeScenario"/> (EXMPL, 30 shares at 100, ISIN INE000A01011, lot dated with the default first import date
/// 2025-01-01); the clock is Saturday 2026-10-10 14:30 India. Runs read from <see cref="From"/> unless the test says otherwise, which keeps the number of windows small.
/// </summary>
internal sealed class BrokerScenario
{
    /// <summary>The first date most tests read from (40 days before the clock: two windows).</summary>
    public static readonly DateOnly From = new(2026, 9, 1);

    private BrokerScenario(TradeScenario tradebook) => Tradebook = tradebook;

    /// <summary>The account, its holding and the tradebook helpers of the trade import tests.</summary>
    public TradeScenario Tradebook { get; }

    public PortfolioFixture Fx => Tradebook.Fx;

    public BrokerAccount Account => Tradebook.Account;

    public Holding Holding => Tradebook.Holding;

    public FakeBrokerHistory Dhan { get; } = new();

    public QueueSpy Queue { get; } = new();

    public InMemoryBrokerData Data => Fx.BrokerData;

    public static async Task<BrokerScenario> StartAsync(long qty = 30, decimal avgCost = 100m) => new(await TradeScenario.StartAsync(qty, avgCost));

    public BrokerHistoryReader Reader() => new(Dhan, Dhan);

    public BrokerLotRebuilder Rebuilder() => new(Fx.Workspace, Fx.Data, Fx.Trades, Fx.Data, Fx.Clock, NullLogger<BrokerLotRebuilder>.Instance);

    public BrokerHistoryPipeline Pipeline() =>
        new(Reader(), Rebuilder(), Data, Fx.Data, Fx.Trades, Fx.Trades, Data, Fx.Accounts, Fx.Data, Fx.Clock, NullLogger<BrokerHistoryPipeline>.Instance);

    public BrokerHistoryAccess Access() => new(Fx.Fetcher, Data, Fx.Clock);

    public SyncBrokerHistoryHandler Sync() => new(Access(), Pipeline());

    public StartBrokerHistoryHandler Start() => new(Access(), Pipeline(), Queue);

    public ExecuteBrokerHistoryHandler Execute() => new(Access(), Pipeline(), Data);

    public GetBrokerHistoryStatusHandler Status() => new(Fx.Accounts, Data, new FixedSchedules());

    public ListLedgerHandler Ledger() => new(Fx.Accounts, Data);

    /// <summary>"Sync now" that waits: the trades, then the ledger, from <see cref="From"/> unless another date is given.</summary>
    public Task<Result<BrokerHistoryRunView>> SyncAsync(bool scheduled = false, DateOnly? from = null, bool defaultFrom = false, string actor = Actor) =>
        Sync().HandleAsync(new SyncBrokerHistoryCommand(Account.Id, scheduled, defaultFrom ? null : from ?? From, actor), CancellationToken.None).AsTask();

    public async Task<BrokerHistoryRunView> SyncOkAsync(bool scheduled = false, DateOnly? from = null, bool defaultFrom = false) => Ok(await SyncAsync(scheduled, from, defaultFrom));

    public static BrokerTrade Trade(string id, DateOnly date, TradeSide side, long qty, decimal price, string? symbol = "EXMPL", TimeOnly? time = null, string? isin = null, decimal charges = 0m, string? securityId = "1001") =>
        new(id, "O-" + id, side, symbol, isin, securityId, qty, price, charges, date, time ?? new TimeOnly(10, 15, 2));

    public static BrokerLedgerRow Line(DateOnly date, string voucher, string description, string narration, decimal amount, decimal? balance = null) =>
        new(date, voucher, description, narration, "NSE-CAPITAL", amount, balance);

    /// <summary>The trades of the base tradebook as Dhan reports them: buy 10 at 90, buy 5 at 110 and sell 3 at 130, so the broker's 30 leave 18 shares as the opening lot.</summary>
    public static BrokerTrade[] BaseTrades() =>
    [
        Trade("T1", new DateOnly(2026, 9, 2), TradeSide.Buy, 10, 90m),
        Trade("T2", new DateOnly(2026, 9, 15), TradeSide.Buy, 5, 110m),
        Trade("T3", new DateOnly(2026, 9, 20), TradeSide.Sell, 3, 130m),
    ];

    /// <summary>The runs of a kind, oldest first.</summary>
    public List<BrokerSyncRun> RunsOf(BrokerSyncKind kind) => [.. Data.Runs.Where(r => r.Kind == kind).OrderBy(r => r.StartedAt).ThenBy(r => r.Id)];

    /// <summary>The trades Dhan's history stored.</summary>
    public List<Transaction> BrokerTrades() => [.. Fx.Trades.Trades.Where(t => t.Source == TradeSource.Broker)];

    /// <summary>The queue the "Sync now" hands its run to, remembering the tickets.</summary>
    internal sealed class QueueSpy : IBrokerHistoryChannel
    {
        public List<BrokerHistoryTicket> Tickets { get; } = [];

        public void Enqueue(BrokerHistoryTicket ticket) => Tickets.Add(ticket);

        public IAsyncEnumerable<BrokerHistoryTicket> ReadAllAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    /// <summary>A schedule reader that says the service runs next at a fixed time.</summary>
    internal sealed class FixedSchedules : XDrishti.Application.Abstractions.Platform.IServiceScheduleReader
    {
        public static readonly DateTimeOffset Next = new(2026, 10, 12, 16, 30, 0, TimeSpan.FromMinutes(330));

        public ValueTask<XDrishti.Application.Abstractions.Platform.ServiceScheduleInfo?> GetAsync(string serviceId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<XDrishti.Application.Abstractions.Platform.ServiceScheduleInfo?>(
                serviceId == PortfolioBrokerHistoryService.Id ? new XDrishti.Application.Abstractions.Platform.ServiceScheduleInfo(true, new TimeOnly(16, 30), "Every trading day at 16:30 IST", Next) : null);
    }
}
