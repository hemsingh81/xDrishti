using XDrishti.Application.Abstractions.Depth;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Depth;

namespace XDrishti.Application.Tests.Depth;

/// <summary>P7-16 criterion 5: armed tickets first (rank order, capped), then pinned, then the most liquid; distinct, capped, ties by symbol; unmapped symbols skipped and counted.</summary>
public sealed class DepthRecordingSetTests
{
    private static readonly string[] LiquidNames = ["L1", "L2", "L3"];

    private static InstrumentSecurity Security(string symbol, int id) => new(symbol, new Guid(id, 0, 0, new byte[8]), id.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static Dictionary<string, InstrumentSecurity> Securities(params string[] symbols) =>
        symbols.Select((s, i) => Security(s, 1000 + i)).ToDictionary(s => s.Symbol, StringComparer.Ordinal);

    private static ArmedTicket Armed(string symbol, int rank) => new(rank, 1, rank, symbol);

    [Fact]
    public void Armed_then_pinned_then_the_most_liquid_fill_the_set_in_that_order()
    {
        var securities = Securities("A1", "A2", "A3", "P1", "P2", "L1", "L2", "L3");
        var liquid = LiquidNames.Select((s, i) => new DepthTurnover(securities[s], 1000 - i)).ToList();

        var result = DepthRecordingSet.Build([Armed("A2", 2), Armed("A1", 1), Armed("A3", 3)], ["P1", "P2"], liquid, securities, 50, 20);

        result.Entries.Select(e => e.Symbol).ShouldBe(["A1", "A2", "A3", "P1", "P2", "L1", "L2", "L3"]);
        result.Entries.Select(e => e.Reason).ShouldBe(["armed", "armed", "armed", "pinned", "pinned", "turnover", "turnover", "turnover"]);
        result.Entries.Select(e => e.Rank).ShouldBe([1, 2, 3, 4, 5, 6, 7, 8]);
        result.Unmapped.ShouldBe(0);
    }

    [Fact]
    public void The_set_is_capped_and_the_armed_part_has_its_own_cap()
    {
        var securities = Securities([.. Enumerable.Range(0, 80).Select(i => $"S{i:00}")]);
        var armed = Enumerable.Range(0, 30).Select(i => Armed($"S{i:00}", i + 1)).ToList();
        var liquid = Enumerable.Range(30, 50).Select(i => new DepthTurnover(securities[$"S{i:00}"], 5000 - i)).ToList();

        var result = DepthRecordingSet.Build(armed, [], liquid, securities, 50, 20);

        result.Entries.Count.ShouldBe(50);
        result.Entries.Count(e => e.Reason == "armed").ShouldBe(20);
        result.Entries.Select(e => e.InstrumentId).Distinct().Count().ShouldBe(50);
        result.Entries[^1].Symbol.ShouldBe("S59"); // 20 armed + 30 liquid, the 31st liquid share does not fit
    }

    [Fact]
    public void A_share_named_twice_is_taken_once_by_its_first_reason()
    {
        var securities = Securities("A1", "L1");
        var liquid = new[] { new DepthTurnover(securities["A1"], 9), new DepthTurnover(securities["L1"], 8) };

        var result = DepthRecordingSet.Build([Armed("A1", 1)], ["A1", "L1"], liquid, securities, 50, 20);

        result.Entries.Select(e => (e.Symbol, e.Reason)).ShouldBe([("A1", "armed"), ("L1", "pinned")]);
    }

    [Fact]
    public void A_symbol_without_a_dhan_id_is_skipped_and_counted()
    {
        var securities = Securities("A1");

        var result = DepthRecordingSet.Build([Armed("GONE", 1), Armed("A1", 2)], ["NOPE"], [], securities, 50, 20);

        result.Entries.Select(e => e.Symbol).ShouldBe(["A1"]);
        result.Unmapped.ShouldBe(2);
    }

    [Fact]
    public void Liquid_ties_break_by_symbol()
    {
        var securities = Securities("ZED", "ABC", "MID");
        var liquid = new[] { new DepthTurnover(securities["ZED"], 10), new DepthTurnover(securities["ABC"], 10), new DepthTurnover(securities["MID"], 11) };

        var result = DepthRecordingSet.Build([], [], liquid, securities, 50, 20);

        result.Entries.Select(e => e.Symbol).ShouldBe(["MID", "ABC", "ZED"]);
    }

    [Fact]
    public void A_security_id_that_is_not_a_number_is_left_out()
    {
        var bad = new InstrumentSecurity("BAD", Guid.NewGuid(), "not-a-number");

        var result = DepthRecordingSet.Build([Armed("BAD", 1)], [], [], new Dictionary<string, InstrumentSecurity> { ["BAD"] = bad }, 50, 20);

        result.Entries.ShouldBeEmpty();
    }
}
