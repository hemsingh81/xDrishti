using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Market.Baskets;
using XDrishti.Application.Market.Baskets;
using XDrishti.Application.Tests.Market.Baskets.Systems;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Baskets;
using XDrishti.Domain.Market.Baskets.Lists;

namespace XDrishti.Application.Tests.Market.Baskets.Lists;

/// <summary>
/// The list baskets of BK-05 over fakes: a master of shares, the catalogue rows of the ban file, the lot file and the master-derived lists, the fetch handler and the refresh handler. The clock is 2026-10-26 19:05 IST
/// (the evening the ban file for the next trading day is published).
/// </summary>
internal sealed class ListWorld
{
    public const string BanFirstLine = "Securities in Ban For Trade Date 27-OCT-2026:";

    public const string LotHeader = "UNDERLYING      ,SYMBOL    ,OCT-26   ,NOV-26   ,DEC-26   ,";

    public FakeFetchSettings Settings { get; } = new();

    public FakeListSettings ListSettings { get; } = new();

    public FakeSourceStore Store { get; } = new();

    public FakeFetcher Fetcher { get; } = new();

    public FakeApplier Applier { get; } = new();

    public FakeLotApplier LotApplier => _lotApplier ??= new(Store);

    private FakeLotApplier? _lotApplier;

    public FakeRunLock RunLock { get; } = new();

    public InMemoryBasketData Baskets { get; } = new(new InMemoryTrackingData());

    public FakeMasterReader Master { get; } = new();

    public XDrishti.Application.Abstractions.Market.Lists.IListDayStore Days { get; } = NSubstitute.Substitute.For<XDrishti.Application.Abstractions.Market.Lists.IListDayStore>();

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 26, 19, 5, 0, TimeSpan.FromMinutes(330)));

    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static Guid BanId => ListBaskets.Ban.Id;

    public static Guid LotsId => ListBaskets.LotSizes.Id;

    public RunBasketFetchHandler Fetch => new(Settings, Store, Fetcher, Baskets, Applier, RunLock, ListSettings, new ListFileProcessor(Store, Baskets, Master), LotApplier, Clock, NullLogger<RunBasketFetchHandler>.Instance, Days);

    public RefreshFlagListsHandler Refresh => new(Store, Master, Baskets, Applier, Clock, NullLogger<RefreshFlagListsHandler>.Instance);

    /// <summary>A share of the master (also findable by symbol for the file matching).</summary>
    public Guid Share(string symbol, bool fno = false, string? asm = "N", string? category = null, decimal? mtf = null, string series = "EQ", int? lot = null, bool retired = false)
    {
        var id = Guid.CreateVersion7();
        Master.Shares.Add(new MasterShare(new FlagRow(id, series, fno, asm, category, mtf, retired), symbol, lot));
        Store.Master.Add(new MasterInstrument(id, symbol, null, retired ? Clock.GetUtcNow() : null));
        return id;
    }

    /// <summary>The catalogue row of a master-derived list.</summary>
    public BasketSourceInfo FlagSource(ListBasketDefinition definition, int members = 0) =>
        Register(new BasketSourceInfo(definition.Id, definition.Name, "instrument-master", string.Empty, null, 0, 100000, true, "never", null, null, null, null, null, null, 0, members, ListBaskets.MasterFlagsFormat));

    public BasketSourceInfo BanSource(bool enabled = true, DateTimeOffset? checkedAt = null, string? sha = null, int min = 0, int max = 200, DateOnly? knownThrough = null) =>
        Register(new BasketSourceInfo(BanId, ListBaskets.Ban.Name, "fo_secban.csv", "https://nsearchives.nseindia.com/content/fo/fo_secban.csv", null, min, max, enabled, "never", null, checkedAt, null, null, sha, null, 0, 0, ListBaskets.BanFormat, knownThrough));

    public BasketSourceInfo LotSource(bool enabled = true, DateTimeOffset? checkedAt = null, int min = 2, int max = 10) =>
        Register(new BasketSourceInfo(LotsId, ListBaskets.LotSizes.Name, "fo_mktlots.csv", "https://nsearchives.nseindia.com/content/fo/fo_mktlots.csv", null, min, max, enabled, "never", null, checkedAt, null, null, null, null, 0, 0, ListBaskets.LotsFormat));

    private BasketSourceInfo Register(BasketSourceInfo source)
    {
        Store.Sources.RemoveAll(s => s.BasketId == source.BasketId);
        Store.Sources.Add(source);
        return source;
    }

    /// <summary>A member that is in the list basket now (since <paramref name="since"/>).</summary>
    public Guid Member(Guid basketId, Guid instrumentId, DateTimeOffset since)
    {
        Baskets.Members.Add(BasketMember.Listed(basketId, instrumentId, Baskets.Members.Count(m => m.BasketId == basketId), since, since, Guid.NewGuid(), "fetch", BasketProvenance.Fetched, null, "seed"));
        return instrumentId;
    }

    public void Serve(string text, int status = 200, string contentType = "text/csv") =>
        Fetcher.Response = new SourceResponse(status, contentType, Encoding.UTF8.GetBytes(text), "\"e\"", null, null);

    public static string Ban(params string[] symbols) => BanFirstLine + "\r\n" + string.Concat(symbols.Select((s, i) => $"{i + 1},{s}\r\n"));

    public static string Lot(params (string Underlying, string Symbol, string Lots)[] rows) =>
        LotHeader + "\r\n" + string.Concat(rows.Select(r => $"{r.Underlying,-18},{r.Symbol,-10},{r.Lots,-10},{r.Lots,-10},{r.Lots,-10},\r\n"));

    public async Task<Result<BasketFetchRun>> RunAsync(Guid? basket = null) =>
        await Fetch.HandleAsync(new RunBasketFetchCommand(basket is null ? null : [basket.Value], false, "system:test"), Ct);

    public async Task<FetchOutcomeView> RunOneAsync(Guid? basket = null)
    {
        var result = await RunAsync(basket);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : null);
        return result.Value.Outcomes.Single();
    }
}
