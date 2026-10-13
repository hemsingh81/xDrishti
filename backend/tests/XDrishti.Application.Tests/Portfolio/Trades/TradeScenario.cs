using System.Text;
using XDrishti.Application.Portfolio;
using XDrishti.Application.Portfolio.Trades;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.PortfolioFixture;

namespace XDrishti.Application.Tests.Portfolio.Trades;

/// <summary>
/// An account with a Portfolio role and one holding imported with the default date (EXMPL, 30 shares at 100, ISIN INE000A01011), and helpers to send tradebooks through the real handlers on in-memory ports.
/// The clock is Saturday 2026-10-10 14:30 India. The base tradebook buys 10 at 90 and 5 at 110 and sells 3 at 130, so the broker's 30 leave 18 shares as the opening lot.
/// </summary>
internal sealed class TradeScenario
{
    public const string Header = "date,symbol,isin,side,quantity,price,trade id";

    public const string Isin = "INE000A01011";

    public static readonly string[] BaseRows =
    [
        "2024-03-05,EXMPL,INE000A01011,buy,10,90,T1",
        "2024-06-12,EXMPL,INE000A01011,buy,5,110,T2",
        "2024-09-20,EXMPL,INE000A01011,sell,3,130,T3",
    ];

    public PortfolioFixture Fx { get; } = new();

    public BrokerAccount Account { get; private set; } = null!;

    public Holding Holding => Fx.HoldingOf("EXMPL");

    public InMemoryTradeData Data => Fx.Trades;

    public static async Task<TradeScenario> StartAsync(long qty = 30, decimal avgCost = 100m)
    {
        var scenario = new TradeScenario();
        scenario.Account = await scenario.Fx.ConnectedAsync();
        scenario.Fx.Dhan.Rows.Add(Row("EXMPL", qty, avgCost, 120m, "1001", Isin));
        Ok(await scenario.Fx.ImportAsync(scenario.Account.Id, new ImportInput(null, null, All: true)));
        return scenario;
    }

    public static string Csv(params string[] rows) => string.Join('\n', [Header, .. rows]) + "\n";

    public static TradeFileContent Content(string csv) => new(Encoding.UTF8.GetBytes(csv));

    public Task<Result<TradePreviewReport>> PreviewAsync(string csv, Guid? accountId = null) =>
        Fx.PreviewTrades().HandleAsync(new PreviewTradesQuery(accountId ?? Account.Id, "tradebook.csv", Content(csv)), CancellationToken.None).AsTask();

    public async Task<TradePreviewReport> PreviewOkAsync(string csv) => Ok(await PreviewAsync(csv));

    /// <summary>Imports with a rebuild, sending the digest of a fresh preview unless one is given.</summary>
    public async Task<Result<TradeImportResult>> ImportAsync(string csv, bool rebuild = true, string? digest = null, string fileName = "tradebook.csv")
    {
        if (rebuild && digest is null)
        {
            digest = (await PreviewOkAsync(csv)).Digest;
        }

        return await Fx.ImportTrades().HandleAsync(new ImportTradesCommand(Account.Id, fileName, Content(csv), rebuild, digest, Actor), CancellationToken.None);
    }

    public async Task<TradeImportResult> ImportOkAsync(string csv, bool rebuild = true) => Ok(await ImportAsync(csv, rebuild));

    public Task<Result<TradeUndoResult>> UndoAsync(Guid batchId) =>
        Fx.UndoTradeBatch().HandleAsync(new UndoTradeBatchCommand(batchId, Actor), CancellationToken.None).AsTask();

    public Task<Result<HoldingRebuildResult>> RebuildAsync(bool dryRun, Guid? holdingId = null) =>
        Fx.RebuildLots().HandleAsync(new RebuildLotsCommand(holdingId ?? Holding.Id, dryRun, Actor), CancellationToken.None).AsTask();

    public static IReadOnlyList<(DateOnly Date, long Qty, decimal Cost, LotSource Source, bool Default, DateOnly? ClosedOn, decimal? ClosePrice, bool Estimated)> Shape(Holding holding) =>
    [
        .. holding.Lots
            .OrderBy(l => l.AcquiredOn).ThenBy(l => l.ClosedOn).ThenBy(l => l.Qty)
            .Select(l => (l.AcquiredOn, l.Qty, l.UnitCost, l.Source, l.UsesDefaultDate, l.ClosedOn, l.ClosePrice, l.Estimated)),
    ];

    public decimal OpenCost() => Holding.Lots.Where(l => l.IsOpen).Sum(l => l.Qty * l.UnitCost);

    public long OpenQty() => Holding.Lots.Where(l => l.IsOpen).Sum(l => l.Qty);
}
