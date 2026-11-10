using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Market.Baskets;
using XDrishti.Application.Market.Baskets;
using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Application.Tests.Market.Baskets.Systems;

/// <summary>Settings for the tests (defaults of the spec; fetching on unless a test turns it off).</summary>
internal sealed class FakeFetchSettings : IBasketFetchSettings
{
    public bool Enabled { get; set; } = true;

    public string UserAgent { get; set; } = "xDrishti test (owner@example.invalid)";

    public FetchLimits Limits => FetchLimits.Default;

    public int StaleAfterDays => 3;

    public int BackoffHours => 24;

    public IReadOnlyList<int> ReviewMonths => [3, 6, 9, 12];

    public int ReviewFromDay => 20;

    public int ReviewToDay => 7;

    public string Snapshot() => "{}";
}

/// <summary>An in-memory catalogue, master and fetch log; counts what is written.</summary>
internal sealed class FakeSourceStore : IBasketSourceStore
{
    public List<BasketSourceInfo> Sources { get; } = [];

    public List<MasterInstrument> Master { get; } = [];

    public List<(SourceUpdate Source, FetchRecord Fetch)> Recorded { get; } = [];

    public int ListCalls { get; private set; }

    public ValueTask<IReadOnlyList<BasketSourceInfo>> ListAsync(IReadOnlyCollection<Guid>? basketIds, CancellationToken cancellationToken)
    {
        ListCalls++;
        return ValueTask.FromResult<IReadOnlyList<BasketSourceInfo>>([.. Sources.Where(s => basketIds is null || basketIds.Contains(s.BasketId))]);
    }

    public ValueTask<IReadOnlyList<MasterInstrument>> FindInstrumentsAsync(IReadOnlyCollection<string> isins, IReadOnlyCollection<string> symbols, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<MasterInstrument>>([.. Master.Where(m => (m.Isin is not null && isins.Contains(m.Isin)) || symbols.Contains(m.Symbol))]);

    public ValueTask<IReadOnlyList<FetchOutcomeView>> RecentFetchesAsync(Guid basketId, int take, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<FetchOutcomeView>>([.. Recorded.Where(r => r.Fetch.BasketId == basketId).Take(take).Select(r => new FetchOutcomeView(basketId, r.Fetch.FetchedAt, r.Fetch.Outcome, r.Fetch.HttpStatus, r.Fetch.Rows, r.Fetch.Added, r.Fetch.Removed, r.Fetch.Unmatched, r.Fetch.Error, r.Fetch.Forced))]);

    public ValueTask RecordAsync(SourceUpdate source, FetchRecord fetch, CancellationToken cancellationToken)
    {
        Recorded.Add((source, fetch));
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeFetcher : IBasketSourceFetcher
{
    public int Calls { get; private set; }

    public SourceResponse Response { get; set; } = SourceResponse.Unreachable("none");

    public ValueTask<SourceResponse> GetAsync(BasketSourceInfo source, CancellationToken cancellationToken)
    {
        Calls++;
        return ValueTask.FromResult(Response);
    }
}

internal sealed class FakeApplier : IBasketMembershipApplier
{
    public List<MembershipApplication> Applied { get; } = [];

    public bool Succeeds { get; set; } = true;

    public ValueTask<bool> ApplyAsync(MembershipApplication application, CancellationToken cancellationToken)
    {
        Applied.Add(application);
        return ValueTask.FromResult(Succeeds);
    }
}

/// <summary>One index basket of 50 members in the master, a catalogue row for it and the handler over fakes; the clock is 2026-10-23 07:30 IST.</summary>
internal sealed class FetchWorld
{
    public const string Header = "Company Name,Industry,Symbol,Series,ISIN Code";

    public FakeFetchSettings Settings { get; } = new();

    public FakeSourceStore Store { get; } = new();

    public FakeFetcher Fetcher { get; } = new();

    public FakeApplier Applier { get; } = new();

    public InMemoryBasketData Baskets { get; } = new(new InMemoryTrackingData());

    public FakeTimeProvider Clock { get; private set; } = new(new DateTimeOffset(2026, 10, 23, 7, 30, 0, TimeSpan.FromMinutes(330)));

    /// <summary>Moves the clock to 07:30 IST of another day (it can go back: the world is built again).</summary>
    public void On(int year, int month, int day) => Clock = new FakeTimeProvider(new DateTimeOffset(year, month, day, 7, 30, 0, TimeSpan.FromMinutes(330)));

    public Guid BasketId { get; }

    public List<Guid> Current => [.. Baskets.Members.Where(m => m.IsActive).Select(m => m.InstrumentId)];

    public List<Guid> InstrumentIds { get; } = [];

    public FetchWorld(int master = 80)
    {
        for (var i = 1; i <= master; i++)
        {
            var id = Guid.CreateVersion7();
            InstrumentIds.Add(id);
            Store.Master.Add(new MasterInstrument(id, $"S{i:000}", Isin(i), null));
        }

        var basket = Basket.CreateSystem("NSE Nifty 50", BasketKinds.Index, Clock.GetUtcNow().AddYears(-1));
        Baskets.Baskets.Add(basket);
        BasketId = basket.Id;
        Store.Sources.Add(Source());
    }

    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static string Isin(int i) => $"INE{i:000}A01010";

    public BasketSourceInfo Source(bool enabled = true, DateTimeOffset? lastOk = null, DateTimeOffset? next = null, string? sha = null, string? etag = null) =>
        new(BasketId, "NSE Nifty 50", "ind_nifty50list.csv", "https://nsearchives.nseindia.com/content/indices/ind_nifty50list.csv", null, 50, 50, enabled, "never", lastOk, null, etag, null, sha, next, 0, Current.Count);

    public RunBasketFetchHandler Handler => new(Settings, Store, Fetcher, Baskets, Applier, Clock, NullLogger<RunBasketFetchHandler>.Instance);

    /// <summary>The members now: instruments 1 to <paramref name="count"/> of the master.</summary>
    public void Members(int from, int count)
    {
        Baskets.Members.Clear();
        foreach (var id in InstrumentIds.Skip(from - 1).Take(count))
        {
            Baskets.AddMember(Baskets.Baskets[0], id, Clock.GetUtcNow().AddYears(-1));
        }
    }

    /// <summary>A CSV of the master's instruments <paramref name="from"/> to <paramref name="from"/> + count - 1 (unmatched rows use symbols and ISINs that are not in the master).</summary>
    public static string Csv(int from, int count, int unmatched = 0)
    {
        var sb = new StringBuilder(Header).Append("\r\n");
        for (var i = 0; i < count; i++)
        {
            if (i < unmatched)
            {
                sb.Append($"Ghost {i},Misc,GHOST{i},EQ,INE9{i:00}Z01010\r\n");
            }
            else
            {
                var n = from + i - unmatched;
                sb.Append($"Company {n},Misc,S{n:000},EQ,{Isin(n)}\r\n");
            }
        }

        return sb.ToString();
    }

    public void Serve(string csv, int status = 200, string contentType = "text/csv", string? etag = "\"v1\"")
    {
        Fetcher.Response = new SourceResponse(status, contentType, Encoding.UTF8.GetBytes(csv), etag, null, null);
    }

    public static string Sha(string csv) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(csv)));

    public async Task<FetchOutcomeView> RunAsync(bool manual = false, bool force = false)
    {
        var result = await Handler.HandleAsync(new RunBasketFetchCommand(manual ? [BasketId] : null, force, "owner:test@example.invalid"), Ct);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : null);
        return result.Value.Outcomes.Single();
    }
}
