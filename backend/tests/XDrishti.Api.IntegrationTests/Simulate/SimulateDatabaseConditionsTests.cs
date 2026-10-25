using Microsoft.Extensions.DependencyInjection;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using ResearchPort = XDrishti.Application.Abstractions.Research;

namespace XDrishti.Api.IntegrationTests.Simulate;

/// <summary>P3-04d against a real TimescaleDB and the real engine: both card writers carry the conditions section, and an engine that does not reproduce the recorded trade stops both verbs with nothing written.</summary>
public sealed partial class SimulateDatabaseTests
{
    private const string OldLine = "not in this export";

    /// <summary>The real engine, except that <c>Explain</c> answers a replay whose trades have another stop: what a changed engine would do.</summary>
    private sealed class TamperingEngine(ResearchPort.ISimulationEngine inner) : ResearchPort.ISimulationEngine
    {
        public string EngineVersion => inner.EngineVersion;

        public IReadOnlyList<BuiltInStrategyInfo> BuiltIns => inner.BuiltIns;

        public Result<ResearchPort.IPreparedStrategy> Prepare(string strategyId, IReadOnlyDictionary<string, double> parameters, Timeframe evaluationTimeframe, FillRules? fills, ExitChoice? exits = null) =>
            inner.Prepare(strategyId, parameters, evaluationTimeframe, fills, exits);

        public Result<ResearchPort.IPreparedStrategy> PrepareStored(string sourceText, string expectedHash, IReadOnlyDictionary<string, double> parameters, Timeframe evaluationTimeframe, FillRules? fills, ExitChoice? exits = null) =>
            inner.PrepareStored(sourceText, expectedHash, parameters, evaluationTimeframe, fills, exits);

        public Result<ResearchPort.StrategyDescription> Describe(string sourceText) => inner.Describe(sourceText);

        public ComparisonReplay ReplayComparison(ResearchPort.IPreparedStrategy strategy, InstrumentReplayInput input) => inner.ReplayComparison(strategy, input);

        public InstrumentReplay Replay(ResearchPort.IPreparedStrategy strategy, InstrumentReplayInput input) => inner.Replay(strategy, input);

        public ExplainResult Explain(ResearchPort.IPreparedStrategy strategy, InstrumentReplayInput input, IReadOnlyList<ExplainRequest> requests)
        {
            var answer = inner.Explain(strategy, input, requests);
            return answer with { Replay = answer.Replay with { Trades = [.. answer.Replay.Trades.Select(t => t with { StopPrice = t.StopPrice + 1 })] } };
        }
    }

    private static void Tamper(IServiceCollection services)
    {
        var engine = services.Single(d => d.ServiceType == typeof(ResearchPort.ISimulationEngine));
        services.Remove(engine);
        services.AddSingleton<ResearchPort.ISimulationEngine>(sp => new TamperingEngine((ResearchPort.ISimulationEngine)ActivatorUtilities.CreateInstance(sp, engine.ImplementationType!)));
    }

    [Fact]
    public async Task Simulate_cards_carry_the_conditions_section_computed_by_the_real_engine_and_never_the_old_line_or_the_stored_run_line()
    {
        await InstrumentAsync("ABB", A());
        using var temp = new TempFolder();

        var outcome = await VerbAsync("--strategy ORB --symbols ABB " + Window + " --fills no-slippage --cards 3", temp);

        outcome.Exit.ShouldBe(0, outcome.Error);
        var cards = Directory.EnumerateFiles(temp.Root, "*.html", SearchOption.AllDirectories).Where(f => !f.EndsWith("index.html", StringComparison.Ordinal)).ToList();
        cards.ShouldNotBeEmpty();
        foreach (var card in cards)
        {
            var html = File.ReadAllText(card);
            html.ShouldContain("<h2>conditions at the decision bar</h2>");
            html.ShouldContain("<h3>trigger (all of)</h3>");
            html.ShouldContain("close (15m, lag 0) = ");
            html.ShouldContain("or_high(15) (15m, lag 0) = ");
            html.ShouldContain("close (15m, lag 1) = ");
            html.ShouldNotContain(OldLine);
            html.ShouldNotContain("not recomputed for a stored run");
        }
    }

    [Fact]
    public async Task Verify_pack_cards_carry_the_conditions_section_and_never_the_old_line()
    {
        await SeedPackDaysAsync();
        using var temp = new TempFolder();

        var outcome = await PackAsync("--strategies ORB --basket Lab " + PackWindow + " --count 3", temp);

        outcome.Exit.ShouldBe(0, outcome.Error);
        var cards = Directory.EnumerateFiles(Path.Combine(PackFolder(temp), "cards")).ToList();
        cards.ShouldNotBeEmpty();
        foreach (var card in cards)
        {
            var html = File.ReadAllText(card);
            html.ShouldContain("<h3>trigger (all of)</h3>");
            html.ShouldContain("close (15m, lag 0) = ");
            html.ShouldNotContain(OldLine);
            html.ShouldNotContain("not recomputed for a stored run");
        }
    }

    [Fact]
    public async Task An_engine_that_does_not_reproduce_the_trade_stops_simulate_with_exit_1_nothing_written_and_the_mismatch_logged()
    {
        await InstrumentAsync("ABB", A());
        using var temp = new TempFolder();

        var outcome = await VerbAsync("--strategy ORB --symbols ABB " + Window + " --fills no-slippage --cards 3", temp, configure: Tamper);

        outcome.Exit.ShouldBe(1, outcome.Error);
        outcome.Error.ShouldContain("StopPrice");
        outcome.Error.ShouldContain("NSE:ABB");
        outcome.Out.ShouldBeEmpty();
        Directory.Exists(temp.Root).ShouldBeFalse("nothing was written, not even the root");
        outcome.Logs.ShouldContain(l => l.Contains("does not give the recorded trade", StringComparison.Ordinal) && l.Contains("StopPrice", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_engine_that_does_not_reproduce_the_trade_stops_verify_pack_with_exit_1_and_nothing_written()
    {
        await SeedPackDaysAsync();
        using var temp = new TempFolder();

        var outcome = await PackAsync("--strategies ORB --basket Lab " + PackWindow + " --count 3", temp, configure: Tamper);

        outcome.Exit.ShouldBe(1, outcome.Error);
        outcome.Error.ShouldContain("StopPrice");
        Directory.Exists(temp.Root).ShouldBeFalse("nothing was written, not even the root");
        outcome.Logs.ShouldContain(l => l.Contains("does not give the recorded trade", StringComparison.Ordinal));
    }
}
