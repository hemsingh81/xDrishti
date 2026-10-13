using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Accounts;
using XDrishti.Application.Portfolio;
using XDrishti.Application.Portfolio.Refresh;
using XDrishti.Application.Tests.Accounts;
using XDrishti.Application.Tests.Accounts.Connections;
using XDrishti.Application.Tests.Portfolio.Groups;
using XDrishti.Application.Tests.Portfolio.History;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Tests.Portfolio;

/// <summary>
/// The holdings handlers on in-memory ports, a scripted Dhan and a controllable clock (start: 2026-10-10 14:30 IST, a Saturday). The accounts side is
/// the real handlers' work (an account with the Portfolio role, connected through the sign-in flow), so the vault holds a real stored token.
/// </summary>
internal sealed class PortfolioFixture
{
    public const string Actor = AccountsFixture.Actor;

    public ConnectionsFixture Connections { get; } = new();

    public FakeBrokerHoldings Dhan => Connections.Accounts.HoldingsDhan;

    public FakeInstrumentMapper Instruments => Connections.Accounts.HoldingsInstruments;

    public InMemoryPortfolioData Data => Connections.Accounts.PortfolioData;

    public FakeTimeProvider Clock => Connections.Clock;

    public InMemoryAccountData Accounts => Connections.Data;

    /// <summary>The owner's calendar day (India) at the fixture's clock.</summary>
    public DateOnly Today => XDrishti.Domain.Market.IndiaTime.Today(Clock.GetUtcNow());

    public HoldingsFetcher Fetcher => Connections.Accounts.Fetcher;

    public SyncRuns Runs => Connections.Accounts.Runs;

    public PreviewHoldingsHandler Preview() => new(Fetcher, Data);

    public HoldingsImporter Importer => Connections.Accounts.Importer;

    public SyncHoldingsHandler Sync() => new(Fetcher, Runs, Importer, Data, Data, Accounts, Clock, NullLogger<SyncHoldingsHandler>.Instance);

    /// <summary>The master's sectors and the baskets' members of the holdings' instruments (empty: every holding is unclassified and in no basket).</summary>
    public FakeHoldingClassifications Classifications { get; } = new();

    public ListHoldingsHandler List() => new(Data, Classifications, Clock);

    public GetHoldingGroupsHandler Groups() => new(Data, Classifications, Clock, NullLogger<GetHoldingGroupsHandler>.Instance);

    /// <summary>The benchmark setting and the stored day closes the summary's benchmark XIRR reads (empty: no benchmark index).</summary>
    public FakePortfolioBenchmark Benchmarks { get; } = new();

    public FakePortfolioPrices Prices { get; } = new();

    public GetHoldingsSummaryHandler Summary() => new(Data, Data, Accounts, Benchmarks, Prices, new FakeTradingCalendar(HistoryData.Weekdays), Clock);

    public GetHoldingLotsHandler Lots() => new(Data, Clock);

    public UpdateLotDateHandler UpdateLotDate() => new(Data, Data, Accounts, Accounts, Data, Clock, NullLogger<UpdateLotDateHandler>.Instance);

    public GetSyncStatusHandler SyncStatus() => new(Accounts, Data, Data, Clock);

    public RecoverInterruptedSyncsHandler Recover() => new(Data, Clock, NullLogger<RecoverInterruptedSyncsHandler>.Instance);

    /// <summary>An account with the Portfolio role on (so it has its default bucket), connected to Dhan.</summary>
    public async Task<BrokerAccount> ConnectedAsync(string name = "Example One", string clientId = AccountsFixture.ClientId, bool connect = true, PortfolioInput? portfolio = null)
    {
        Connections.Dhan.HolderId = clientId;
        var account = await Connections.Accounts.CreatedAsync(Inputs.Account(name: name, clientId: clientId, roles: Inputs.Roles(false, true), portfolio: portfolio));
        if (connect)
        {
            await Connections.ConnectByLoginAsync(account);
        }

        return account;
    }

    public Task<Result<ImportPreview>> PreviewAsync(Guid accountId) =>
        Preview().HandleAsync(new PreviewHoldingsQuery(accountId), CancellationToken.None).AsTask();

    /// <summary>The import the first-import paths use, driven directly.</summary>
    public async Task<Result<ImportResultView>> ImportAsync(Guid accountId, ImportInput input)
    {
        var outcome = await Importer.ImportAsync(accountId, input, Actor, CancellationToken.None);
        return outcome.IsFailure ? Result.Failure<ImportResultView>(outcome.Error) : Result.Success(outcome.Value.Result);
    }

    public Task<Result<SyncRunView>> SyncAsync(Guid accountId, SyncTrigger trigger = SyncTrigger.Manual, bool confirm = false, int? expectedOpen = null, int? expectedWouldClose = null) =>
        Sync().HandleAsync(new SyncHoldingsCommand(accountId, trigger, trigger == SyncTrigger.Auto ? PortfolioRefreshRunner.Requester : Actor, confirm, expectedOpen, expectedWouldClose), CancellationToken.None).AsTask();

    /// <summary>Imports every row Dhan reports, all on the same date.</summary>
    public async Task ImportAllAsync(Guid accountId, DateOnly date)
    {
        var result = await ImportAsync(accountId, new ImportInput(date, [.. BrokerHolding.Merge(Dhan.Rows).Select(r => new ImportRowInput(r.Key, date))]));
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
    }

    public static BrokerHolding Row(string symbol = "EXMPL", long qty = 100, decimal avgCost = 50m, decimal price = 60m, string securityId = "1001", string? isin = "INE000A01011") =>
        new("ALL", securityId, isin, symbol, qty, avgCost, price);

    public Holding HoldingOf(string symbol) => Data.Holdings.Single(h => h.TradingSymbol == symbol);

    public List<AccountEvent> Events(AccountEventType type) => [.. Accounts.Events.Where(e => e.Event == type)];

    public static IReadOnlyList<string> FieldsOf(Result result) => [.. result.Error.Fields?.Select(f => f.Field) ?? []];

    public static T Ok<T>(Result<T> result)
    {
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code + ": " + result.Error.Message : string.Empty);
        return result.Value;
    }
}
