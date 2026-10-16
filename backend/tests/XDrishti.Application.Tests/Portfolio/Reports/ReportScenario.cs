using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio;
using XDrishti.Application.Portfolio.BrokerHistory;
using XDrishti.Application.Portfolio.Reports;
using XDrishti.Application.Portfolio.Trades;
using XDrishti.Application.Tests.Auth;
using XDrishti.Application.Tests.Portfolio.BrokerHistory;
using XDrishti.Application.Tests.Portfolio.DividendImport;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.PortfolioFixture;

namespace XDrishti.Application.Tests.Portfolio.Reports;

/// <summary>
/// An account with a Portfolio role, holdings imported with the default date (the test adds the rows Dhan reports before <see cref="StartAsync"/> imports them), master shares, remembered names and the real report handlers on in-memory ports. Clock: Saturday 2026-10-10
/// 14:30 India (so the reports' dates up to then are not in the future); the made-up reports carry <c>ReportFiles.ClientCode</c> as their client code (the UCC, not the account's Dhan client id); the account remembers none until a test says so (<see cref="Remembers"/>) or an import leaves it.
/// </summary>
internal sealed class ReportScenario
{
    public const string Zenith = "INE904D01014";

    public const string Orion = "INE902B01012";

    public const string Nova = "INE903C01013";

    public const string Kestrel = "INE910K01010";

    public const string Lumen = "INE911L01011";

    public PortfolioFixture Fx { get; } = new();

    public BrokerAccount Account { get; private set; } = null!;

    public FakeInstrumentNames Names { get; } = new();

    public InMemoryDividendAliases Aliases { get; } = new();

    public InMemorySecurityAliases Learned { get; } = new();

    public InMemoryReportClientCodes ClientCodes { get; } = new();

    public InMemoryCostBatches Costs { get; private set; } = null!;

    public FakeSpreadsheets Spreadsheets { get; } = new();

    /// <summary>What Dhan's trade history and ledger say (for the tests that read the broker's history after or before an import).</summary>
    public FakeBrokerHistory History { get; } = new();

    public CapturingLogger<PreviewImportFileHandler> PreviewLog { get; } = new();

    public CapturingLogger<ImportFileHandler> ImportLog { get; } = new();

    public ReportWorkspace Workspace => new(Fx.Accounts, Fx.Data, Names, Aliases, Learned, Spreadsheets);

    public TradeImporter Importer => Fx.TradeImporter();

    public ReportClientCodes CodeCheck => new(ClientCodes);

    public BillService Bills => new(Workspace, Fx.Workspace, Fx.Trades, Importer, Aliases, Learned, Fx.Accounts, CodeCheck, Fx.Clock);

    public DpService CostService => new(Workspace, Fx.BrokerData, Costs, Learned, Fx.Accounts, CodeCheck, Fx.SyncGuard, Fx.Data, Fx.Clock);

    public DematService DematService => new(Workspace, Learned, Fx.Accounts, CodeCheck, Fx.Data, Fx.Clock);

    public ReportPreviewer Previewer => new(Workspace, Bills, CostService, DematService, Fx.PreviewTrades());

    public static async Task<ReportScenario> StartAsync(Action<ReportScenario>? holdings = null)
    {
        var scenario = new ReportScenario();
        scenario.Account = await scenario.Fx.ConnectedAsync();
        scenario.Fx.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero));
        scenario.Costs = new InMemoryCostBatches(scenario.Fx.BrokerData);
        holdings?.Invoke(scenario);
        if (scenario.Fx.Dhan.Rows.Count > 0)
        {
            Ok(await scenario.Fx.ImportAsync(scenario.Account.Id, new ImportInput(null, null, All: true)));
        }

        // The database rolls back what a refused save had collected and keeps what a good one saved: the remembered names and batches follow the trades.
        var saved = scenario.Fx.Data.OnSaved;
        var refused = scenario.Fx.Data.OnRefusedSave;
        scenario.Fx.Data.OnSaved = () =>
        {
            saved?.Invoke();
            scenario.Aliases.ForgetCollected();
            scenario.Learned.ForgetCollected();
            scenario.Costs.ForgetCollected();
            scenario.ClientCodes.ForgetCollected();
        };
        scenario.Fx.Data.OnRefusedSave = () =>
        {
            refused?.Invoke();
            scenario.Aliases.RollBackCollected();
            scenario.Learned.RollBackCollected();
            scenario.Costs.RollBackCollected();
            scenario.ClientCodes.RollBackCollected();
        };
        return scenario;
    }

    /// <summary>"Sync now" that waits: the trades, then the ledger, from the date, with <see cref="History"/> as Dhan.</summary>
    public async Task<BrokerHistoryRunView> SyncHistoryAsync(DateOnly from)
    {
        var pipeline = new BrokerHistoryPipeline(
            new BrokerHistoryReader(History, History),
            new BrokerLotRebuilder(Fx.Workspace, Fx.Data, Fx.Trades, Fx.Data, Fx.Clock, NullLogger<BrokerLotRebuilder>.Instance),
            Fx.BrokerData,
            Fx.Data,
            Fx.Trades,
            Fx.Trades,
            Fx.BrokerData,
            Fx.Accounts,
            Fx.Data,
            Fx.Data.Health,
            Fx.Connections.Accounts.HealthRecorder,
            Fx.HealSettings,
            Fx.Clock,
            NullLogger<BrokerHistoryPipeline>.Instance);
        var handler = new SyncBrokerHistoryHandler(new BrokerHistoryAccess(Fx.Fetcher, Fx.BrokerData, Fx.Clock), pipeline);
        return Ok(await handler.HandleAsync(new SyncBrokerHistoryCommand(Account.Id, false, from, Actor), CancellationToken.None));
    }

    /// <summary>The account already remembers the client code (as an earlier import would have left it).</summary>
    public void Remembers(string clientCode) => ClientCodes.Codes.Add(ReportClientCode.Remember(Account.Id, ReportClientCodes.Hash(clientCode), Fx.Clock.GetUtcNow()));

    /// <summary>The hash the account remembers; null when it remembers none.</summary>
    public string? RememberedHash() => ClientCodes.Codes.FirstOrDefault(c => c.AccountId == Account.Id)?.Hash;

    /// <summary>Adds a row to what Dhan reports as held (imported when the scenario starts).</summary>
    public void Hold(string symbol, string isin, long qty, decimal avgCost, string? securityId = null) =>
        Fx.Dhan.Rows.Add(Row(symbol, qty, avgCost, avgCost, securityId ?? "S" + symbol, isin));

    /// <summary>Lists a share in the instrument master (names for matching and tickers for the trades).</summary>
    public ShareName Master(string name, string symbol, string isin)
    {
        var share = DividendHoldings.Share(name, symbol, isin);
        Names.Shares.Add(share);
        Fx.Trades.Instruments.Add(new TradeInstrument(share.Id, symbol, isin, "share"));
        return share;
    }

    public Task<Result<ImportFilePreview>> PreviewAsync(string text, string fileName = "report.csv", IReadOnlyList<BillChoice>? choices = null, Guid? accountId = null) =>
        new PreviewImportFileHandler(Workspace, Previewer, Fx.Clock, PreviewLog).HandleAsync(new PreviewImportFileQuery(accountId ?? Account.Id, fileName, ReportFiles.Content(text), choices ?? []), CancellationToken.None).AsTask();

    public async Task<ImportFilePreview> PreviewOkAsync(string text, IReadOnlyList<BillChoice>? choices = null) => Ok(await PreviewAsync(text, choices: choices));

    /// <summary>Imports, sending the digest of a fresh preview unless one is given.</summary>
    public async Task<Result<ImportFileResult>> ImportAsync(
        string text, bool rebuild = true, string? digest = null, IReadOnlyList<BillChoice>? choices = null, bool acceptOtherAccount = false, string fileName = "report.csv", bool noDigest = false)
    {
        if (digest is null && !noDigest)
        {
            digest = (await PreviewAsync(text, fileName, choices)).ValueOrDefault()?.Digest;
        }

        var handler = new ImportFileHandler(Workspace, Previewer, Bills, CostService, DematService, CodeCheck, Fx.ImportTrades(), Fx.Clock, ImportLog);
        return await handler.HandleAsync(new ImportFileCommand(Account.Id, fileName, ReportFiles.Content(text), digest, rebuild, choices ?? [], acceptOtherAccount, Actor), CancellationToken.None);
    }

    public async Task<ImportFileResult> ImportOkAsync(string text, bool rebuild = true, IReadOnlyList<BillChoice>? choices = null) => Ok(await ImportAsync(text, rebuild, choices: choices));

    public Task<Result<CostUndoResult>> UndoCostsAsync(Guid batchId) =>
        new UndoCostBatchHandler(Costs, Fx.BrokerData, Fx.Accounts, Fx.Accounts, Fx.Data, Fx.Clock, NullLogger<UndoCostBatchHandler>.Instance).HandleAsync(new UndoCostBatchCommand(batchId, Actor), CancellationToken.None).AsTask();

    public Task<Result<TradeUndoResult>> UndoTradesAsync(Guid batchId) =>
        Fx.UndoTradeBatch().HandleAsync(new UndoTradeBatchCommand(batchId, Actor), CancellationToken.None).AsTask();

    public Task<Result<CostBatchPage>> ListCostsAsync(Guid? accountId = null, int? page = null, int? pageSize = null) =>
        new ListCostBatchesHandler(Costs, Fx.Accounts).HandleAsync(new ListCostBatchesQuery(accountId, page, pageSize), CancellationToken.None).AsTask();

    /// <summary>The trades of the account that are not voided, from any source.</summary>
    public List<Transaction> Active() => [.. Fx.Trades.Trades.Where(t => !t.IsVoided)];

    /// <summary>The cost entries imported and not voided.</summary>
    public List<LedgerEntry> ActiveCosts() => [.. Fx.BrokerData.Entries.Where(e => e.Source == LedgerSource.Import && !e.IsVoided)];

    /// <summary>Everything a response, an audit event or a log line could carry, as text: to look for what must never be in it.</summary>
    public string Disclosed(params object?[] responses) =>
        string.Join('\n', responses.Select(r => JsonSerializer.Serialize(r, r?.GetType() ?? typeof(object)))
            .Concat(Fx.Accounts.Events.Select(e => e.Detail))
            .Concat(PreviewLog.Messages)
            .Concat(ImportLog.Messages));

    /// <summary>The standard book of the tests: master shares for the made-up sample's companies.</summary>
    public void ListSampleCompanies()
    {
        Master("Orion Steel Limited", "ORIONSTL", Orion);
        Master("Nova Oxides & Chemicals Ltd.", "NOVAOX", Nova);
        Master("Zenith Energy Systems Limited", "ZENITHE", Zenith);
        Master("Arcadia Beverages Ltd", "ARCADIA", "INE905E01015");
        Master("Orion Gold ETF", "ORIONGLD", "INF901A01011");
        Master("Terra Dynamics Power Systems Ltd", "TERRADYN", "INE912T01012");
        Master("Kestrel Foods Limited", "KESTREL", Kestrel);
        Master("Lumen Oils Limited", "LUMENOIL", Lumen);
    }
}
