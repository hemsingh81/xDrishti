using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.OptionBackfill;
using XDrishti.Application.Abstractions.OptionExpression;
using XDrishti.Application.Abstractions.OptionIv;
using XDrishti.Application.Abstractions.OptionQuality;
using XDrishti.Application.Learning;
using XDrishti.Domain.Filings;
using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Options.Quality;
using XDrishti.Domain.Research;
using XDrishti.Infrastructure.OptionBackfill;
using XDrishti.Tests.Shared;

namespace XDrishti.Api.IntegrationTests.OptionExpression;

/// <summary>
/// P14-11 on a real TimescaleDB (the integration part of the test plan): the three new reads of the rolling-bar reader (offset filter, grouped daily last bars, existence), the quality verdicts of chosen keys with their run ids, and the extractor that uses them on a seeded archive: the features
/// of a trade at its decision instant, and the values NaN with a reason where the archive has nothing. The slice adds no migration, so the grants are those of the archive and the research schema (pinned by their own grant tests). Synthetic bars only.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class IvArchiveDatabaseTests(ApiFactory factory) : DataTestBase(factory)
{
    private const string Truncate = "TRUNCATE options.rolling_bars, options.fetch_log, options.quality_results, options.quality_runs, options.contracts RESTART IDENTITY";

    private static readonly DateOnly Monday = new(2025, 6, 9);
    private static readonly TimeSpan India = TimeSpan.FromMinutes(330);

    private Guid _nifty;
    private Guid _sleepy;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, Truncate);
        }

        _nifty = await SeedAsync("NIFTY", type: "index", dhanId: false);
        _sleepy = await SeedAsync("SLEEPY", dhanId: false);
    }

    public override async ValueTask DisposeAsync()
    {
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, Truncate);
        }

        await base.DisposeAsync();
    }

    private async Task<T> WithAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        using var scope = Factory.Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    private static DateTimeOffset At(DateOnly day, int hour, int minute) => new(day.ToDateTime(new TimeOnly(hour, minute)), India);

    private static IEnumerable<DateTimeOffset> Times(DateOnly day) => Enumerable.Range(0, 75).Select(i => At(day, 9, 15).AddMinutes(i * 5));

    private async Task SeedAsync(DateOnly day, int code, string side, float? iv, long? oi, params int[] offsets)
    {
        foreach (var offset in offsets)
        {
            var bars = Times(day).Select(t => new BackfillBar(t, _nifty, 'W', code, offset, side, 5, 2_200_000 + (offset * 5000), 2_200_000, 6000, 6100, 5900, 6000, 1000, oi, offset == 0 ? iv : 12f, 0)).ToList();
            var attempt = new BackfillFetchRecord(new BackfillKey(_nifty, 'W', code, offset, side, 5, day.AddDays(-20)), 1, At(day, 20, 0), At(day, 20, 1), "ok", 200, null, 0, bars.Count);
            await WithAsync(async p => await new RollingBarStore(p.GetRequiredService<NpgsqlDataSource>()).AppendAsync(attempt, bars, Ct));
        }
    }

    private async Task<long> VerdictsAsync(DateOnly day, int code, string side, int[] offsets, QualityStatus stitching = QualityStatus.Pass)
    {
        return await WithAsync(async p =>
        {
            var store = p.GetRequiredService<IOptionQualityStore>();
            var run = await store.StartRunAsync(Ct);
            var rows = new List<OptionQualityRow>();
            foreach (var offset in offsets)
            {
                var key = $"{_nifty}:W{code}:{offset}:{side}:5";
                rows.Add(new OptionQualityRow(day, _nifty, "rolling", null, key, new QualityCheck("coverage", QualityStatus.Pass, 1, 0.95, null, 75, null)));
                rows.Add(new OptionQualityRow(day, _nifty, "rolling", null, key, new QualityCheck("stitching", stitching, 0, 0.01, null, 74, null)));
            }

            await store.AddResultsAsync(run, rows, Ct);
            await store.FinishRunAsync(run, "manual", At(day, 20, 0), At(day, 20, 1), 1, rows.Count, null, Ct);
            return run;
        });
    }

    private static readonly int[] Seven = [-3, -2, -1, 0, 1, 2, 3];

    [Fact]
    public async Task The_offset_overload_returns_only_the_requested_offsets_and_the_existence_query_tells_an_underlying_with_bars_from_one_without()
    {
        await SeedAsync(Monday, 1, "CE", 15f, 10_000, -3, 0, 5);

        var some = await WithAsync(p => p.GetRequiredService<IRollingBarReader>().BarsAsync(_nifty, 'W', 1, "CE", 5, [0, 5], At(Monday, 0, 0), At(Monday.AddDays(1), 0, 0), Ct).AsTask());
        var has = await WithAsync(p => p.GetRequiredService<IRollingBarReader>().HasSeriesAsync(_nifty, Ct).AsTask());
        var hasNot = await WithAsync(p => p.GetRequiredService<IRollingBarReader>().HasSeriesAsync(_sleepy, Ct).AsTask());

        some.Count.ShouldBe(150);
        some.Select(b => b.Offset).Distinct().Order().ShouldBe([0, 5]);
        has.ShouldBeTrue();
        hasNot.ShouldBeFalse();
    }

    [Fact]
    public async Task The_daily_read_gives_the_offset_zero_iv_of_the_last_bar_of_each_session_code_and_side()
    {
        await SeedAsync(Monday, 1, "CE", 15f, 10_000, 0, 1);
        await SeedAsync(Monday, 1, "PE", 17f, 10_000, 0);
        await SeedAsync(Monday, 2, "CE", 19f, 10_000, 0);
        await SeedAsync(Monday.AddDays(1), 1, "CE", null, 10_000, 0);

        var rows = await WithAsync(p => p.GetRequiredService<IRollingBarReader>().DailyLastBarsAsync(_nifty, 'W', [1, 2], 5, At(Monday, 0, 0), At(Monday.AddDays(2), 0, 0), Ct).AsTask());

        rows.Count.ShouldBe(4, "one row per session, code and side; offset 1 is not read");
        rows.Single(r => r.Date == Monday && r.Code == 1 && r.Side == "CE").VendorIv.ShouldBe(15.0);
        rows.Single(r => r.Date == Monday && r.Code == 1 && r.Side == "PE").VendorIv.ShouldBe(17.0);
        rows.Single(r => r.Date == Monday && r.Code == 2).VendorIv.ShouldBe(19.0);
        rows.Single(r => r.Date == Monday.AddDays(1)).VendorIv.ShouldBeNull("a last bar without an IV is a missing value, never zero");
        rows.ShouldAllBe(r => !r.Usable);
    }

    [Fact]
    public async Task The_quality_read_returns_the_newest_verdict_of_the_chosen_keys_only_with_the_id_of_the_run()
    {
        var first = await VerdictsAsync(Monday, 1, "CE", [0, 1], QualityStatus.Fail);
        var second = await VerdictsAsync(Monday, 1, "CE", [0]);
        var key = $"{_nifty}:W1:0:CE:5";

        var rows = await WithAsync(p => p.GetRequiredService<IRollingQualityRuns>().RollingResultsForKeysAsync(_nifty, [key], Monday, Monday, Ct).AsTask());

        rows.Count.ShouldBe(2, "coverage and stitching of the one key");
        rows.ShouldAllBe(r => r.Row.FetchKey == key && r.RunId == second);
        rows.Single(r => r.Row.Check.Code == "stitching").Row.Check.Status.ShouldBe(QualityStatus.Pass, "the newer run replaces the failed one");
        first.ShouldBeLessThan(second);
        (await WithAsync(p => p.GetRequiredService<IRollingQualityRuns>().RollingResultsForKeysAsync(_nifty, [], Monday, Monday, Ct).AsTask())).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_extractor_gives_the_features_of_a_trade_from_the_last_ended_bar_and_nan_with_a_reason_where_the_archive_has_nothing()
    {
        foreach (var (code, ce, pe) in new[] { (1, 15f, 17f), (2, 18f, 20f) })
        {
            await SeedAsync(Monday, code, "CE", ce, 10_000, Seven);
            await SeedAsync(Monday, code, "PE", pe, 10_000, Seven);
            await VerdictsAsync(Monday, code, "CE", Seven);
            await VerdictsAsync(Monday, code, "PE", Seven);
        }

        var decision = At(Monday, 11, 0);
        var trades = new[] { new IvTrade(_nifty, "nifty-trade", decision), new IvTrade(_sleepy, "sleepy-trade", decision) };

        var extraction = await WithAsync(p => p.GetRequiredService<IIvFeatureExtractor>().ExtractAsync(trades, EventCalendarSnapshot.Create([], 5), new TradingCalendar(), Ct).AsTask());

        var nifty = extraction.Cells["nifty-trade"];
        nifty[0].Value.ShouldBe(0.16, 1e-6);
        nifty[3].Value.ShouldBe(0.19 - 0.16, 1e-6);
        nifty[6].Value.ShouldBe(1.0, 1e-12);
        nifty[1].Reason.ShouldBe(FeatureReasons.ShortHistory);
        nifty[7].Reason.ShouldBe(IvReasons.EventUnavailable);
        var sleepy = extraction.Cells["sleepy-trade"];
        sleepy[0].Reason.ShouldBe(IvReasons.NoOptions);
        sleepy[6].Reason.ShouldBe(IvReasons.NoOptions);
        sleepy[5].Reason.ShouldBe(FeatureReasons.ShortHistory, "an instrument without options still gets the NIFTY rank, here with too little history");
        extraction.Sources.ArchiveWatermark.ShouldBeGreaterThan(0);
        extraction.Sources.QualityRunIds.Count.ShouldBe(4);
        extraction.Sources.IvUnit.ShouldBe("percent_assumed");
    }

    [Fact]
    public async Task A_key_session_that_failed_stitching_makes_the_value_archive_unusable()
    {
        await SeedAsync(Monday, 1, "CE", 15f, 10_000, Seven);
        await SeedAsync(Monday, 1, "PE", 17f, 10_000, Seven);
        await VerdictsAsync(Monday, 1, "CE", Seven, QualityStatus.Fail);
        await VerdictsAsync(Monday, 1, "PE", Seven);

        var extraction = await WithAsync(p => p.GetRequiredService<IIvFeatureExtractor>().ExtractAsync([new IvTrade(_nifty, "t", At(Monday, 11, 0))], EventCalendarSnapshot.Create([], 5), new TradingCalendar(), Ct).AsTask());

        extraction.Cells["t"][0].Reason.ShouldBe(IvReasons.ArchiveUnusable);
        extraction.Cells["t"][6].Reason.ShouldBe(IvReasons.ArchiveUnusable);
    }

    [Fact]
    public async Task The_handler_extracts_a_stored_run_from_the_seeded_archive_and_the_stored_arrays_agree_with_the_nan_counts_of_the_record()
    {
        foreach (var code in new[] { 1, 2 })
        {
            await SeedAsync(Monday, code, "CE", 15f + code, 10_000, Seven);
            await SeedAsync(Monday, code, "PE", 17f + code, 10_000, Seven);
            await VerdictsAsync(Monday, code, "CE", Seven);
            await VerdictsAsync(Monday, code, "PE", Seven);
        }

        var members = new[] { (_nifty, "NSE:NIFTY"), (_sleepy, "NSE:SLEEPY") };
        var run = FabricatedRun.Assemble(
            FabricatedRun.List(members),
            [FabricatedRun.Ran(_nifty, "NSE:NIFTY", Monday, Monday), FabricatedRun.Ran(_sleepy, "NSE:SLEEPY", Monday, Monday)],
            [FabricatedRun.Trade(_nifty, "NSE:NIFTY", Side.Buy, Monday, entryMinute: 662), FabricatedRun.Trade(_sleepy, "NSE:SLEEPY", Side.Buy, Monday, entryMinute: 662)],
            from: Monday,
            to: Monday);
        (await StoreKit.WithStoreAsync(Factory.Services, store => store.StoreAsync(StoreKit.ToStore(run), Ct).AsTask())).IsSuccess.ShouldBeTrue();

        var outcome = await WithAsync(p => p.GetRequiredService<ICommandHandler<ExtractExtensionFeaturesCommand, ExtensionFeaturesOutcome>>().HandleAsync(new ExtractExtensionFeaturesCommand(run.RunKey, "iv-1"), Ct).AsTask());
        var rows = await WithAsync(async p => await p.GetRequiredService<IExtensionFeatureStore>().ReadAsync([run.RunKey], "iv-1", Ct));
        var record = (await WithAsync(async p => await p.GetRequiredService<IExtensionExtractionStore>().FindAsync(run.RunKey, "iv-1", Ct)))!;

        outcome.IsSuccess.ShouldBeTrue(outcome.IsFailure ? outcome.Error.Message : string.Empty);
        outcome.Value.Stored.ShouldBe(2);
        rows.Count.ShouldBe(2);
        (record.LagMinutes, record.ParserVersion, record.Rows).ShouldBe((0, null, 2));
        using var counts = JsonDocument.Parse(record.NanCountsJson);
        var nan = counts.RootElement.GetProperty("nan");
        for (var i = 0; i < IvFeatureCatalog.Names.Count; i++)
        {
            var stored = rows.Count(r => double.IsNaN(r.Values[i]));
            var recorded = nan.GetProperty(IvFeatureCatalog.Names[i]).EnumerateObject().Sum(reason => reason.Value.GetInt32());
            stored.ShouldBe(recorded, IvFeatureCatalog.Names[i]);
        }

        rows.Single(r => r.TradeKey.Contains("NIFTY", StringComparison.Ordinal)).Values[0].ShouldBe(0.17, 1e-6);
        counts.RootElement.GetProperty("sources").GetProperty("archive_watermark").GetString().ShouldNotBe("0");
        var again = await WithAsync(p => p.GetRequiredService<ICommandHandler<ExtractExtensionFeaturesCommand, ExtensionFeaturesOutcome>>().HandleAsync(new ExtractExtensionFeaturesCommand(run.RunKey, "iv-1"), Ct).AsTask());
        again.Value.AlreadyExtracted.ShouldBeTrue();
    }

    [Fact]
    public async Task The_handler_refuses_an_iv_extraction_on_an_empty_archive_and_stores_neither_rows_nor_a_record()
    {
        var run = FabricatedRun.Assemble(
            FabricatedRun.List((_nifty, "NSE:NIFTY")), [FabricatedRun.Ran(_nifty, "NSE:NIFTY", Monday, Monday)], [FabricatedRun.Trade(_nifty, "NSE:NIFTY", Side.Buy, Monday, entryMinute: 662)], from: Monday, to: Monday);
        (await StoreKit.WithStoreAsync(Factory.Services, store => store.StoreAsync(StoreKit.ToStore(run), Ct).AsTask())).IsSuccess.ShouldBeTrue();

        var outcome = await WithAsync(p => p.GetRequiredService<ICommandHandler<ExtractExtensionFeaturesCommand, ExtensionFeaturesOutcome>>().HandleAsync(new ExtractExtensionFeaturesCommand(run.RunKey, "iv-1"), Ct).AsTask());

        outcome.IsFailure.ShouldBeTrue();
        outcome.Error.Code.ShouldBe("learning.iv_archive_empty");
        (await WithAsync(async p => await p.GetRequiredService<IExtensionExtractionStore>().FindAsync(run.RunKey, "iv-1", Ct))).ShouldBeNull();
        (await WithAsync(async p => await p.GetRequiredService<IExtensionFeatureStore>().ReadAsync([run.RunKey], "iv-1", Ct))).ShouldBeEmpty();
    }
}
