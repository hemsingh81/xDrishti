using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.DividendImport;
using XDrishti.Application.Portfolio.Trades;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.PortfolioFixture;

namespace XDrishti.Application.Tests.Portfolio.DividendImport;

/// <summary>
/// An account with a Portfolio role and the real dividend import handlers on in-memory ports, with holdings, master shares and aliases the test chooses. Clock: Thursday 2026-10-15 12:00 India. Holdings carry a lot dated 2025-01-01 (the default
/// first-import date) unless the test says otherwise.
/// </summary>
internal sealed class DividendScenario
{
    public PortfolioFixture Fx { get; } = new();

    public BrokerAccount Account { get; private set; } = null!;

    public FakeDividendHoldings Holdings { get; } = new();

    public FakeInstrumentNames Names { get; } = new();

    public InMemoryDividendAliases Aliases { get; } = new();

    public InMemoryDividendBatches Batches { get; private set; } = null!;

    public DividendWorkspace Workspace => new(Fx.Accounts, Holdings, Names, Aliases, Fx.Dividends);

    public static async Task<DividendScenario> StartAsync()
    {
        var scenario = new DividendScenario();
        scenario.Account = await scenario.Fx.ConnectedAsync();
        scenario.Fx.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 15, 6, 30, 0, TimeSpan.Zero));
        scenario.Batches = new InMemoryDividendBatches(scenario.Fx.Dividends);

        // The database rolls back what a refused save had collected, and keeps what a good one saved: the batches and aliases follow the dividends.
        var saved = scenario.Fx.Data.OnSaved;
        var refused = scenario.Fx.Data.OnRefusedSave;
        scenario.Fx.Data.OnSaved = () =>
        {
            saved?.Invoke();
            scenario.Batches.ForgetCollected();
            scenario.Aliases.ForgetCollected();
        };
        scenario.Fx.Data.OnRefusedSave = () =>
        {
            refused?.Invoke();
            scenario.Batches.RollBackCollected();
            scenario.Aliases.RollBackCollected();
        };
        return scenario;
    }

    public HoldingData Hold(string name, string symbol, string? isin = null, DateOnly? acquired = null, bool open = true, Guid? account = null, Guid? instrument = null)
    {
        var holding = DividendHoldings.Held(name, symbol, isin, open, instrument, acquired, account ?? Account.Id);
        Holdings.Holdings.Add(holding);
        return holding;
    }

    public ShareName ListInMaster(string name, string symbol, string? isin = null)
    {
        var share = DividendHoldings.Share(name, symbol, isin);
        Names.Shares.Add(share);
        return share;
    }

    public static TradeFileContent Content(string csv) => new(Encoding.UTF8.GetBytes(csv));

    public Task<Result<DividendPreviewReport>> PreviewAsync(string csv, Guid? accountId = null, string fileName = "dividends.csv") =>
        new PreviewDividendsHandler(Workspace, Fx.Clock, NullLogger<PreviewDividendsHandler>.Instance)
            .HandleAsync(new PreviewDividendsQuery(accountId ?? Account.Id, fileName, Content(csv)), CancellationToken.None).AsTask();

    public async Task<DividendPreviewReport> PreviewOkAsync(string csv) => Ok(await PreviewAsync(csv));

    /// <summary>Imports, sending the digest of a fresh preview unless one is given.</summary>
    public async Task<Result<DividendImportResult>> ImportAsync(
        string csv,
        IReadOnlyList<DividendChoice>? choices = null,
        IReadOnlyList<int>? includeDuplicates = null,
        string? digest = null,
        string fileName = "dividends.csv",
        Guid? accountId = null)
    {
        digest ??= (await PreviewAsync(csv, accountId)).ValueOrDefault()?.Digest;
        var handler = new ImportDividendsHandler(Workspace, Fx.Dividends, Batches, Aliases, Fx.Accounts, Fx.Data, Fx.Clock, NullLogger<ImportDividendsHandler>.Instance);
        return await handler.HandleAsync(
            new ImportDividendsCommand(accountId ?? Account.Id, fileName, Content(csv), digest, choices ?? [], includeDuplicates ?? [], Actor),
            CancellationToken.None);
    }

    public async Task<DividendImportResult> ImportOkAsync(string csv, IReadOnlyList<DividendChoice>? choices = null, IReadOnlyList<int>? includeDuplicates = null) =>
        Ok(await ImportAsync(csv, choices, includeDuplicates));

    public Task<Result<DividendUndoResult>> UndoAsync(Guid batchId) =>
        new UndoDividendBatchHandler(Batches, Fx.Dividends, Fx.Accounts, Fx.Accounts, Fx.Data, Fx.Clock, NullLogger<UndoDividendBatchHandler>.Instance)
            .HandleAsync(new UndoDividendBatchCommand(batchId, Actor), CancellationToken.None).AsTask();

    public Task<Result<DividendBatchPage>> ListBatchesAsync(Guid? accountId = null, int? page = null, int? pageSize = null) =>
        new ListDividendBatchesHandler(Batches, Fx.Accounts).HandleAsync(new ListDividendBatchesQuery(accountId, page, pageSize), CancellationToken.None).AsTask();

    /// <summary>A dividend the owner typed in for a holding (stored already).</summary>
    public Dividend AddManual(Guid? holdingId, DateOnly date, decimal amount)
    {
        var dividend = Dividend.Create(Account.Id, holdingId, null, null, "SYM", date, amount, null, null, Actor, Fx.Clock.GetUtcNow());
        Fx.Dividends.Add(dividend);
        Fx.Dividends.ForgetCollected();
        return dividend;
    }

    /// <summary>The dividends the account stores that are not voided.</summary>
    public List<Dividend> Active() => [.. Fx.Dividends.Dividends.Where(d => !d.IsVoided)];
}

internal static class ResultExtensions
{
    public static T? ValueOrDefault<T>(this Result<T> result)
        where T : class => result.IsSuccess ? result.Value : null;
}
