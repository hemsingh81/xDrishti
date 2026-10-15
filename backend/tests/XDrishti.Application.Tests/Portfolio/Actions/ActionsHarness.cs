using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio;
using XDrishti.Application.Portfolio.Actions;
using XDrishti.Application.Portfolio.Alerts;
using XDrishti.Application.Portfolio.Attention;
using XDrishti.Application.Tests.Portfolio.Groups;
using XDrishti.Application.Tests.Portfolio.History;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Tests.Portfolio.Actions;

/// <summary>
/// The Action-center and price-alert handlers over made-up holdings, a fake master, the benchmark and the stored index closes, and the in-memory stores (clock: Monday 2026-10-12 10:00 India time). One account exists: <see cref="ActionFixtures.AccountOne"/>
/// (and <see cref="ActionFixtures.AccountTwo"/>, which does not count in totals only through the stub's filters).
/// </summary>
internal sealed class ActionsHarness
{
    public const string Owner = "owner@example.com";

    public ActionsHarness(params HoldingData[] holdings)
    {
        Queries = new StubHoldingQueries(holdings);
        Data.Cache = Cache;
        Clock.SetUtcNow(new DateTimeOffset(2026, 10, 12, 4, 30, 0, TimeSpan.Zero));
        var account = BrokerAccount.Create("Example One", "1000000001", null, null, null, ActionFixtures.Today, Clock.GetUtcNow()).Value;
        Accounts = new KnownAccounts(AccountIds, account);
    }

    public StubHoldingQueries Queries { get; }

    public FakeHoldingClassifications Classifications { get; } = new();

    public InMemoryActionData Data { get; } = new();

    public FakeHistoryCache Cache { get; } = new();

    public FakePortfolioBenchmark Benchmarks { get; } = new();

    public FakePortfolioPrices Prices { get; } = new();

    public FakeTimeProvider Clock { get; } = new();

    public TradingCalendar Calendar { get; set; } = HistoryData.Weekdays;

    public FakeTradingCalendar Trading => new(Calendar);

    /// <summary>The accounts that exist for the scope check of the read (both fixture accounts).</summary>
    public HashSet<Guid> AccountIds { get; } = [ActionFixtures.AccountOne, ActionFixtures.AccountTwo];

    public IAccountStore Accounts { get; }

    public ActionEvaluator Evaluator() => new(Queries, Classifications, Data, Data, Benchmarks, Prices, Trading, Clock);

    public ActionReconciler Reconciler() => new(Evaluator(), Data, Clock, NullLogger<ActionReconciler>.Instance);

    public GetActionItemsHandler Get() => new(Evaluator(), Reconciler(), Accounts, Clock, NullLogger<GetActionItemsHandler>.Instance);

    public ActionDecisions Decisions() => new(Evaluator(), Data, Data, Data, Clock, NullLogger<ActionDecisions>.Instance);

    public ActionThresholdsWriter Writer() => new(Data, Data, Data, Reconciler(), Clock, NullLogger<ActionThresholdsWriter>.Instance);

    public PriceAlertChecker Checker() => new(Data, Data, Data, NullLogger<PriceAlertChecker>.Instance);

    public CreatePriceAlertHandler Create() => new(Data, Trading, Data, Data, Clock, NullLogger<CreatePriceAlertHandler>.Instance);

    public CancelPriceAlertHandler Cancel() => new(Data, Data, Data, Clock, NullLogger<CancelPriceAlertHandler>.Instance);

    public ListPriceAlertsHandler Alerts() => new(Data);

    public GetPortfolioNoticesHandler Notices() => new(Data, Evaluator(), Cache, AttentionStore, Clock, NullLogger<GetPortfolioNoticesHandler>.Instance);

    /// <summary>The writer of the attention items, over the same facts and memory as <see cref="Notices"/> reads (ADR 0030).</summary>
    public AttentionReconciler AttentionReconciler() => new(AttentionFacts, AttentionStore, HealSettings, Clock, NullLogger<AttentionReconciler>.Instance);

    /// <summary>The memory of the attention items; it lives as long as the harness, so a second read sees what the first remembered.</summary>
    public XDrishti.Application.Tests.Portfolio.Heal.InMemoryAttentionStore AttentionStore { get; } = new();

    public XDrishti.Application.Tests.Portfolio.Heal.FixedHealSettings HealSettings { get; } = new();

    /// <summary>The facts the attention items are derived from: none, unless a test adds some.</summary>
    public XDrishti.Application.Tests.Portfolio.Heal.StaticAttentionFacts AttentionFacts => _attentionFacts ??= new(Clock);

    private XDrishti.Application.Tests.Portfolio.Heal.StaticAttentionFacts? _attentionFacts;

    public DateTimeOffset Now => Clock.GetUtcNow();

    public async Task<ActionList> ListAsync(string? status = null, Guid? account = null, Guid? bucket = null)
    {
        var result = await Get().HandleAsync(new GetActionItemsQuery(new ActionItemsRequest(account, bucket, status)), CancellationToken.None);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code + ": " + result.Error.Message : string.Empty);
        return result.Value;
    }

    public async Task<Result<ActionDecisionView>> SnoozeAsync(string id) =>
        await new SnoozeActionHandler(Decisions()).HandleAsync(new SnoozeActionCommand(id, Owner), CancellationToken.None);

    public async Task<Result<ActionDecisionView>> DoneAsync(string id) =>
        await new MarkActionDoneHandler(Decisions()).HandleAsync(new MarkActionDoneCommand(id, Owner), CancellationToken.None);

    public async Task<Result<ActionDecisionView>> ReopenAsync(string id) =>
        await new ReopenActionHandler(Decisions()).HandleAsync(new ReopenActionCommand(id, Owner), CancellationToken.None);

    public void Advance(TimeSpan span) => Clock.Advance(span);

    /// <summary>The holding with a new last price (the quote of a tick).</summary>
    public void Reprice(string symbol, decimal price)
    {
        var index = Queries.Holdings.FindIndex(h => h.TradingSymbol == symbol);
        Queries.Holdings[index] = Queries.Holdings[index] with { LastPrice = price };
    }
}
