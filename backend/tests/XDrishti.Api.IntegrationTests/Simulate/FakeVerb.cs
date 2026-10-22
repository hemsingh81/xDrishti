using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Market.Baskets;
using XDrishti.Application.Market.Search;
using XDrishti.Application.Market.Tracking;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Replay;
using XDrishti.Tests.Shared;

namespace XDrishti.Api.IntegrationTests.Simulate;

/// <summary>A query handler made of a function: it records the queries it was given.</summary>
internal sealed class FuncHandler<TQuery, TResult>(Func<TQuery, TResult> handle) : IQueryHandler<TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    public ConcurrentQueue<TQuery> Received { get; } = new();

    public ValueTask<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Received.Enqueue(query);
        return ValueTask.FromResult(handle(query));
    }
}

/// <summary>One log line the verb or a fake wrote: the event id, the level and the arguments of the message.</summary>
internal sealed record CapturedLine(int EventId, LogLevel Level, string Text, IReadOnlyList<KeyValuePair<string, object?>> Arguments);

/// <summary>A logger factory that keeps every line (event id, level, arguments, text): the verb's own lines are checked against the spec's list.</summary>
internal sealed class CapturingLoggerFactory : ILoggerFactory
{
    public ConcurrentQueue<CapturedLine> Lines { get; } = new();

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public ILogger CreateLogger(string categoryName) => new Capture(Lines);

    public void Dispose()
    {
    }

    private sealed class Capture(ConcurrentQueue<CapturedLine> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var arguments = state is IReadOnlyList<KeyValuePair<string, object?>> list ? list : [];
            lines.Enqueue(new CapturedLine(eventId.Id, logLevel, formatter(state, exception) + (exception is null ? string.Empty : " " + exception), arguments));
        }
    }
}

/// <summary>
/// The services of the verb made by hand (no database): the handlers are functions, the engine is the real catalog with no replay, the clock is the system's. The run the handler returns is a fabricated one assembled by the real assembler.
/// What the test needs to see is recorded: the queries the handlers were given and the lines logged.
/// </summary>
internal sealed class FakeVerb : IDisposable
{
    private readonly List<ResolvedInstrument> _matched = [];

    public FakeVerb()
    {
        Configuration = new Dictionary<string, string?>
        {
            ["Database:ConnectionString"] = "Host=db.example.invalid;Port=5433;Database=xdrishti;Username=xd_app",
            ["Database:Password"] = Password,
        };
    }

    /// <summary>A password made for this test only (never a real one): the scans look for it in everything the verb prints, logs or writes.</summary>
    public string Password { get; } = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12));

    public TempFolder Root { get; } = new();

    public StringWriter Out { get; } = new();

    public StringWriter Error { get; } = new();

    public CapturingLoggerFactory Logs { get; } = new();

    /// <summary>The instruments by id (what the instrument query answers).</summary>
    public Dictionary<Guid, InstrumentDetail> Instruments { get; } = [];

    /// <summary>What the resolve query answers.</summary>
    public Func<ResolveInstrumentsQuery, Result<ResolveResult>> Resolve { get; set; } = q => Result.Success(new ResolveResult([], [.. q.Input.Symbols!.Select(s => new UnmatchedEntry(s, "not_found"))], q.Input.Symbols!.Count, 0));

    public List<BasketView> Baskets { get; } = [];

    public Dictionary<Guid, BasketMembers> Members { get; } = [];

    /// <summary>What the run handler answers; by default the fabricated standard run.</summary>
    public Func<RunSimulationQuery, Result<SimulationRun>> Run { get; set; } = _ => Result.Success(FabricatedRun.Standard());

    /// <summary>An exception the run handler throws instead of answering.</summary>
    public Func<Exception?> RunThrows { get; set; } = () => null;

    /// <summary>A writer registered in the services (a test gives one whose rename can be observed); null: the verb makes its own.</summary>
    public ExportFolderWriter? Writer { get; set; }

    public FuncHandler<RunSimulationQuery, Result<SimulationRun>> RunHandler { get; private set; } = null!;

    /// <summary>What the second read for the trade cards answers; by default a dense flat day for each key (the run is fabricated, so there are no stored bars to read).</summary>
    public Func<BuildTradeCardsQuery, Result<IReadOnlyList<TradeCardData>>> Cards { get; set; } = FlatDays;

    public FuncHandler<BuildTradeCardsQuery, Result<IReadOnlyList<TradeCardData>>> CardsHandler { get; private set; } = null!;

    public FuncHandler<GetInstrumentQuery, Result<InstrumentDetail>> InstrumentHandler { get; private set; } = null!;

    public Dictionary<string, string?> Configuration { get; }

    public string ExportRoot => Root.Root;

    /// <summary>A flat dense day (09:15 to 15:29) at the entry price of each chosen trade, with the 09:15 bar as the opening bar.</summary>
    public static Result<IReadOnlyList<TradeCardData>> FlatDays(BuildTradeCardsQuery query)
    {
        IReadOnlyList<TradeCardData> cards =
        [
            .. query.TradeKeys.Select(key =>
            {
                var trade = query.Run.Trades.Single(t => t.Trade.Key == key);
                var bars = Day.Dense(trade.Trade.TradeDate).Start(trade.Trade.EntryPrice).Bars();
                return new TradeCardData(trade, query.Run.Instruments.Single(i => i.InstrumentId == trade.Trade.InstrumentId), bars, bars[0], null);
            }),
        ];
        return Result.Success(cards);
    }

    public static InstrumentDetail Detail(Guid id, string symbol, string type = "share", decimal? tick = 5m) =>
        new(id, symbol, "NSE", "NSE_EQ", type, symbol, symbol, null, "EQ", 1, tick, null, symbol, null, null, false, false, null, null, null, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, [], TrackedInfo.Never);

    public static InstrumentSummary Summary(Guid id, string symbol, string type = "share", decimal? tick = 5m, bool retired = false) =>
        new(id, symbol, symbol, symbol, type, null, "EQ", null, 1, tick, null, false, false, retired, TrackedInfo.Never);

    /// <summary>Adds an instrument the resolve query matches (in the order added) and the instrument query answers.</summary>
    public Guid AddInstrument(string symbol, string type = "share", decimal? tick = 5m, bool retired = false)
    {
        var id = Guid.CreateVersion7();
        Instruments[id] = Detail(id, symbol, type, tick);
        _matched.Add(new ResolvedInstrument(symbol, id, symbol, symbol, type, null, retired, false));
        Resolve = _ => Result.Success(new ResolveResult([.. _matched], [], _matched.Count, 0));
        return id;
    }

    public BasketView AddBasket(string name, string kind = "manual", params (string Symbol, string Type, decimal? Tick, bool Retired)[] members)
    {
        var id = Guid.CreateVersion7();
        var view = new BasketView(id, name, "#000000", "trading", null, 0, members.Length, DateTimeOffset.UnixEpoch, null, kind, kind == "portfolio", null, null);
        Baskets.Add(view);
        Members[id] = new BasketMembers(
            id,
            name,
            null,
            null,
            members.Length,
            [.. members.Select((m, i) => new BasketMemberView(Guid.CreateVersion7(), Summary(Guid.CreateVersion7(), m.Symbol, m.Type, m.Tick, m.Retired), i, DateTimeOffset.UnixEpoch, null, null, "owner", null, null))]);
        return view;
    }

    public IServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISimulationEngine>(new CatalogEngine());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ILoggerFactory>(Logs);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(Configuration).Build());
        services.AddSingleton<IQueryHandler<ResolveInstrumentsQuery, Result<ResolveResult>>>(new FuncHandler<ResolveInstrumentsQuery, Result<ResolveResult>>(q => Resolve(q)));
        InstrumentHandler = new FuncHandler<GetInstrumentQuery, Result<InstrumentDetail>>(
            q => Instruments.TryGetValue(q.Id, out var d) ? Result.Success(d) : Result.Failure<InstrumentDetail>(XDrishti.Domain.Common.Error.NotFound("market.not_found", "no such instrument")));
        services.AddSingleton<IQueryHandler<GetInstrumentQuery, Result<InstrumentDetail>>>(InstrumentHandler);
        services.AddSingleton<IQueryHandler<ListBasketsQuery, IReadOnlyList<BasketView>>>(new FuncHandler<ListBasketsQuery, IReadOnlyList<BasketView>>(_ => [.. Baskets]));
        services.AddSingleton<IQueryHandler<GetBasketMembersQuery, Result<BasketMembers>>>(
            new FuncHandler<GetBasketMembersQuery, Result<BasketMembers>>(
                q => Members.TryGetValue(q.BasketId, out var m) ? Result.Success(m) : Result.Failure<BasketMembers>(XDrishti.Domain.Common.Error.NotFound("baskets.not_found", "no such basket"))));
        RunHandler = new FuncHandler<RunSimulationQuery, Result<SimulationRun>>(q => RunThrows() is { } failure ? throw failure : Run(q));
        services.AddSingleton<IQueryHandler<RunSimulationQuery, Result<SimulationRun>>>(RunHandler);
        CardsHandler = new FuncHandler<BuildTradeCardsQuery, Result<IReadOnlyList<TradeCardData>>>(q => Cards(q));
        services.AddSingleton<IQueryHandler<BuildTradeCardsQuery, Result<IReadOnlyList<TradeCardData>>>>(CardsHandler);
        if (Writer is not null)
        {
            services.AddSingleton(Writer);
        }

        return services.BuildServiceProvider();
    }

    /// <summary>Runs the verb with the arguments (split on spaces) and the export root of this fake.</summary>
    public Task<int> RunAsync(string args, CancellationToken cancellationToken) => RunInRootAsync(args, ExportRoot, cancellationToken);

    /// <summary>
    /// Runs the verb with the given export root (null, relative or missing included). The cost choice is required since P3-03f: a command line that names neither <c>--gross</c> nor <c>--costs</c> gets <c>--gross</c> here, because these tests are
    /// about the other flags; <see cref="RunRawAsync"/> sends the line as it is.
    /// </summary>
    public async Task<int> RunInRootAsync(string args, string? root, CancellationToken cancellationToken) =>
        await RunRawAsync(WithCosts(args), root, cancellationToken);

    /// <summary>Runs the verb with exactly these arguments.</summary>
    public async Task<int> RunRawAsync(string args, string? root, CancellationToken cancellationToken) =>
        await SimulateCommand.RunAsync(args.Split(' ', StringSplitOptions.RemoveEmptyEntries), Services(), new SimulateConsole(Out, Error), root, cancellationToken);

    private static string WithCosts(string args) =>
        args.Contains("--gross", StringComparison.Ordinal) || args.Contains("--costs", StringComparison.Ordinal) || args.Contains("--list-strategies", StringComparison.Ordinal) ? args : args + " --gross";

    public void Dispose() => Root.Dispose();

    /// <summary>The engine port with the real catalog and no replay: the verb asks it for the catalog only.</summary>
    private sealed class CatalogEngine : ISimulationEngine
    {
        public string EngineVersion => "sim-3";

        public IReadOnlyList<BuiltInStrategyInfo> BuiltIns => BuiltInStrategies.Catalog;

        public Result<IPreparedStrategy> Prepare(string strategyId, IReadOnlyDictionary<string, double> parameters, XDrishti.Domain.Market.Bars.Timeframe evaluationTimeframe, FillRules? fills, ExitChoice? exits = null) =>
            throw new NotSupportedException("the handler is a fake in these tests");

        public Result<IPreparedStrategy> PrepareStored(string sourceText, string expectedHash, IReadOnlyDictionary<string, double> parameters, XDrishti.Domain.Market.Bars.Timeframe evaluationTimeframe, FillRules? fills, ExitChoice? exits = null) =>
            throw new NotSupportedException("the handler is a fake in these tests");

        public Result<StrategyDescription> Describe(string sourceText) => throw new NotSupportedException("the handler is a fake in these tests");

        public ComparisonReplay ReplayComparison(IPreparedStrategy strategy, InstrumentReplayInput input) => throw new NotSupportedException("the handler is a fake in these tests");

        public InstrumentReplay Replay(IPreparedStrategy strategy, InstrumentReplayInput input) => throw new NotSupportedException("the handler is a fake in these tests");
    }
}
