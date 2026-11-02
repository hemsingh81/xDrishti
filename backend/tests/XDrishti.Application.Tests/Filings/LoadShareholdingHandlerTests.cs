using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Filings;
using XDrishti.Domain.Filings;

namespace XDrishti.Application.Tests.Filings;

/// <summary>P15-04 criteria 7, 8, 10 for the shareholding loader.</summary>
public sealed class LoadShareholdingHandlerTests
{
    private const string Header = "symbol,period_end,category,pct,pledged_pct,broadcast_ts\n";

    private const string Ts = "2026-07-20T18:00:00+05:30";

    private readonly FilingWorld _world = new();

    private readonly FakeShareholdingStore _store = new();

    private LoadShareholdingHandler Handler() => new(_world.Source, _world.Instruments, _store, new FakeTimeProvider(new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero)), NullLogger<LoadShareholdingHandler>.Instance);

    private async Task<RowLoadSummary> RunAsync() => (await Handler().HandleAsync(new LoadShareholdingCommand(), TestContext.Current.CancellationToken)).Value;

    private void Drop(string csv, string? name = null) => _world.Add("*", FilingKind.Shareholding, null, null, csv, name ?? $"holding-{_world.Source.Accepted.Count}.csv", BroadcastState.RowLevelTs);

    private static string Group(string symbol, string period, string ts, params (string Category, string Pct)[] lines) =>
        string.Join("\n", lines.Select(l => $"{symbol},{period},{l.Category},{l.Pct},{(l.Category == "promoter" && l.Pct.Length > 0 ? "10" : string.Empty)},{ts}"));

    [Fact]
    public async Task A_group_of_four_categories_is_one_envelope_with_four_lines_and_a_rerun_inserts_nothing()
    {
        Drop(Header + Group("RELIANCE", "2026-06-30", Ts, ("promoter", "50.5"), ("fii", "20"), ("dii", "15"), ("public", "14.5")));

        var first = await RunAsync();
        var second = await RunAsync();

        var stored = _store.Groups.ShouldHaveSingleItem();
        stored.Lines.Count.ShouldBe(4);
        (stored.Filing.Kind, stored.Filing.PeriodEnd, stored.Filing.BroadcastTs, stored.Filing.State).ShouldBe((FilingKind.Shareholding, new DateOnly(2026, 6, 30), new DateTimeOffset(2026, 7, 20, 12, 30, 0, TimeSpan.Zero), BroadcastState.Ok));
        (first.Loaded, first.SumOff, second.Loaded, second.Duplicate).ShouldBe((1, 0, 0, 1));
    }

    [Fact]
    public async Task A_blank_pct_is_stored_unavailable_and_a_group_off_100_is_stored_and_counted_sum_off()
    {
        Drop(Header + Group("RELIANCE", "2026-06-30", Ts, ("promoter", ""), ("fii", "20")) + "\n" + Group("TCS", "2026-06-30", Ts, ("promoter", "30"), ("fii", "20"), ("dii", "10"), ("public", "30")));

        var summary = await RunAsync();

        summary.Loaded.ShouldBe(2);
        summary.SumOff.ShouldBe(1);
        _store.Groups.Single(g => g.Filing.InstrumentId == FilingWorld.Reliance).Lines[0].IsValue.ShouldBeFalse();
        _store.Groups.Single(g => g.Filing.InstrumentId == FilingWorld.Tcs).Lines.Select(l => l.Pct).ShouldBe([30m, 20m, 10m, 30m]);
    }

    [Fact]
    public async Task Rejections_are_counted_by_reason_and_a_bad_row_never_stops_the_good_ones()
    {
        Drop(Header + string.Join("\n",
            Group("RELIANCE", "2026-06-30", Ts, ("promoter", "150"), ("fii", "20")),
            Group("NOSUCH", "2026-06-30", Ts, ("fii", "5")),
            Group("TCS", "2026-06-30", Ts, ("promoter", "10")),
            "TCS,2026-06-30,fii,5,,2026-07-21T18:00:00+05:30"));

        var summary = await RunAsync();

        summary.Rejected.ShouldBe(new Dictionary<string, int> { ["out_of_range"] = 1, ["inconsistent_ts"] = 2, ["unknown_instrument"] = 1 });
        summary.Loaded.ShouldBe(1);
        _store.Groups.ShouldHaveSingleItem().Lines.Select(l => l.Category).ShouldBe(["fii"]);
    }

    [Fact]
    public async Task A_later_group_of_the_same_period_supersedes_the_earlier_one_and_an_equal_time_with_other_content_is_refused()
    {
        Drop(Header + Group("RELIANCE", "2026-06-30", Ts, ("promoter", "50")), "a.csv");
        Drop(Header + Group("RELIANCE", "2026-06-30", "2026-08-20T18:00:00+05:30", ("promoter", "52")), "b.csv");
        Drop(Header + Group("RELIANCE", "2026-06-30", "2026-08-20T18:00:00+05:30", ("promoter", "53")), "c.csv");

        var summary = await RunAsync();

        (summary.Loaded, summary.Revisions).ShouldBe((2, 1));
        summary.Rejected.ShouldBe(new Dictionary<string, int> { ["ambiguous_order"] = 1 });
        _store.Groups[1].Filing.Supersedes.ShouldBe(_store.Groups[0].Filing.Id);
    }

    [Fact]
    public async Task A_group_without_a_usable_time_is_stored_unavailable_and_counted()
    {
        Drop(Header + Group("RELIANCE", "2026-06-30", "", ("promoter", "50")));

        var summary = await RunAsync();

        summary.Unavailable.ShouldBe(1);
        _store.Groups.ShouldHaveSingleItem().Filing.State.ShouldBe(BroadcastState.Unavailable);
    }

    [Fact]
    public async Task A_bad_header_and_an_unreadable_file_are_counted_and_the_next_file_still_loads()
    {
        Drop("symbol,pct\nA,1", "bad.csv");
        var gone = _world.Add("*", FilingKind.Shareholding, null, null, "x", "gone.csv", BroadcastState.RowLevelTs);
        _world.Source.Content.Remove(gone.Sha256);
        Drop(Header + Group("TCS", "2026-06-30", Ts, ("fii", "5")), "ok.csv");

        var summary = await RunAsync();

        (summary.Loaded, summary.Failures).ShouldBe((1, 1));
        summary.Rejected["bad_header"].ShouldBe(1);
    }

    [Fact]
    public async Task The_counts_line_has_numbers_and_codes_only()
    {
        Drop(Header + Group("RELIANCE", "2026-06-30", Ts, ("promoter", "10"), ("fii", "20"), ("dii", "10"), ("public", "10")));

        (await RunAsync()).Text.ShouldBe("shareholding: files 1, loaded 1 (revisions 0, out of order 0, without an intimation time 0), duplicate 0, rejected 0, sum off 1");
    }
}
