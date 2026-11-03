using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.OptionCosts;
using XDrishti.Application.Abstractions.OptionQuality;
using XDrishti.Application.OptionCosts;
using XDrishti.Domain.Options.Costs;
using XDrishti.Domain.Options.Quality;

namespace XDrishti.Api.IntegrationTests.OptionCosts;

/// <summary>
/// P14-07 on a real TimescaleDB (criteria 3, 7 and 8): a seeded archive of chain snapshots is read through the spread reader for the usable sessions only, the model is built, stored and loaded, and a rebuild with the same inputs writes nothing. The archive is filled by
/// raw SQL of the test; the quality results are a fake that names which sessions are usable, so the unusable session (with a spread that would move every median) shows whether it was read.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class OptionSpreadModelDatabaseTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly DateOnly First = new(2026, 10, 5);
    private static readonly DateOnly Second = new(2026, 10, 6);
    private static readonly DateOnly Unusable = new(2026, 10, 7);

    private const string Clean = "TRUNCATE options.chain_snapshots, research.option_spread_buckets, research.option_spread_models";

    private Guid _nifty;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, Clean);
        _nifty = await SeedAsync("NIFTY", type: "index", dhanId: false);
    }

    public override async ValueTask DisposeAsync()
    {
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, Clean);
        }

        await base.DisposeAsync();
    }

    /// <summary>Three strikes (the ATM and one step each side), both sides, one snapshot at 11:00 India time, expiring the next day, with the given bid and ask.</summary>
    private async Task SeedSessionAsync(DateOnly day, int bid, int ask)
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(
            connection,
            """
            INSERT INTO options.chain_snapshots (ts, underlying_id, expiry, strike_px, side, rx_at, security_id, underlying_px, last_px, bid_px, bid_qty, ask_px, ask_qty, oi, volume, iv, delta, gamma, vega, flags)
            SELECT @ts::timestamptz, @u, @expiry, k, sd.side, @ts::timestamptz, k / 10 + CASE WHEN sd.side = 'CE' THEN 0 ELSE 1 END, 2500100, 10000, @bid, 10, @ask, 10, 1000, 100, 20, 0.5, 0.001, 1.0, 0
            FROM unnest(ARRAY[2495000, 2500000, 2505000]) k, (VALUES ('CE'), ('PE')) sd(side)
            """,
            ("ts", new DateTimeOffset(day.Year, day.Month, day.Day, 11, 0, 0, TimeSpan.FromMinutes(330)).ToUniversalTime()), ("u", _nifty), ("expiry", day.AddDays(1)), ("bid", bid), ("ask", ask));
    }

    private static OptionQualityRow Row(DateOnly day, string code, QualityStatus status) =>
        new(day, Guid.Empty, QualityCodes.Chain, null, null, new QualityCheck(code, status, null, null, null, 1, null));

    private BuildSpreadModelHandler Handler(IServiceProvider services)
    {
        OptionQualityRow[] rows =
        [
            Row(First, QualityCodes.Coverage, QualityStatus.Pass), Row(First, QualityCodes.Parity, QualityStatus.Pass),
            Row(Second, QualityCodes.Coverage, QualityStatus.Pass), Row(Second, QualityCodes.Parity, QualityStatus.Warn),
            Row(Unusable, QualityCodes.Coverage, QualityStatus.Pass), Row(Unusable, QualityCodes.Parity, QualityStatus.Fail),
        ];

        // the archive reader is used only when no symbol is named
        return new BuildSpreadModelHandler(new FakeReference(_nifty), null!, new FakeQuality(rows), services.GetRequiredService<IChainSpreadReader>(), services.GetRequiredService<ISpreadModelStore>(), new FakeSettings(), new FakeTimeProvider(Now));
    }

    private sealed class FakeSettings : ISpreadModelSettings
    {
        public int MinUsableSessions => 2;

        public int MinQuotesPerBucket => 4;

        public LiquidityThresholds Liquidity { get; } = new(5m, 25m);

        public decimal? AssumedHalfSpreadPct(string canonicalSymbol) => null;
    }

    private sealed class FakeReference(Guid nifty) : IOptionReference
    {
        public ValueTask<IReadOnlyDictionary<Guid, string>> SymbolsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Guid?> FindUnderlyingAsync(string canonicalSymbol, CancellationToken cancellationToken) => ValueTask.FromResult<Guid?>(canonicalSymbol == "NSE:NIFTY" ? nifty : null);

        public ValueTask<int?> StrikeStepAsync(Guid underlyingId, DateOnly date, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<XDrishti.Domain.Options.LotSizeEntry>> LotSizesAsync(Guid underlyingId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<XDrishti.Domain.Options.MarketEpoch>> EpochsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeQuality(OptionQualityRow[] rows) : IOptionQualityStore
    {
        public ValueTask<long> StartRunAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask AddResultsAsync(long runId, IReadOnlyList<OptionQualityRow> results, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask FinishRunAsync(long runId, string trigger, DateTimeOffset startedAt, DateTimeOffset endedAt, int sessions, int results, string? note, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<OptionQualityRow>> ChainResultsAsync(Guid underlyingId, DateOnly to, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<OptionQualityRow>>(rows);

        public ValueTask<IReadOnlyList<OptionQualityRow>> RollingResultsAsync(Guid underlyingId, DateOnly from, DateOnly to, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    [Fact]
    public async Task The_reader_returns_one_india_day_of_bid_ask_and_bucket_keys_in_order_and_nothing_of_the_next_day()
    {
        await SeedSessionAsync(First, 9_900, 10_100);
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(
                connection,
                "INSERT INTO options.chain_snapshots (ts, underlying_id, expiry, strike_px, side, rx_at, security_id, underlying_px, bid_px, ask_px, flags) VALUES (@ts, @u, @expiry, 2500000, 'CE', @ts, 77, 2500100, NULL, 10100, 0)",
                ("ts", new DateTimeOffset(Second.Year, Second.Month, Second.Day, 0, 0, 0, TimeSpan.FromMinutes(330)).ToUniversalTime()), ("u", _nifty), ("expiry", Second));
        }

        using var scope = Factory.Services.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<IChainSpreadReader>();

        var firstDay = await reader.QuotesAsync(_nifty, First, Ct);
        var secondDay = await reader.QuotesAsync(_nifty, Second, Ct);

        firstDay.Count.ShouldBe(6);
        firstDay.Select(q => q.StrikePx).ShouldBe([2_495_000, 2_495_000, 2_500_000, 2_500_000, 2_505_000, 2_505_000]);
        firstDay.ShouldAllBe(q => q.BidPx == 9_900 && q.AskPx == 10_100 && q.UnderlyingPx == 2_500_100 && q.Expiry == First.AddDays(1));
        firstDay[0].Ts.ToUniversalTime().ShouldBe(new DateTimeOffset(2026, 10, 5, 5, 30, 0, TimeSpan.Zero));
        secondDay.ShouldHaveSingleItem().BidPx.ShouldBeNull();
        (await reader.QuotesAsync(Guid.NewGuid(), First, Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_model_is_built_from_the_usable_sessions_alone_stored_loaded_and_a_rebuild_writes_nothing()
    {
        await SeedSessionAsync(First, 9_900, 10_100); // 1.0 % half-spread
        await SeedSessionAsync(Second, 9_900, 10_100);
        await SeedSessionAsync(Unusable, 1_000, 20_000); // would move every median; its quality is a fail, so it is never read
        using var scope = Factory.Services.CreateScope();
        var handler = Handler(scope.ServiceProvider);

        var built = await handler.HandleAsync(new BuildSpreadModelCommand("NSE:NIFTY"), Ct);

        var line = built.Value.Lines.ShouldHaveSingleItem();
        line.Status.ShouldBe("built");
        line.UsableSessions.ShouldBe(2);
        var model = await scope.ServiceProvider.GetRequiredService<ISpreadModelStore>().LatestAsync(_nifty, Ct);
        model.ShouldNotBeNull();
        model.ModelId.ShouldBe(SpreadModelId.Of(_nifty, [First, Second], 2, 4));
        model.Sessions.ShouldBe(2);
        model.FirstSession.ShouldBe(First);
        model.LastSession.ShouldBe(Second);
        // two sessions x (CE, PE): 4 quotes at the ATM (0 steps), 8 at one step: the buckets reached the least count of 4
        model.Lookup(new SpreadBucket("0", "0-2", "10:00-14:30")).ShouldBe(new SpreadBucketRow("0", "0-2", "10:00-14:30", 4, 1.0));
        model.Lookup(new SpreadBucket("1-2", "0-2", "10:00-14:30")).ShouldBe(new SpreadBucketRow("1-2", "0-2", "10:00-14:30", 8, 1.0));
        model.Lookup(new SpreadBucket("0", "31+", "10:00-14:30")).ShouldBeNull();
        (await ScalarAsync<long>("SELECT count(*) FROM research.option_spread_models")).ShouldBe(1);
        var buckets = await ScalarAsync<long>("SELECT count(*) FROM research.option_spread_buckets");
        buckets.ShouldBe(model.BucketCount);

        var again = await handler.HandleAsync(new BuildSpreadModelCommand("NSE:NIFTY"), Ct);

        again.Value.Lines.ShouldHaveSingleItem().Status.ShouldBe("already_built");
        (await ScalarAsync<long>("SELECT count(*) FROM research.option_spread_models")).ShouldBe(1);
        (await ScalarAsync<long>("SELECT count(*) FROM research.option_spread_buckets")).ShouldBe(buckets);
    }

    [Fact]
    public async Task Storing_the_same_model_twice_writes_nothing_the_second_time_and_a_lookup_of_no_model_is_null()
    {
        var store = Factory.Services.CreateScope().ServiceProvider.GetRequiredService<ISpreadModelStore>();
        var id = new string('b', 64);
        var model = new BuiltSpreadModel(id, _nifty, 60, First, Second, "{}", [new SpreadBucketRow("0", "0-2", "all", 300, 0.75)]);

        (await store.LatestAsync(_nifty, Ct)).ShouldBeNull();
        (await store.ExistsAsync(id, Ct)).ShouldBeFalse();
        (await store.AppendAsync(model, Ct)).ShouldBeTrue();
        (await store.AppendAsync(model, Ct)).ShouldBeFalse();

        (await store.ExistsAsync(id, Ct)).ShouldBeTrue();
        (await ScalarAsync<long>("SELECT count(*) FROM research.option_spread_buckets")).ShouldBe(1);
        (await store.LatestAsync(_nifty, Ct)).ShouldNotBeNull().Lookup(new SpreadBucket("0", "0-2", "09:20-10:00")).ShouldBe(new SpreadBucketRow("0", "0-2", "all", 300, 0.75));
    }
}
