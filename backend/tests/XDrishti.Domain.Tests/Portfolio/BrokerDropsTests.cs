using System.Text.Json;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Domain.Tests.Portfolio;

/// <summary>The reasons rows were dropped (roadmap ISS-03): summed per reason over the windows of a run, the place of the first five kept, old stored counts read as empty.</summary>
public sealed class BrokerDropsTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly DateOnly D1 = new(2026, 9, 1);

    private static readonly DateOnly D2 = new(2026, 9, 30);

    private static BrokerDroppedRow Row(int index, string reason = "bad_price", int page = 0) => new(D1, D2, page, index, reason);

    private static BrokerDrops Drops(IReadOnlyDictionary<string, int> byReason, params BrokerDroppedRow[] rows) => new() { ByReason = byReason, Rows = rows };

    private static readonly Dictionary<string, int> None = [];

    [Fact]
    public void Windows_add_their_reasons_up_and_the_reasons_add_up_to_the_dropped_rows()
    {
        var counts = BrokerSyncCounts.None
            .AddWindow(10, 5, 0, 3, None, false, drops: Drops(new Dictionary<string, int> { ["bad_price"] = 2, ["missing_id"] = 1 }, Row(0), Row(1), Row(2, "missing_id")))
            .AddWindow(10, 5, 0, 2, None, false, drops: Drops(new Dictionary<string, int> { ["bad_price"] = 1, ["bad_time"] = 1 }, Row(4, "bad_time")));

        counts.Dropped.ShouldBe(5);
        counts.DroppedByReason.ShouldBe(new Dictionary<string, int> { ["bad_price"] = 3, ["bad_time"] = 1, ["missing_id"] = 1 });
        counts.DroppedByReason.Values.Sum().ShouldBe(counts.Dropped);
    }

    [Fact]
    public void Only_the_first_five_places_are_kept()
    {
        var rows = Enumerable.Range(0, 4).Select(i => Row(i)).ToArray();
        var counts = BrokerSyncCounts.None
            .AddWindow(4, 0, 0, 4, None, false, drops: Drops(new Dictionary<string, int> { ["bad_price"] = 4 }, rows))
            .AddWindow(4, 0, 0, 4, None, false, drops: Drops(new Dictionary<string, int> { ["bad_price"] = 4 }, [.. rows.Select(r => r with { Page = 1 })]));

        counts.DroppedRows.Count.ShouldBe(BrokerDrops.MaxLocations);
        counts.DroppedRows.Select(r => (r.Page, r.RowIndex)).ShouldBe([(0, 0), (0, 1), (0, 2), (0, 3), (1, 0)]);
    }

    [Fact]
    public void Dropped_rows_that_no_reason_explains_are_counted_as_other()
    {
        var counts = BrokerSyncCounts.None.AddWindow(5, 0, 0, 3, None, false, drops: Drops(new Dictionary<string, int> { ["bad_price"] = 1 }));

        counts.DroppedByReason.ShouldBe(new Dictionary<string, int> { ["bad_price"] = 1, [BrokerDrops.Other] = 2 });
        BrokerSyncCounts.None.AddWindow(5, 0, 0, 2, None, false).DroppedByReason.ShouldBe(new Dictionary<string, int> { [BrokerDrops.Other] = 2 });
        BrokerSyncCounts.None.AddWindow(5, 5, 0, 0, None, false).DroppedByReason.ShouldBeEmpty();
    }

    [Fact]
    public void Counts_stored_before_the_reasons_existed_read_as_empty_and_still_add_windows()
    {
        var old = """{"windowsDone":2,"windowsTotal":2,"halved":0,"read":9,"stored":4,"duplicates":1,"dropped":3,"enriched":0,"skipped":2,"emptyByNoData":0,"skippedByReason":{"bse":2},"rebuilt":1,"unchanged":0,"needsReview":0,"notRebuiltTotal":0,"notRebuilt":[]}""";

        var counts = JsonSerializer.Deserialize<BrokerSyncCounts>(old, Web)!;

        (counts.Dropped, counts.DroppedByReason.Count, counts.DroppedRows.Count).ShouldBe((3, 0, 0));
        counts.AddWindow(1, 0, 0, 1, None, false, drops: Drops(new Dictionary<string, int> { ["bad_time"] = 1 }, Row(0, "bad_time"))).DroppedByReason.ShouldBe(new Dictionary<string, int> { ["bad_time"] = 1 });
    }

    [Fact]
    public void The_reasons_and_places_survive_the_json_round_trip_with_the_wire_names()
    {
        var counts = BrokerSyncCounts.None.AddWindow(3, 0, 0, 2, None, false, drops: Drops(new Dictionary<string, int> { ["bad_price"] = 2 }, Row(7, page: 2), Row(9)));

        var json = JsonSerializer.Serialize(counts, Web);
        var back = JsonSerializer.Deserialize<BrokerSyncCounts>(json, Web)!;

        json.ShouldContain("\"droppedByReason\":{\"bad_price\":2}");
        json.ShouldContain("\"droppedRows\":[{\"windowFrom\":\"2026-09-01\",\"windowTo\":\"2026-09-30\",\"page\":2,\"rowIndex\":7,\"reason\":\"bad_price\"}");
        back.DroppedByReason.ShouldBe(counts.DroppedByReason);
        back.DroppedRows.ShouldBe(counts.DroppedRows);
    }

    [Fact]
    public void Adding_a_reason_without_a_place_changes_only_the_counts()
    {
        var drops = BrokerDrops.None.Plus("failed_checks", 2).Plus("failed_checks", 1).Plus("store_refused", 0);

        drops.ByReason.ShouldBe(new Dictionary<string, int> { ["failed_checks"] = 3 });
        (drops.Total, drops.Rows.Count).ShouldBe((3, 0));
    }
}
