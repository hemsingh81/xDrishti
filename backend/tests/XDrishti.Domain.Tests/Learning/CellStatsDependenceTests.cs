using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>CF-01 acceptance criteria 9, 12 and 13: effective n on the cell rows, the entry cluster on the trade, and no leak past the as-of date.</summary>
public sealed class CellStatsDependenceTests
{
    private static readonly DateOnly AsOf = new(2026, 3, 31);

    private static readonly CellStatsOptions Options = new(RecencyHalfLifeDays: 30, PriorStrength: 10, MinTrades: 30);

    private static CellId Cell() => new("ORB", "15m", "BUY", "intraday", "FIXED_RR");

    /// <summary>34 trades in 7 entry sessions (several per day), exits the next day.</summary>
    private static List<CellTrade> Clustered(int sessions = 7, int perSession = 5)
    {
        var rng = new Random(5);
        var trades = new List<CellTrade>();
        for (var d = 0; d < sessions; d++)
        {
            var level = (d % 2 == 0 ? 0.8 : -0.8) + (d * 0.05);
            for (var i = 0; i < perSession; i++)
            {
                var entry = new DateOnly(2026, 1, 5).AddDays(d * 4);
                trades.Add(new CellTrade(entry.AddDays(1), level + ((rng.NextDouble() - 0.5) * 0.3), 0.5, 1, entry));
            }
        }

        return trades;
    }

    private static CellStatsRow All(IReadOnlyList<CellStatsRow> rows) => rows.First(r => r.Window == CellWindow.All);

    [Fact]
    public void Without_the_confidence_options_the_rows_are_exactly_what_they_were_and_carry_nothing()
    {
        var input = new CellInput(Cell(), true, Clustered());

        var plain = CellStatsCalculator.Compute([input], AsOf, Options);
        var off = CellStatsCalculator.Compute([input], AsOf, Options, null, new ConfidenceOptions(Mode: ConfidenceMode.Off));

        plain.ShouldAllBe(r => r.Dependence == null);
        off.Count.ShouldBe(plain.Count);
        for (var i = 0; i < plain.Count; i++)
        {
            off[i].ShouldBe(plain[i]);
        }
    }

    [Fact]
    public void Report_only_adds_the_block_and_leaves_every_other_field_and_Enough_unchanged()
    {
        var input = new CellInput(Cell(), true, Clustered(7, 5));   // 35 trades: enough by count (30), few by independence
        var plain = All(CellStatsCalculator.Compute([input], AsOf, Options));

        var report = All(CellStatsCalculator.Compute([input], AsOf, Options, null, new ConfidenceOptions()));

        (report with { Dependence = null }).ShouldBe(plain);
        report.Enough.ShouldBeTrue();
        report.Dependence.ShouldNotBeNull();
        report.Dependence!.NEff.ShouldNotBeNull();
        report.Dependence.NEff!.Value.ShouldBeLessThan(35);
        report.IndependentN.ShouldBe(report.Dependence.NEff);
        report.Dependence.EnoughByNEff.ShouldBe(false);
        report.Dependence.N.ShouldBe(35);
    }

    [Fact]
    public void Gate_changes_Enough_and_nothing_else()
    {
        var input = new CellInput(Cell(), true, Clustered(7, 5));
        var report = All(CellStatsCalculator.Compute([input], AsOf, Options, null, new ConfidenceOptions()));

        var gate = All(CellStatsCalculator.Compute([input], AsOf, Options, null, new ConfidenceOptions(Mode: ConfidenceMode.Gate)));

        gate.Enough.ShouldBeFalse();
        (gate with { Enough = true, Dependence = report.Dependence }).ShouldBe(report);
    }

    [Fact]
    public void Gate_with_effective_n_unavailable_is_not_enough()
    {
        var few = new CellInput(Cell(), true, Clustered(1, 3));   // 3 trades: below the 5 needed for an estimate

        var gate = All(CellStatsCalculator.Compute([few], AsOf, new CellStatsOptions(MinTrades: 1), null, new ConfidenceOptions(Mode: ConfidenceMode.Gate)));

        gate.Dependence!.NEff.ShouldBeNull();
        gate.Dependence.Note.ShouldBe("unavailable: n < 5");
        gate.Enough.ShouldBeFalse();
    }

    [Fact]
    public void A_snapshot_recomputed_from_the_same_trades_gives_the_same_numbers()
    {
        var input = new CellInput(Cell(), true, Clustered());

        var a = CellStatsCalculator.Compute([input], AsOf, Options, null, new ConfidenceOptions());
        var b = CellStatsCalculator.Compute([input], AsOf, Options, null, new ConfidenceOptions());

        b.Select(r => r.Dependence).ShouldBe(a.Select(r => r.Dependence));
    }

    [Fact]
    public void A_trade_built_with_four_arguments_clusters_by_exit_date_and_the_row_says_none()
    {
        var old = Clustered().Select(t => new CellTrade(t.ExitDate, t.R, t.MaeR, t.MfeR)).ToList();

        var row = All(CellStatsCalculator.Compute([new CellInput(Cell(), true, old)], AsOf, Options, null, new ConfidenceOptions()));

        old[0].EntryDate.ShouldBeNull();
        old[0].HasEntryCluster.ShouldBeFalse();
        old[0].ClusterKey.ShouldBe(old[0].ExitDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        row.Dependence!.Method.ShouldBe("none");
        row.Dependence.NEff.ShouldBeNull();
        row.Dependence.Note.ShouldContain("entry dates not carried");
        row.Dependence.NeededTrades.ShouldNotBeNull();   // power does not depend on the cluster
        row.Enough.ShouldBeTrue();
    }

    [Fact]
    public void The_cluster_key_is_the_entry_session_date()
    {
        var t = new CellTrade(new DateOnly(2026, 1, 7), 1, 0, 0, new DateOnly(2026, 1, 5));

        t.ClusterKey.ShouldBe("2026-01-05");
        t.HasEntryCluster.ShouldBeTrue();
    }

    [Fact]
    public void Effective_n_at_an_as_of_date_reads_only_trades_that_exited_on_or_before_it()
    {
        var trades = Clustered(7, 5);
        var cutoff = trades[19].ExitDate;   // the 20 first trades exit by then
        var withFuture = new CellInput(Cell(), true, trades);
        var onlyKnown = new CellInput(Cell(), true, [.. trades.Where(t => t.ExitDate <= cutoff)]);

        var a = All(CellStatsCalculator.Compute([withFuture], cutoff, Options, null, new ConfidenceOptions()));
        var b = All(CellStatsCalculator.Compute([onlyKnown], cutoff, Options, null, new ConfidenceOptions()));

        a.N.ShouldBe(20);
        a.Dependence.ShouldBe(b.Dependence);
    }
}
