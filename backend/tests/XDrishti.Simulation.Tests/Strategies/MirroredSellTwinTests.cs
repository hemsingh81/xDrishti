using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Replay;
using XDrishti.Simulation.Scanning;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.File;
using XDrishti.Strategies.Mirror;
using XDrishti.Strategies.Model;

namespace XDrishti.Simulation.Tests.Strategies;

/// <summary>Spec P3-02e, integration: a stored strategy with SELL listed and no sell block gives, on the mirrored day, the SELL candidates of its hand-written twin; the stored text and the Lab's check see the mirror.</summary>
public sealed class MirroredSellTwinTests
{
    public static readonly TheoryData<string> Ids = [.. IntradayBatchTests.Every.Select(r => r.Data)];

    private static string Text(string id) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Seed", id + ".yaml"));

    private static StrategyDefinition Load(string id) => StrategyFile.Read(Text(id)).Definition.ShouldNotBeNull();

    private static IReadOnlyList<EntryCandidate> Sell(StrategyDefinition definition, IEnumerable<Bar> bars) => Scan.Run(definition, Side.Sell, bars).Candidates;

    private static (string, Side, int, long, Bar, double) Key(EntryCandidate c) => (c.StrategyId, c.Side, c.DecisionBarIndex, c.DecisionEpochSeconds, c.TriggerBar, c.StopLevel);

    [Theory]
    [MemberData(nameof(Ids))]
    public void The_mirrored_sell_gives_the_candidates_of_the_hand_written_sell_on_the_mirrored_day(string id)
    {
        var hand = Load(id);
        var mirrored = StrategyMirror.Expand(hand with { Sell = null }).Definition;
        var bars = BandedDays.Mirror(IntradayBatchTests.Days(id));

        var expected = Sell(hand, bars);
        var actual = Sell(mirrored, bars);

        expected.ShouldNotBeEmpty();
        // same trades; the trace holds the text of the conditions, which differs only in spelling (the mirror prints the canonical text)
        actual.Select(Key).ShouldBe(expected.Select(Key));
    }

    [Fact]
    public void A_stored_text_without_a_sell_block_is_checked_with_the_applied_note_and_hashes_the_mirrored_block_while_the_text_is_not_rewritten()
    {
        var withoutSell = RemoveSell(Text("GAP_FILL"));

        var check = StoredStrategies.Check(withoutSell);
        var inspected = StoredStrategies.Inspect(withoutSell);

        check.Description.ShouldNotBeNull();
        check.Issues.Where(i => i.Code == MirrorCodes.Applied).ShouldHaveSingleItem().Message.ShouldContain("gap_pct >= gap_min and gap_pct <= gap_max");
        inspected.Description.ShouldNotBeNull().CanonicalJson.ShouldContain("\"sell_mirrored\":\"mirror-1\"");
        inspected.Description.Hash.ShouldNotBe(StoredStrategies.Inspect(Text("GAP_FILL")).Description!.Hash);
        StoredStrategies.Retarget(withoutSell, "OTHER")!.ShouldNotContain("sell:");
    }

    [Fact]
    public void A_refusal_is_a_located_error_in_the_check_and_stops_the_inspect()
    {
        var text = RemoveSell(Text("GAP_FILL")).Replace("close > today.open and close[1] <= today.open", "close > 100", StringComparison.Ordinal);

        var check = StoredStrategies.Check(text);

        check.Description.ShouldBeNull();
        check.Issues.Single(i => i.Code == MirrorCodes.AbsoluteLevel).Path.ShouldBe("sell.trigger[0]");
        StoredStrategies.Inspect(text).Description.ShouldBeNull();
    }

    [Fact]
    public void A_ticket_for_a_mirrored_sell_cell_prepares_with_the_hash_that_inspect_gives()
    {
        var text = RemoveSell(Text("GAP_FILL"));
        var hash = StoredStrategies.Inspect(text).Description.ShouldNotBeNull().Hash;

        var prepared = PreparedTicketStrategy.Prepare(text, hash, Timeframe.M15, Side.Sell, "intraday", "FIXED_RR");

        prepared.Problem.ShouldBeNull();
        prepared.StrategyId.ShouldBe("GAP_FILL");
    }

    [Fact]
    public void A_direction_specific_entry_kind_is_refused_for_the_mirrored_sell()
    {
        var text = RemoveSell(Text("GAP_FILL")).Replace("type: at_close", "type: stop_above_high", StringComparison.Ordinal);

        var check = StoredStrategies.Check(text);

        check.Issues.ShouldContain(i => i.Code == MirrorCodes.NotMirrorable && i.Path == "sell.entry");
        check.Description.ShouldBeNull();
    }

    private static string RemoveSell(string text)
    {
        var from = text.IndexOf("  sell:", StringComparison.Ordinal);
        var to = text.IndexOf("  time_rules:", StringComparison.Ordinal);
        return text[..from] + text[to..];
    }
}
