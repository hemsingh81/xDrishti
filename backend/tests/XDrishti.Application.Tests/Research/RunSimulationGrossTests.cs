using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Tests.Research;

/// <summary>
/// Criterion 20 of P3-03c: the handler, the request and the engine port are not changed, so a run through the handler is gross until P3-03f gives the request a cost choice: the replay input carries no cost context, the record no
/// <see cref="CostRecord"/>, the trades <see cref="CostBasis.None"/> and no <see cref="SimTrade.Costing"/>, the key text the line <c>costs=none</c> and the third label <c>costs: none</c>.
/// </summary>
public sealed class RunSimulationGrossTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_run_through_the_handler_is_gross_and_says_so_in_the_record_the_key_and_the_labels()
    {
        var fixture = new RunFixture();
        var abb = fixture.Add("NSE:ABB", RunFixture.ThreeDays());
        fixture.Engine.Script = input => new InstrumentReplay(FakeSimulationEngine.Record(input, null), [FakeSimulationEngine.Trade(input, RunFixture.D2)], []);

        var result = await fixture.RunAsync(RunFixture.List([abb], RunFixture.D1, RunFixture.D3), Ct);

        result.IsSuccess.ShouldBeTrue();
        var run = result.Value;
        fixture.Engine.Replays.ShouldAllBe(input => input.Costs == null, "the handler gives the engine no cost context");
        run.Costs.ShouldBeNull();
        run.SchemaVersion.ShouldBe(6);
        run.Trades.ShouldNotBeEmpty();
        run.Trades.Concat(run.OpenAtEnd).ShouldAllBe(t => t.Trade.Costs == CostBasis.None && t.Trade.Costing == null);
        run.Totals.Fates.SkippedQuantity.ShouldBe(0);
        run.Totals.Fates.SkippedCost.ShouldBe(0);
        var header = new RunHeader(run.Strategy, run.EngineVersion, run.From, run.To, run.Options, run.AcknowledgeCsvAdjusted, run.Fills, run.Universe, new TradingCalendar());
        var keyText = RunAssembler.KeyText(header, run.Instruments, run.CalendarExceptions);
        keyText.ShouldContain("\ncosts=none\n");
        CanonicalText.Sha256Hex(keyText).ShouldBe(run.RunKey);
        RunLabels.Of(run, FillRules.Default).Lines[3].ShouldBe("costs: none");
    }
}
