using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Market.Context;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.Research;

/// <summary>P3-lift (ADR 0145): the run key carries a fingerprint of the context days a run could read, only for a strategy that reads the context; a day after the window changes nothing.</summary>
public sealed class ContextRunKeyTests
{
    private static readonly DateOnly Monday = new(2026, 3, 2);

    private static readonly Guid Id = new("00000000-0000-0000-0000-000000000001");

    private static List<ContextDayRecord> Days(double vix = 15, double? breadth = null) =>
        [.. Enumerable.Range(0, 10).Select(i => new ContextDayRecord(Monday.AddDays(i - 10), 20_100, 19_900, 20_000 + i, vix + i, 375, 375, breadth))];

    private static RunHeader Header(string? context)
    {
        var strategy = new StrategyRecord("ORB", new Dictionary<string, double>(), "{}", new string('1', 64), Timeframe.M15, [Side.Buy]);
        var universe = new UniverseRecord(UniverseKind.InstrumentList, null, null, UniverseLabels.InstrumentsAsListed, [new UniverseMember(Id, "X")]);
        return new RunHeader(strategy, "sim-6", Monday, Monday.AddDays(4), ReplayOptions.Default, false, "{}", universe, new TradingCalendar([]), null, context);
    }

    private static string Key(RunHeader header)
    {
        var instrument = new InstrumentRun(
            Id, "X", null, null, Monday, Monday, Monday, new DateSpan(Monday, Monday), 0, 0, 0, 0, new string('b', 64), 0.05m, false, [], SessionCounts.Empty, [], false, [], [], WalkCounters.Empty, RunFates.Empty, 0, 0);
        return RunAssembler.Assemble(header, [new InstrumentReplay(instrument, [], [])]).RunKey;
    }

    [Fact]
    public void A_run_without_a_context_has_the_key_it_always_had()
    {
        var key = Key(Header(null));

        key.ShouldBe(Key(Header(null)));
        RunAssembler.KeyText(Header(null), [], []).ShouldNotContain("context=");
    }

    [Fact]
    public void A_run_with_a_context_has_a_different_key_that_follows_the_fingerprint()
    {
        var a = ContextFingerprint.Of(Days(), Monday.AddDays(4));
        var b = ContextFingerprint.Of(Days(vix: 16), Monday.AddDays(4));

        Key(Header(a)).ShouldNotBe(Key(Header(null)));
        Key(Header(a)).ShouldBe(Key(Header(a)));
        Key(Header(a)).ShouldNotBe(Key(Header(b)));
        RunAssembler.KeyText(Header(a), [], []).ShouldContain("context=" + a);
    }

    [Fact]
    public void Days_after_the_end_of_the_window_do_not_change_the_fingerprint_and_a_restated_breadth_does()
    {
        var through = Monday.AddDays(-4);
        var days = Days();
        var later = days.Append(new ContextDayRecord(Monday.AddDays(1), 20_100, 19_900, 20_050, 22, 375, 375)).ToList();

        ContextFingerprint.Of(later, through).ShouldBe(ContextFingerprint.Of(days, through));
        ContextFingerprint.Of(later, Monday.AddDays(1)).ShouldNotBe(ContextFingerprint.Of(days, Monday.AddDays(1)));
        ContextFingerprint.Of(Days(breadth: 55), through).ShouldNotBe(ContextFingerprint.Of(Days(breadth: 56), through));
        ContextFingerprint.Of(days.AsEnumerable().Reverse().ToList(), through).ShouldBe(ContextFingerprint.Of(days, through), "the order of the list does not matter");
        ContextFingerprint.Of([], through).ShouldBe(CanonicalText.Sha256Hex(string.Empty));
    }
}
