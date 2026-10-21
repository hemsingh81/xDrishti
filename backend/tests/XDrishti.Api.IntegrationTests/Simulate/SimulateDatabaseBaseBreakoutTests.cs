using System.Text.Json;
using XDrishti.Domain.Market.Bars;
using XDrishti.Tests.Shared;

namespace XDrishti.Api.IntegrationTests.Simulate;

/// <summary>
/// P3-06 against a real TimescaleDB (criteria 15 and 16): the invented series of the worked row P1 stored for one instrument, <c>BASE_BREAKOUT</c> run through the verb with a card, and the refusals of a timeframe the strategy does not declare. The numbers are those of
/// the spec's table P1 (decided Monday 2026-04-13, filled Tuesday 04-14 at the open 102.05, stop 99.40, target 107.35 reached on Thursday 04-16).
/// </summary>
public sealed partial class SimulateDatabaseTests
{
    private const string BaseWindow = "--from 2026-01-05 --to 2026-04-30 --gross";

    private static Bar[] P1Bars() => Day.Join(
        [
            .. BaseDays.ThroughSignal(),
            BaseDays.Ohlc(71, 102.05, 103.20, 101.80, 103.00),
            BaseDays.Ohlc(72, 103.00, 105.00, 102.90, 104.80),
            BaseDays.Ohlc(73, 104.80, 107.50, 104.60, 107.00),
        ]);

    [Fact]
    public async Task Base_breakout_through_the_verb_with_a_card_gives_the_trade_of_row_P1_and_changes_no_table()
    {
        var id = await InstrumentAsync("BASEB", P1Bars());
        await CreateBasketAsync("Bases", id);
        var before = await MarketRowCountsAsync();
        using var temp = new TempFolder();

        var outcome = await VerbAsync("--strategy BASE_BREAKOUT --basket Bases " + BaseWindow + " --param base_len=10 --cards 1", temp);

        outcome.Exit.ShouldBe(0, outcome.Error);
        (await MarketRowCountsAsync()).ShouldBe(before, "the verb writes nothing to the database");
        var folder = RunFolder(temp);
        var trade = Rows(folder).ShouldHaveSingleItem();
        (trade["strategy_id"], trade["symbol"], trade["side"], trade["trade_date"], trade["trigger_timeframe"]).ShouldBe(("BASE_BREAKOUT", "NSE:BASEB", "BUY", "2026-04-14", "1D"));
        (trade["entry_price"], trade["stop_price"], trade["target_price"], trade["exit_price"], trade["exit_reason"], trade["sessions_held"]).ShouldBe(("102.05", "99.4", "107.35", "107.35", "target", "3"));
        trade["key"].ShouldBe("BASE_BREAKOUT|NSE:BASEB|Buy|2026-04-13");
        double.Parse(trade["r_exact"], System.Globalization.CultureInfo.InvariantCulture).ShouldBe(2.0, 1e-9);

        using var json = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "run.json")));
        var text = json.RootElement.GetRawText();
        text.ShouldContain("\"engine_version\":\"sim-6\"");
        text.ShouldContain("\"parameters\":{\"base_len\":10,\"base_pct\":8,\"rr\":2,\"vol_x\":1.5}");

        var card = Directory.EnumerateFiles(Path.Combine(folder, "cards")).ShouldHaveSingleItem();
        var html = File.ReadAllText(card);
        html.ShouldContain("BASE_BREAKOUT");
        html.ShouldContain("2026-04-14");
        html.ShouldContain("entry 102.05");
        html.ShouldContain("stop 99.4");
        html.ShouldContain("target 107.35");
    }

    [Theory]
    [InlineData("15m", "strategy.model.timeframe_not_declared")]
    [InlineData("1W", "simulation.scanner_timeframe_unsupported")]
    public async Task A_timeframe_the_strategy_cannot_run_on_is_refused_with_a_located_problem_exit_1_and_nothing_exported(string timeframe, string code)
    {
        await InstrumentAsync("BASEB", P1Bars());
        using var temp = new TempFolder();

        var outcome = await VerbAsync("--strategy BASE_BREAKOUT --symbols BASEB " + BaseWindow + " --timeframe " + timeframe, temp);

        outcome.Exit.ShouldBe(1, outcome.Error);
        outcome.Error.ShouldContain(code);
        outcome.Out.ShouldBeEmpty();
        Directory.Exists(temp.Root).ShouldBeFalse("nothing was written, not even the root");
    }

    [Fact]
    public async Task A_parameter_outside_its_range_is_refused_before_a_bar_is_read()
    {
        await InstrumentAsync("BASEB", P1Bars());
        using var temp = new TempFolder();
        var reads = new NoReads();

        var outcome = await VerbAsync("--strategy BASE_BREAKOUT --symbols BASEB " + BaseWindow + " --param base_len=5", temp, reads);

        outcome.Exit.ShouldBe(1, outcome.Error);
        outcome.Error.ShouldContain("parameters.base_len: simulation.parameter_out_of_range: ");
        reads.Calls.ShouldBeEmpty();
        Directory.Exists(temp.Root).ShouldBeFalse();
    }
}
