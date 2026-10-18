using Microsoft.Extensions.Logging;
using NSubstitute;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Tests.Research;

/// <summary>Criterion 10 of P3-04a: a run writes exactly the three kinds of line of D5 and none of them holds a price, a bar or the text of a strategy; the phase times come from the time provider.</summary>
public sealed class RunSimulationLoggingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Type[] AllowedArgumentTypes = [typeof(string), typeof(int), typeof(long), typeof(DateOnly), typeof(Guid)];

    private static async Task<(RunFixture Fixture, SimulationRun Run)> RunWithTwoSkipsAsync()
    {
        var fixture = new RunFixture();
        var good = fixture.Add("NSE:ABB", RunFixture.ThreeDays());
        var empty = fixture.Add("NSE:EMPTY");
        var missing = Guid.CreateVersion7();
        fixture.BarQueries.FindInstrumentAsync(missing, Arg.Any<CancellationToken>()).Returns((BarsInstrument?)null);
        fixture.Engine.Warnings.Add(new XDrishti.Domain.Common.FieldError("buy.trigger[0]", "a warning", Code: "strategy.lint.x"));
        fixture.Store.OnRead = _ => fixture.Time.Advance(TimeSpan.FromMilliseconds(30));
        fixture.Engine.OnReplay = _ => fixture.Time.Advance(TimeSpan.FromMilliseconds(70));
        var result = await fixture.RunAsync(RunFixture.List([good, empty, missing]), Ct);
        return (fixture, result.Value);
    }

    [Fact]
    public async Task A_run_writes_one_start_line_one_line_per_skip_and_one_finish_line_in_that_order()
    {
        var (fixture, run) = await RunWithTwoSkipsAsync();

        fixture.Log.Events.Select(e => e.Id).ShouldBe([4402, 4403, 4403, 4404]);
        fixture.Log.Events.ShouldAllBe(e => e.Level == LogLevel.Information);
        fixture.Log.Events[0].Argument("StrategyId").ShouldBe("ORB");
        fixture.Log.Events[0].Argument("Universe").ShouldBe("instruments as listed");
        fixture.Log.Events[0].Argument("Instruments").ShouldBe(3);
        fixture.Log.Events[0].Argument("Warnings").ShouldBe(1);
        fixture.Log.Events[1].Arguments.Select(a => a.Value).ShouldBe(["NSE:EMPTY", run.Instruments[1].InstrumentId, "no_bars"]);
        fixture.Log.Events[2].Arguments.Select(a => a.Value).ShouldBe(["?", run.Instruments[2].InstrumentId, "instrument_not_found"]);
        var finish = fixture.Log.Events[3];
        finish.Argument("RunKey").ShouldBe(run.RunKey);
        (finish.Argument("Run"), finish.Argument("Skipped"), finish.Argument("Trades")).ShouldBe((1, 2, 0));
    }

    [Fact]
    public async Task The_times_of_the_phases_are_those_of_the_time_provider()
    {
        var (fixture, _) = await RunWithTwoSkipsAsync();

        var finish = fixture.Log.Events[^1];
        finish.Argument("ReadMs").ShouldBe(60L, "two reads of 30 ms; the instrument without a row is not read");
        finish.Argument("ReplayMs").ShouldBe(140L, "two replays of 70 ms");
        finish.Argument("AssemblyMs").ShouldBe(0L);
    }

    [Fact]
    public async Task No_line_holds_a_price_a_bar_or_strategy_text_only_ids_symbols_counts_reasons_and_milliseconds()
    {
        var (fixture, _) = await RunWithTwoSkipsAsync();

        foreach (var line in fixture.Log.Events)
        {
            line.Arguments.ShouldAllBe(a => a.Value != null && AllowedArgumentTypes.Contains(a.Value.GetType()), "no double or decimal reaches the log: a price would be one");
            line.Template.ShouldNotContain("bar", Case.Insensitive);
            line.Template.ShouldNotContain("price", Case.Insensitive);
            line.Template.ShouldNotContain("definition", Case.Insensitive);
            line.Message.ShouldNotContain("{");
        }
    }

    [Fact]
    public async Task A_request_that_is_refused_writes_no_line()
    {
        var fixture = new RunFixture();

        await fixture.RunAsync(RunFixture.List([]), Ct);

        fixture.Log.Events.ShouldBeEmpty();
    }
}
