using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.Simulate;
using XDrishti.Domain.Market.Bars;
using XDrishti.Infrastructure.Persistence;
using XDrishti.Tests.Shared;

namespace XDrishti.Api.IntegrationTests.Simulate;

/// <summary>
/// The verb on a stored strategy against a real database (spec P3-02d, criteria 8 and 9): after the seed, <c>--strategy ORB</c>, <c>--strategy ORB --strategy-version 1</c> and <c>--strategy-hash</c> write the files of the run through the
/// built-in path of before, byte for byte, with the same run id; the version and hash are printed first; an unknown id, version or hash is a refusal that names what exists.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class StoredStrategyVerbTests(ApiFactory factory) : DataTestBase(factory), IAsyncDisposable
{
    private const string OrbHash = "0ba11bc69d2f1cfa24d8801f78f8fcc111415b39fa1454a1a09e59eeebf8574c";

    private const string Window = "--symbols ABB,TCS --from 2026-03-02 --to 2026-03-04 --gross --fills no-slippage";

    private static readonly string[] Files = ["trades.csv", "open_at_end.csv", "run.json"];

    private sealed record Outcome(int Exit, string Out, string Error);

    private async Task ClearLabAsync()
    {
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "DELETE FROM lab.strategy_versions; DELETE FROM lab.strategies;");
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await ClearLabAsync();
    }

    private async Task<Outcome> VerbAsync(string args, TempFolder temp)
    {
        var overrides = new Dictionary<string, string?> { ["Database:ConnectionString"] = Factory.AdminConnectionString, ["BrokerVault:Key"] = string.Empty };
        using var host = SimulateHost.Create(overrides, _ => { });
        var output = new StringWriter();
        var error = new StringWriter();

        var exit = await SimulateCommand.RunAsync(args.Split(' ', StringSplitOptions.RemoveEmptyEntries), host.Services, new SimulateConsole(output, error), temp.Root, Ct);

        return new Outcome(exit, output.ToString(), error.ToString());
    }

    private async Task SeedBarsAsync()
    {
        foreach (var (ticker, bars) in new[] { ("ABB", ReplayDays.Join(ReplayDays.Flat(ReplayDays.D1, 99.00), ReplayDays.F1(ReplayDays.D2), ReplayDays.Flat(ReplayDays.D3, 105.00))), ("TCS", ReplayDays.Join(ReplayDays.F2(ReplayDays.D1), ReplayDays.Flat(ReplayDays.D2, 98.50), ReplayDays.Flat(ReplayDays.D3, 98.50))) })
        {
            var id = await SeedAsync(ticker);
            await using (var connection = await OpenAsync())
            {
                await ExecuteAsync(connection, "UPDATE market.instruments SET tick_size = 5 WHERE id = @id", ("id", id));
            }

            using var scope = Factory.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<XDrishti.Application.Abstractions.Market.Bars.IBarStore>();
            await using var session = await store.BeginMergeAsync(id, BarSource.Dhan, Ct);
            await session.AddAsync(bars, Ct);
            await session.CompleteAsync(null, Ct);
        }
    }

    private static string RunFolder(TempFolder temp) => Directory.EnumerateDirectories(Path.Combine(temp.Root, "sim")).Single(d => !Path.GetFileName(d).StartsWith('.'));

    private static void SameRun(TempFolder expected, TempFolder actual)
    {
        Path.GetFileName(RunFolder(actual)).ShouldBe(Path.GetFileName(RunFolder(expected)), "the same run id: the same record and the same key");
        foreach (var file in Files)
        {
            File.ReadAllBytes(Path.Combine(RunFolder(actual), file)).ShouldBe(File.ReadAllBytes(Path.Combine(RunFolder(expected), file)), file);
        }
    }

    [Fact]
    public async Task A_stored_strategy_writes_the_files_of_the_built_in_path_byte_for_byte_by_id_by_version_and_by_hash_and_prints_its_version_first()
    {
        await SeedBarsAsync();
        using var builtIn = new TempFolder("builtin");
        var before = await VerbAsync("--strategy ORB " + Window, builtIn);
        await StrategySeeding.SeedAsync(Factory.Services, Ct);
        using var byId = new TempFolder("by-id");
        using var byVersion = new TempFolder("by-version");
        using var byHash = new TempFolder("by-hash");

        var latest = await VerbAsync("--strategy ORB " + Window, byId);
        var versioned = await VerbAsync("--strategy ORB --strategy-version 1 " + Window, byVersion);
        var hashed = await VerbAsync("--strategy-hash " + OrbHash + " --timeframe 15m " + Window, byHash);

        before.Exit.ShouldBe(0, before.Error);
        before.Out.Contains("strategy ORB version", StringComparison.Ordinal).ShouldBeFalse("nothing is stored yet: the built-in of the engine ran");
        foreach (var outcome in new[] { latest, versioned, hashed })
        {
            outcome.Exit.ShouldBe(0, outcome.Error);
            outcome.Out.ShouldStartWith("strategy ORB version 1, hash " + OrbHash + "\n".Replace("\n", Environment.NewLine, StringComparison.Ordinal));
        }

        SameRun(builtIn, byId);
        SameRun(builtIn, byVersion);
        SameRun(builtIn, byHash);
        File.ReadAllText(Path.Combine(RunFolder(byId), "trades.csv")).Split('\n').Length.ShouldBeGreaterThan(2, "the run has trades: the comparison is not between two empty files");
    }

    [Fact]
    public async Task An_unknown_version_hash_or_id_is_a_refusal_that_names_what_exists_and_writes_nothing()
    {
        await SeedBarsAsync();
        await StrategySeeding.SeedAsync(Factory.Services, Ct);
        using var temp = new TempFolder();

        var version = await VerbAsync("--strategy ORB --strategy-version 9 " + Window, temp);
        var hash = await VerbAsync("--strategy-hash " + new string('0', 64) + " --timeframe 15m " + Window, temp);
        var id = await VerbAsync("--strategy MACD " + Window, temp);

        version.Exit.ShouldBe(SimulateCommand.Refused);
        version.Error.ShouldContain("simulation.unknown_strategy_version");
        version.Error.ShouldContain("1 to 1");
        hash.Exit.ShouldBe(SimulateCommand.Refused);
        hash.Error.ShouldContain("simulation.unknown_strategy_hash");
        id.Exit.ShouldBe(SimulateCommand.Refused);
        id.Error.ShouldContain("simulation.unknown_strategy");
        id.Error.ShouldContain("`BASE_BREAKOUT`, `EMA_PULLBACK`, `GAP_FILL`, `GAP_GO`, `HIGH_52W_MOMENTUM`, `INSIDE_BAR_BO`, `MEAN_REV_BANDS`, `NR7_ID_BO`, `ORB`, `PDH_PDL_BREAK`, `PDH_PDL_REJECT`, `RSI2_REVERSION`, `TREND_PULLBACK`, `VWAP_PULLBACK`, `VWAP_RECLAIM`");
        Directory.Exists(Path.Combine(temp.Root, "sim")).ShouldBeFalse("a refusal writes no export");
    }

    [Fact]
    public async Task The_run_a_stored_text_gives_is_refused_when_the_text_is_changed_in_the_database()
    {
        await SeedBarsAsync();
        await StrategySeeding.SeedAsync(Factory.Services, Ct);
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "UPDATE lab.strategy_versions SET source_text = replace(source_text, 'or_min: 15', 'or_min: 45') WHERE strategy_id = 'ORB'");
        }

        using var temp = new TempFolder();

        var outcome = await VerbAsync("--strategy ORB " + Window, temp);

        outcome.Exit.ShouldBe(SimulateCommand.Refused);
        outcome.Error.ShouldContain("strategy_store.hash_mismatch");
        Directory.Exists(Path.Combine(temp.Root, "sim")).ShouldBeFalse();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await ClearLabAsync();
        await DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
