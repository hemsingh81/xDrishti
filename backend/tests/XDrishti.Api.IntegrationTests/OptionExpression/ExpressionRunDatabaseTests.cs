using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.Cli;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.OptionBackfill;
using XDrishti.Application.Abstractions.OptionExpression;
using XDrishti.Application.Abstractions.OptionQuality;
using XDrishti.Application.OptionExpression;
using XDrishti.Domain.Options.Quality;
using XDrishti.Domain.Options.Remark;
using XDrishti.Domain.Research;
using XDrishti.Infrastructure.OptionBackfill;
using XDrishti.Tests.Shared;

namespace XDrishti.Api.IntegrationTests.OptionExpression;

/// <summary>
/// P14-08 on a real TimescaleDB (criteria 1, 9, 10 and the integration part of the test plan): the bounded reader of one series and its watermark, the contract expiries, and a seeded archive with a stored cash run put through the use case that the verb calls: one row per cash trade,
/// the unpaired reasons counted, the premium read from the stored bars, and a second run that writes nothing. Synthetic bars only.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ExpressionRunDatabaseTests(ApiFactory factory) : DataTestBase(factory)
{
    private const string Truncate = "TRUNCATE options.rolling_bars, options.fetch_log, options.quality_results, options.quality_runs, options.contracts, research.expression_trades, research.expression_runs RESTART IDENTITY";

    private static readonly DateOnly Tuesday = new(2025, 6, 10);
    private static readonly TimeSpan India = TimeSpan.FromMinutes(330);

    private Guid _nifty;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, Truncate);
        }

        _nifty = await SeedAsync("NIFTY", type: "index", dhanId: false);
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

    private BackfillBar Bar(DateTimeOffset ts, int offset, string side = "CE", int code = 1, short flags = 0, float? iv = 15f, long? oi = 10_000) =>
        new(ts, _nifty, 'W', code, offset, side, 5, 2_200_000 + (offset * 5000), 2_200_000, 6000 + (offset < 0 ? -offset * 5000 : 0), 6100, 5900, 6000 + (offset < 0 ? -offset * 5000 : 0), 1000, oi, iv, flags);

    private BackfillFetchRecord Attempt(int code = 1, int offset = 0, DateOnly? from = null) =>
        new(new BackfillKey(_nifty, 'W', code, offset, "CE", 5, from ?? new DateOnly(2025, 5, 20)), 1, At(Tuesday, 20, 0), At(Tuesday, 20, 1), "ok", 200, null, 0, 100);

    private static IEnumerable<DateTimeOffset> Times(DateOnly day) => Enumerable.Range(0, 75).Select(i => At(day, 9, 15).AddMinutes(i * 5));

    private async Task SeedBarsAsync(DateOnly day, int code = 1, params int[] offsets)
    {
        foreach (var offset in offsets)
        {
            var bars = Times(day).Select(t => Bar(t, offset, code: code)).ToList();
            await WithAsync(async p => await new RollingBarStore(p.GetRequiredService<NpgsqlDataSource>()).AppendAsync(Attempt(code, offset), bars, Ct));
        }
    }

    private async Task SeedVerdictsAsync(DateOnly day, int code, params int[] offsets)
    {
        await WithAsync(async p =>
        {
            var store = p.GetRequiredService<IOptionQualityStore>();
            var run = await store.StartRunAsync(Ct);
            var rows = new List<OptionQualityRow>();
            foreach (var offset in offsets)
            {
                var key = $"{_nifty}:W{code}:{offset}:CE:5";
                rows.Add(new OptionQualityRow(day, _nifty, "rolling", null, key, new QualityCheck("coverage", QualityStatus.Pass, 1, 0.95, null, 75, null)));
                rows.Add(new OptionQualityRow(day, _nifty, "rolling", null, key, new QualityCheck("stitching", QualityStatus.Pass, 0, 0.01, null, 74, null)));
            }

            await store.AddResultsAsync(run, rows, Ct);
            await store.FinishRunAsync(run, "manual", At(day, 20, 0), At(day, 20, 1), 1, rows.Count, null, Ct);
            return 0;
        });
    }

    [Fact]
    public async Task The_reader_returns_one_series_over_one_time_range_ordered_with_every_offset_and_the_flags_as_stored()
    {
        await SeedBarsAsync(Tuesday, 1, 0, -1);
        await SeedBarsAsync(Tuesday, 2, 0);
        var suspect = new List<BackfillBar> { Bar(At(Tuesday.AddDays(1), 9, 15), 0, flags: 1, iv: null, oi: null) };
        await WithAsync(async p => await new RollingBarStore(p.GetRequiredService<NpgsqlDataSource>()).AppendAsync(Attempt(1, 0, new DateOnly(2025, 6, 10)), suspect, Ct));

        var bars = await WithAsync(p => p.GetRequiredService<IRollingBarReader>().BarsAsync(_nifty, 'W', 1, "CE", 5, At(Tuesday, 0, 0), At(Tuesday.AddDays(2), 0, 0), Ct).AsTask());
        var oneDay = await WithAsync(p => p.GetRequiredService<IRollingBarReader>().BarsAsync(_nifty, 'W', 1, "CE", 5, At(Tuesday, 9, 15), At(Tuesday, 9, 25), Ct).AsTask());

        bars.Count.ShouldBe(151, "75 bars of 2 offsets of code 1 and the one flagged bar; code 2 and the PE side are not in it");
        bars.Select(b => b.Ts).ShouldBe(bars.Select(b => b.Ts).Order());
        oneDay.Select(b => (b.Ts, b.Offset)).ShouldBe([(At(Tuesday, 9, 15), -1), (At(Tuesday, 9, 15), 0), (At(Tuesday, 9, 20), -1), (At(Tuesday, 9, 20), 0)], "the end of the range is not included");
        oneDay.ShouldAllBe(b => !b.Usable && !b.Suspect && b.Iv == 15f && b.Oi == 10_000);
        oneDay.First(b => b.Offset == -1).OpenPx.ShouldBe(11_000);
        var flagged = bars.Single(b => b.Ts == At(Tuesday.AddDays(1), 9, 15));
        flagged.Suspect.ShouldBeTrue();
        flagged.Iv.ShouldBeNull();
        flagged.Oi.ShouldBeNull();
        await Should.ThrowAsync<ArgumentOutOfRangeException>(async () => await WithAsync(p => p.GetRequiredService<IRollingBarReader>().BarsAsync(_nifty, 'W', 1, "CE", 5, At(Tuesday, 0, 0), At(Tuesday.AddDays(400), 0, 0), Ct).AsTask()));
    }

    [Fact]
    public async Task The_watermark_is_the_highest_finished_fetch_log_id_of_the_underlying_over_the_dates_and_zero_for_none()
    {
        await SeedBarsAsync(Tuesday, 1, 0);

        var covered = await WithAsync(p => p.GetRequiredService<IRollingBarReader>().WatermarkAsync(_nifty, Tuesday, Tuesday, Ct).AsTask());
        var elsewhere = await WithAsync(p => p.GetRequiredService<IRollingBarReader>().WatermarkAsync(_nifty, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 5), Ct).AsTask());
        var other = await WithAsync(p => p.GetRequiredService<IRollingBarReader>().WatermarkAsync(Guid.NewGuid(), Tuesday, Tuesday, Ct).AsTask());

        covered.ShouldBeGreaterThan(0);
        elsewhere.ShouldBe(0);
        other.ShouldBe(0);
    }

    [Fact]
    public async Task The_contract_reader_returns_the_distinct_expiries_and_kinds_of_one_underlying_in_the_range()
    {
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "INSERT INTO options.contracts (security_id, underlying_id, instrument, expiry, strike_px, side, expiry_kind, lot_size_seen, first_seen_on, last_seen_on) VALUES (1, @u, 'OPTIDX', '2026-11-05', 2200000, 'CE', 'week', 75, '2026-11-01', '2026-11-01'), (2, @u, 'OPTIDX', '2026-11-05', 2200000, 'PE', 'week', 75, '2026-11-01', '2026-11-01'), (3, @u, 'OPTIDX', '2026-11-25', 2200000, 'CE', 'month', 75, '2026-11-01', '2026-11-01'), (4, @u, 'OPTIDX', '2026-11-12', 2200000, 'CE', NULL, 75, '2026-11-01', '2026-11-01'), (5, @u, 'OPTIDX', '2027-03-30', 2200000, 'CE', 'month', 75, '2026-11-01', '2026-11-01')", ("u", _nifty));
        }

        var expiries = await WithAsync(p => p.GetRequiredService<IContractExpiryReader>().ExpiriesAsync(_nifty, new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 31), Ct).AsTask());

        expiries.ShouldBe([new ContractExpiry(new DateOnly(2026, 11, 5), 'W'), new ContractExpiry(new DateOnly(2026, 11, 25), 'M')]);
    }

    [Fact]
    public async Task A_seeded_archive_and_a_stored_cash_run_give_one_row_per_trade_read_the_premium_from_the_stored_bars_and_a_second_run_writes_nothing()
    {
        await SeedBarsAsync(Tuesday, 1, -2, -1, 0, 1, 2);
        await SeedVerdictsAsync(Tuesday, 1, -2, -1, 0, 1, 2);
        var profile = OptionProfileParams.Create("atm", "nearest_safe", null, 1).Value.ToProfile().Value;
        await WithAsync(async p => await p.GetRequiredService<IExpressionProfileStore>().AppendAsync(profile, Ct));
        var unknown = Guid.Parse("00000000-0000-0000-0000-0000000000f9");
        var trades = new[]
        {
            FabricatedRun.Trade(_nifty, "NSE:NIFTY", Side.Buy, Tuesday, ExitReason.Target, entryMinute: 560, entry: 100, stop: 99, target: 102, exitPrice: 102, rExact: 2, rFilled: 2),
            FabricatedRun.Trade(_nifty, "NSE:NIFTY", Side.Buy, Tuesday.AddDays(7), ExitReason.Stop, entryMinute: 560, entry: 100, stop: 99, target: 102, exitPrice: 99, rExact: -1, rFilled: -1),
            FabricatedRun.Trade(unknown, "NSE:OTHER", Side.Buy, Tuesday, ExitReason.Target, entryMinute: 560, entry: 100, stop: 99, target: 102, exitPrice: 102, rExact: 2, rFilled: 2),
        };
        var run = FabricatedRun.Assemble(FabricatedRun.List((_nifty, "NSE:NIFTY"), (unknown, "NSE:OTHER")), [FabricatedRun.Ran(_nifty, "NSE:NIFTY", Tuesday, Tuesday.AddDays(8)), FabricatedRun.Ran(unknown, "NSE:OTHER", Tuesday, Tuesday.AddDays(8))], trades, from: Tuesday, to: Tuesday.AddDays(8), strategy: "EXPR");
        (await StoreKit.WithStoreAsync(Factory.Services, s => s.StoreAsync(StoreKit.ToStore(run), Ct).AsTask())).IsSuccess.ShouldBeTrue();

        var first = await WithAsync(p => p.GetRequiredService<ICommandHandler<RemarkRunCommand, RemarkRunSummary>>().HandleAsync(new RemarkRunCommand(run.RunKey, profile.Hash), Ct).AsTask());
        var second = await WithAsync(p => p.GetRequiredService<ICommandHandler<RemarkRunCommand, RemarkRunSummary>>().HandleAsync(new RemarkRunCommand(run.RunKey, profile.Hash), Ct).AsTask());

        first.IsSuccess.ShouldBeTrue(first.IsFailure ? first.Error.Message : null);
        (await ScalarAsync<long>("SELECT count(*) FROM research.expression_trades WHERE fate = 'paired' AND entry_ts = @t", ("t", At(Tuesday, 9, 20).ToUniversalTime()))).ShouldBe(1, "the entry is the option bar of the cash entry minute");
        first.Value.Trades.ShouldBe(3);
        first.Value.Paired.ShouldBe(1);
        first.Value.Unpaired[RemarkFates.NoSeries].ShouldBe(2, "the instrument with no series, and the day with no bars");
        first.Value.Stored.ShouldBeTrue();
        second.Value.Stored.ShouldBeFalse();
        second.Value.RunId.ShouldBe(first.Value.RunId);
        (await ScalarAsync<long>("SELECT count(*) FROM research.expression_runs")).ShouldBe(1);
        (await ScalarAsync<long>("SELECT count(*) FROM research.expression_trades WHERE run_id = @r", ("r", first.Value.RunId))).ShouldBe(3);
        (await ScalarAsync<long>("SELECT count(*) FROM research.expression_trades WHERE run_id = @r AND fate = 'paired' AND entry_premium_px = 6000 AND strike_px = 2200000 AND expiry = '2025-06-12' AND side = 'CE' AND expiry_source = 'derived' AND r_equiv IS NOT NULL", ("r", first.Value.RunId))).ShouldBe(1);
        (await ScalarAsync<long>("SELECT count(*) FROM research.expression_trades WHERE run_id = @r AND fate <> 'paired' AND (entry_premium_px IS NOT NULL OR r_equiv IS NOT NULL OR cash_r IS NOT NULL)", ("r", first.Value.RunId))).ShouldBe(0);
        (await ScalarAsync<string>("SELECT unpaired::text FROM research.expression_runs")).ShouldContain("\"no_series\": 2");
    }

    [Fact]
    public async Task The_verb_runs_the_use_case_exits_0_and_says_already_stored_the_second_time()
    {
        await SeedBarsAsync(Tuesday, 1, 0);
        await SeedVerdictsAsync(Tuesday, 1, 0);
        var profile = OptionProfileParams.Create("atm", "nearest_safe", null, 1).Value.ToProfile().Value;
        await WithAsync(async p => await p.GetRequiredService<IExpressionProfileStore>().AppendAsync(profile, Ct));
        var trade = FabricatedRun.Trade(_nifty, "NSE:NIFTY", Side.Buy, Tuesday, ExitReason.Target, entryMinute: 560, entry: 100, stop: 99, target: 102, exitPrice: 102, rExact: 2, rFilled: 2);
        var run = FabricatedRun.Assemble(FabricatedRun.List((_nifty, "NSE:NIFTY")), [FabricatedRun.Ran(_nifty, "NSE:NIFTY", Tuesday, Tuesday.AddDays(8))], [trade], from: Tuesday, to: Tuesday.AddDays(8), strategy: "VERB");
        (await StoreKit.WithStoreAsync(Factory.Services, s => s.StoreAsync(StoreKit.ToStore(run), Ct).AsTask())).IsSuccess.ShouldBeTrue();
        string[] args = ["--run", run.RunKey, "--profile", profile.Hash];

        var output = new StringWriter();
        var error = new StringWriter();
        var exitFirst = await OptionsRemarkCommand.RunAsync(args, Factory.Services, new SimulateConsole(output, error), Ct);
        var exitSecond = await OptionsRemarkCommand.RunAsync(args, Factory.Services, new SimulateConsole(output, error), Ct);
        var exitUnknown = await OptionsRemarkCommand.RunAsync(["--run", new string('9', 64), "--profile", profile.Hash], Factory.Services, new SimulateConsole(output, error), Ct);

        exitFirst.ShouldBe(0, error.ToString());
        exitSecond.ShouldBe(0, error.ToString());
        exitUnknown.ShouldBe(1);
        output.ToString().ShouldContain("1 trades, 1 paired");
        output.ToString().ShouldContain("(already stored", Case.Sensitive); // the console cuts a line to its first characters
        (await ScalarAsync<long>("SELECT count(*) FROM research.expression_runs")).ShouldBe(1);
    }
}
