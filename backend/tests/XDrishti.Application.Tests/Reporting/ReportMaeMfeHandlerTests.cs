using XDrishti.Application.Reporting;

namespace XDrishti.Application.Tests.Reporting;

/// <summary>P8-10: the excursion read and the sector lens through the loader.</summary>
public sealed class ReportMaeMfeHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ReportWorld World()
    {
        var world = new ReportWorld();
        world.AddRun('a');
        world.AddTrades('a', Enumerable.Range(0, 30).Select(i => i % 2 == 0 ? 1.0 : -1.0));
        for (var i = 0; i < world.Trades.Count; i++)
        {
            world.Trades[i] = world.Trades[i] with { MaeR = 0.5, MfeR = 1.5 };
        }

        return world;
    }

    [Fact]
    public async Task The_excursion_read_returns_counts_that_sum_to_the_trades()
    {
        var world = World();
        var query = ReportWorld.Query();

        var result = await new GetReportMaeMfeHandler(world.Loader).HandleAsync(new GetReportMaeMfeQuery(query), Ct);

        result.IsSuccess.ShouldBeTrue();
        var group = result.Value.Groups.Single();
        group.ExcursionN.ShouldBe(30);
        group.Grid!.Counts.Sum(r => r.Sum()).ShouldBe(30);
        group.ReachedMfe[0].Count.ShouldBe(30);
        group.WinnersMaeP90.Number.ShouldBe(0.5);
        result.Value.Evidence.N.ShouldBe(30);
    }

    [Fact]
    public async Task The_sector_lens_reads_the_master_and_leaves_unclassified_instruments_unrecorded()
    {
        var world = World();
        var unknown = await new GetReportGroupsHandler(world.Loader).HandleAsync(new GetReportGroupsQuery(ReportWorld.Query(new ReportQueryInput(Lens: "sector"))), Ct);
        world.Sectors[Guid.Empty] = "Banks";
        var known = await new GetReportGroupsHandler(world.Loader).HandleAsync(new GetReportGroupsQuery(ReportWorld.Query(new ReportQueryInput(Lens: "sector"))), Ct);

        unknown.Value.Groups.Single().Parts.Single().Value.ShouldBeNull();
        known.Value.Groups.Single().Parts.Single().Value.ShouldBe("Banks");
    }
}
