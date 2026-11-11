using System.Diagnostics;
using XDrishti.Domain.Selection;

namespace XDrishti.Domain.Tests.Selection;

/// <summary>BK-08 criteria 1 and 2 and the properties of the cluster kernel: average linkage on 1 - rho, unknown is not zero, ids are the smallest member and do not depend on the input order.</summary>
public sealed class CorrelationClustersTests
{
    private static readonly string[] Four = ["A", "B", "C", "D"];

    // The spec's example: A-B 0.85, C-D 0.75, B-C 0.72, A-C 0.40, A-D 0.30, B-D 0.20.
    private static CorrelationMatrix Example() => new([("A", "B", 0.85), ("C", "D", 0.75), ("B", "C", 0.72), ("A", "C", 0.40), ("A", "D", 0.30), ("B", "D", 0.20)]);

    private static string[][] Members(ClusterResult r) => [.. r.Clusters.Select(c => c.Members.ToArray())];

    [Fact]
    public void The_hand_example_gives_two_clusters_because_the_groups_join_at_an_average_of_0_405()
    {
        var r = CorrelationClusters.Compute(Example(), Four, new ClusterOptions(0.70));

        Members(r).ShouldBe([["A", "B"], ["C", "D"]]);
        r.Clusters.Select(c => c.Id).ShouldBe(["A", "C"]);
        r.Unknown.ShouldBeEmpty();
    }

    [Fact]
    public void Single_linkage_on_the_same_data_would_chain_into_one_cluster_which_is_not_used()
    {
        // Reference single linkage at the same cut: any pair at or above 0.70 joins (connected components). B-C at 0.72 chains A-B and C-D into one.
        var m = Example();
        var component = Four.ToDictionary(s => s, s => s);
        string Root(string s) => component[s] == s ? s : Root(component[s]);
        foreach (var a in Four)
        {
            foreach (var b in Four.Where(b => string.CompareOrdinal(a, b) < 0).Where(b => m.Get(a, b) >= 0.70))
            {
                component[Root(a)] = Root(b);
            }
        }

        Four.Select(Root).Distinct().Count().ShouldBe(1);
        CorrelationClusters.Compute(m, Four, new ClusterOptions(0.70)).Clusters.Count.ShouldBe(2);
    }

    [Fact]
    public void A_lower_cut_joins_the_groups_when_their_average_reaches_it()
    {
        var r = CorrelationClusters.Compute(Example(), Four, new ClusterOptions(0.40));

        Members(r).ShouldBe([["A", "B", "C", "D"]]);
    }

    [Fact]
    public void A_cut_of_one_gives_only_singletons()
    {
        var r = CorrelationClusters.Compute(Example(), Four, new ClusterOptions(1.0));

        Members(r).ShouldBe([["A"], ["B"], ["C"], ["D"]]);
        r.Unknown.ShouldBeEmpty();
    }

    [Fact]
    public void A_symbol_with_no_known_pair_is_a_singleton_flagged_unknown_never_joined_as_zero_or_one()
    {
        var r = CorrelationClusters.Compute(new CorrelationMatrix([("A", "B", 0.9)]), ["A", "B", "Z"], new ClusterOptions(0.70));

        Members(r).ShouldBe([["A", "B"], ["Z"]]);
        r.Unknown.ShouldBe(["Z"]);
        r.Of("Z")!.CorrelationUnknown.ShouldBeTrue();
        r.Of("A")!.CorrelationUnknown.ShouldBeFalse();
    }

    [Fact]
    public void A_missing_matrix_makes_every_symbol_unknown()
    {
        var r = CorrelationClusters.Compute(null, ["A", "B"], new ClusterOptions());

        r.Unknown.ShouldBe(["A", "B"]);
        r.Clusters.ShouldAllBe(c => c.Members.Count == 1);
    }

    [Fact]
    public void Out_of_range_and_not_a_number_pairs_are_unknown_not_a_correlation()
    {
        var r = CorrelationClusters.Compute(new CorrelationMatrix([("A", "B", double.NaN), ("A", "C", 1.5), ("B", "C", double.PositiveInfinity)]), ["A", "B", "C"], new ClusterOptions());

        Members(r).ShouldBe([["A"], ["B"], ["C"]]);
        r.Unknown.ShouldBe(["A", "B", "C"]);
    }

    [Fact]
    public void One_symbol_is_a_known_singleton_and_none_is_an_empty_result()
    {
        var one = CorrelationClusters.Compute(Example(), ["A"], new ClusterOptions());
        var none = CorrelationClusters.Compute(Example(), [], new ClusterOptions());

        Members(one).ShouldBe([["A"]]);
        one.Unknown.ShouldBeEmpty();
        none.Clusters.ShouldBeEmpty();
    }

    [Fact]
    public void Symbols_compare_ignoring_case_and_blanks_and_duplicates_count_once()
    {
        var r = CorrelationClusters.Compute(Example(), [" a", "A", "b"], new ClusterOptions());

        Members(r).ShouldBe([["A", "B"]]);
    }

    [Fact]
    public void Ids_and_clusters_do_not_depend_on_the_order_of_the_input()
    {
        var random = new Random(7);
        var symbols = Enumerable.Range(0, 12).Select(i => "S" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture)).ToList();
        var matrix = RandomMatrix(symbols, random);
        var expected = CorrelationClusters.Compute(matrix, symbols, new ClusterOptions(0.5));

        for (var round = 0; round < 20; round++)
        {
            var shuffled = symbols.OrderBy(_ => random.Next()).ToList();

            Members(CorrelationClusters.Compute(matrix, shuffled, new ClusterOptions(0.5))).ShouldBe(Members(expected));
        }
    }

    [Fact]
    public void Property_on_a_complete_matrix_each_cluster_averages_at_least_the_cut_and_two_clusters_average_below_it()
    {
        var random = new Random(11);
        for (var round = 0; round < 30; round++)
        {
            var symbols = Enumerable.Range(0, 10).Select(i => "S" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture)).ToList();
            var matrix = RandomMatrix(symbols, random);
            var cut = 0.3 + (random.NextDouble() * 0.4);

            var r = CorrelationClusters.Compute(matrix, symbols, new ClusterOptions(cut));

            foreach (var c in r.Clusters.Where(c => c.Members.Count > 1))
            {
                Average(matrix, c.Members, c.Members).ShouldBeGreaterThanOrEqualTo(cut - 1e-9);
            }

            foreach (var a in r.Clusters)
            {
                foreach (var b in r.Clusters.Where(x => string.CompareOrdinal(x.Id, a.Id) > 0))
                {
                    Average(matrix, a.Members, b.Members).ShouldBeLessThan(cut + 1e-9);
                }
            }
        }
    }

    [Fact(Explicit = true)]
    public void Sixty_symbols_cluster_in_under_twenty_milliseconds()
    {
        var symbols = Enumerable.Range(0, 60).Select(i => "S" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture)).ToList();
        var matrix = RandomMatrix(symbols, new Random(3));
        CorrelationClusters.Compute(matrix, symbols, new ClusterOptions());
        var best = TimeSpan.MaxValue;
        for (var i = 0; i < 3; i++)
        {
            var watch = Stopwatch.StartNew();
            CorrelationClusters.Compute(matrix, symbols, new ClusterOptions());
            best = TimeSpan.FromTicks(Math.Min(best.Ticks, watch.Elapsed.Ticks));
        }

        best.TotalMilliseconds.ShouldBeLessThan(20);
    }

    private static CorrelationMatrix RandomMatrix(List<string> symbols, Random random)
    {
        var pairs = new List<(string, string, double)>();
        for (var i = 0; i < symbols.Count; i++)
        {
            for (var j = i + 1; j < symbols.Count; j++)
            {
                pairs.Add((symbols[i], symbols[j], (random.NextDouble() * 1.6) - 0.6));
            }
        }

        return new CorrelationMatrix(pairs);
    }

    private static double Average(CorrelationMatrix m, IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        var values = a.SelectMany(x => b.Where(y => x != y).Select(y => m.Get(x, y)!.Value)).ToList();
        return values.Average();
    }
}
