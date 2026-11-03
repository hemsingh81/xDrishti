using XDrishti.Domain.Lab;
using XDrishti.Domain.PairResearch;

namespace XDrishti.Domain.Tests.Pairs.Study;

/// <summary>P14-13 criterion 2: one synthetic sector of four shares where one pair is constructed cointegrated (the P14-12 fixture) and the rest are independent walks: the cointegrated pair is selected, the others are not, the Benjamini-Hochberg table is printed, an unavailable test counts in m.</summary>
public sealed class PairFormationTests
{
    private const int N = 252;

    private static double[] Prices(IEnumerable<double> ln) => [.. ln.Select(Math.Exp)];

    private static List<PairMember> Sector(ulong seed)
    {
        var rng = new SeededRandom(seed);
        var x = PairsData.Walk(rng, N).Select(v => v + 4.6).ToArray();
        var u = PairsData.Ar1(rng, N, 0.85, 0.003);
        var y = x.Select((v, i) => 1.0 + (0.8 * v) + u[i]).ToArray();
        var c = PairsData.Walk(rng, N).Select(v => v + 4.5).ToArray();
        var d = PairsData.Walk(rng, N).Select(v => v + 4.7).ToArray();
        return
        [
            new PairMember("NSE:COINT_Y", "Banks", Prices(y), 9),
            new PairMember("NSE:COINT_X", "Banks", Prices(x), 8),
            new PairMember("NSE:INDEP_C", "Banks", Prices(c), 7),
            new PairMember("NSE:INDEP_D", "Banks", Prices(d), 6),
        ];
    }

    private static List<PairCandidate> AllPairs(List<PairMember> members)
    {
        var list = new List<PairCandidate>();
        for (var i = 0; i < members.Count; i++)
        {
            for (var j = i + 1; j < members.Count; j++)
            {
                list.Add(new PairCandidate(members[i].Symbol, members[j].Symbol, i, j, 0.9));
            }
        }

        return list;
    }

    [Fact]
    public void The_cointegrated_pair_is_selected_and_the_independent_ones_are_not()
    {
        var members = Sector(7);

        var formation = PairFormation.Form(members, AllPairs(members), PairStudyOptions.Default);

        formation.Selected.Select(s => s.Key).ShouldBe(["NSE:COINT_Y|NSE:COINT_X"]);
        formation.M.ShouldBe(6);
        formation.Unavailable.ShouldBe(0);
        formation.Rejected.ShouldBe(1);
        var selected = formation.Selected.Single();
        selected.Beta.ShouldBe(0.8, 0.1);
        selected.HalfLifeSessions.ShouldBeInRange(1.0, 20.0);
        selected.SpreadMean.ShouldBe(0.0, 1e-9);
        selected.PValue.ShouldBeLessThan(0.01);
    }

    [Fact]
    public void The_benjamini_hochberg_table_lists_every_candidate_by_p_value_with_its_threshold_and_is_printed()
    {
        var members = Sector(7);

        var formation = PairFormation.Form(members, AllPairs(members), PairStudyOptions.Default);
        var table = formation.Table();

        table.Count.ShouldBe(7);
        table[0].ShouldContain("threshold(rank*q/6)");
        formation.Rows.Select(r => r.Rank).ShouldBe([1, 2, 3, 4, 5, 6]);
        formation.Rows[0].Threshold.ShouldBe(0.05 / 6, 1e-12);
        formation.Rows[5].Threshold.ShouldBe(0.05, 1e-12);
        formation.Rows[0].Selected.ShouldBeTrue();
        formation.Rows.Skip(1).ShouldAllBe(r => !r.Selected && r.NotSelected == "not_rejected");
        formation.Rows.Select(r => r.PValue!.Value).ShouldBe(formation.Rows.Select(r => r.PValue!.Value).Order());
        table[1].ShouldContain("selected");
    }

    [Fact]
    public void An_unavailable_test_is_counted_in_m_with_p_equal_to_one_and_is_never_selected()
    {
        var members = Sector(7);
        members.Add(new PairMember("NSE:FLAT", "Banks", Enumerable.Repeat(100.0, N).ToArray(), 1));
        var candidates = AllPairs(members);

        var formation = PairFormation.Form(members, candidates, PairStudyOptions.Default);

        formation.M.ShouldBe(10);
        formation.Unavailable.ShouldBe(4);
        formation.Tested.ShouldBe(6);
        formation.Rows.Count(r => r.PValue is null).ShouldBe(4);
        formation.Rows.Where(r => r.PValue is null).ShouldAllBe(r => r.Reason != null && r.NotSelected == "test_unavailable" && !r.Selected);
        formation.Rows[0].Threshold.ShouldBe(0.05 / 10, 1e-12);
        formation.Selected.Select(s => s.Key).ShouldBe(["NSE:COINT_Y|NSE:COINT_X"]);
    }

    [Fact]
    public void Changing_a_daily_close_inside_the_window_can_change_the_selection_the_control_of_the_look_ahead_property()
    {
        var members = Sector(7);
        var baseline = PairFormation.Form(members, AllPairs(members), PairStudyOptions.Default);
        var broken = members.ToList();
        var x = broken[1].Closes.ToArray();
        for (var i = 120; i < N; i++)
        {
            x[i] *= 1.6;
        }

        broken[1] = broken[1] with { Closes = x };

        var after = PairFormation.Form(broken, AllPairs(broken), PairStudyOptions.Default);

        baseline.Selected.Count.ShouldBe(1);
        after.Selected.ShouldBeEmpty();
    }

    [Fact]
    public void The_same_closes_give_the_same_formation_and_the_same_stored_detail()
    {
        var one = Sector(7);
        var two = Sector(7);
        var exclusions = new Dictionary<string, int> { ["capped"] = 0 };

        var a = PairFormation.Form(one, AllPairs(one), PairStudyOptions.Default).DetailJson("as of today", "as of today", "unavailable", exclusions);
        var b = PairFormation.Form(two, AllPairs(two), PairStudyOptions.Default).DetailJson("as of today", "as of today", "unavailable", exclusions);

        a.ShouldBe(b);
        a.ShouldContain("\"ban_list\":\"unavailable\"");
        a.ShouldContain("\"p_value_basis\":\"asymptotic\"");
    }

    [Fact]
    public void A_stored_detail_has_the_text_unavailable_and_no_digit_for_a_missing_statistic()
    {
        var members = new List<PairMember>
        {
            new("NSE:A", "Banks", Enumerable.Repeat(100.0, N).ToArray(), 2),
            new("NSE:B", "Banks", Enumerable.Repeat(100.0, N).ToArray(), 1),
        };

        var formation = PairFormation.Form(members, AllPairs(members), PairStudyOptions.Default);
        var json = formation.DetailJson("as of today", "as of today", "unavailable", new Dictionary<string, int>());

        formation.Selected.ShouldBeEmpty();
        json.ShouldContain("\"tau\":\"unavailable\"");
        json.ShouldContain("\"p\":\"unavailable\"");
        json.ShouldNotContain("NaN");
        json.ShouldNotContain("Infinity");
    }
}
