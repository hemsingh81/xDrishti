using NSubstitute;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Market.Context;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Tests.Research;

/// <summary>P3-lift (ADR 0145): the handler reads the stored context days once for a strategy that reads the context, gives the same list to every instrument, and puts their fingerprint in the run key; any other strategy reads nothing and keeps its key.</summary>
public sealed class RunSimulationContextTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static List<ContextDayRecord> Days() =>
        [.. Enumerable.Range(0, 5).Select(i => new ContextDayRecord(new DateOnly(2026, 2, 23).AddDays(i), 20_100, 19_900, 20_000 + i, 15 + i, 375, 375))];

    private static string KeyOf(SimulationRun run, string? context)
    {
        var header = new RunHeader(run.Strategy, run.EngineVersion, run.From, run.To, run.Options, run.AcknowledgeCsvAdjusted, run.Fills, run.Universe, new TradingCalendar(), null, context);
        return CanonicalText.Sha256Hex(RunAssembler.KeyText(header, run.Instruments, run.CalendarExceptions));
    }

    [Fact]
    public async Task A_context_strategy_gets_the_days_once_the_same_list_for_every_instrument_and_a_key_with_their_fingerprint()
    {
        var fixture = new RunFixture();
        fixture.Engine.ReadsContext = true;
        var days = Days();
        fixture.ContextDays.ListAsync(Arg.Any<CancellationToken>()).Returns(days);
        var a = fixture.Add("NSE:AAA", RunFixture.ThreeDays());
        var b = fixture.Add("NSE:BBB", RunFixture.ThreeDays());

        var result = await fixture.RunAsync(RunFixture.List([a, b], RunFixture.D1, RunFixture.D3), Ct);

        result.IsSuccess.ShouldBeTrue();
        await fixture.ContextDays.Received(1).ListAsync(Arg.Any<CancellationToken>());
        fixture.Engine.Replays.Count.ShouldBe(2);
        fixture.Engine.Replays.ShouldAllBe(input => ReferenceEquals(input.ContextDays, days) && input.ContextNote == null);
        result.Value.RunKey.ShouldBe(KeyOf(result.Value, ContextFingerprint.Of(days, RunFixture.D3)));
        result.Value.RunKey.ShouldNotBe(KeyOf(result.Value, null));
    }

    [Fact]
    public async Task No_stored_day_is_told_to_the_engine_as_no_context()
    {
        var fixture = new RunFixture();
        fixture.Engine.ReadsContext = true;
        fixture.ContextDays.ListAsync(Arg.Any<CancellationToken>()).Returns(new List<ContextDayRecord>());
        var a = fixture.Add("NSE:AAA", RunFixture.ThreeDays());

        var result = await fixture.RunAsync(RunFixture.List([a], RunFixture.D1, RunFixture.D3), Ct);

        result.IsSuccess.ShouldBeTrue();
        fixture.Engine.Replays.Single().ContextDays.ShouldBeEmpty();
        fixture.Engine.Replays.Single().ContextNote.ShouldBe("no_context");
    }

    [Fact]
    public async Task A_strategy_that_reads_no_context_reads_no_days_and_keeps_its_key()
    {
        var fixture = new RunFixture();
        var a = fixture.Add("NSE:AAA", RunFixture.ThreeDays());

        var result = await fixture.RunAsync(RunFixture.List([a], RunFixture.D1, RunFixture.D3), Ct);

        result.IsSuccess.ShouldBeTrue();
        await fixture.ContextDays.DidNotReceive().ListAsync(Arg.Any<CancellationToken>());
        fixture.Engine.Replays.ShouldAllBe(input => input.ContextDays == null && input.ContextNote == null);
        result.Value.RunKey.ShouldBe(KeyOf(result.Value, null));
    }
}
