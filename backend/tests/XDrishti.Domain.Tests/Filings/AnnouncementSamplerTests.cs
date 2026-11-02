using XDrishti.Domain.Filings;

namespace XDrishti.Domain.Tests.Filings;

/// <summary>P15-07 criterion 4: the sample is the same whatever the insert order, half of it comes from baseline-material categories, every non-empty stratum has a place when places allow, and too few announcements refuse naming the count.</summary>
public sealed class AnnouncementSamplerTests
{
    private static readonly AnnouncementCategoryBaseline Baseline =
        AnnouncementCategoryBaseline.Parse("""{"version":1,"categories":{"Results":"material","Credit Rating":"material","Acquisition":"material","Trading Window":"non_material"}}""").Value;

    private static readonly string[] Categories = ["Results", "Credit Rating", "Acquisition", "Trading Window", "Newspaper Publication", "Investor Meet"];

    private static List<SampleCandidate> World(int count, int seed = 1, params string[] categories)
    {
        var random = new Random(seed);
        var cats = categories.Length > 0 ? categories : Categories;
        var rows = new List<SampleCandidate>();
        for (var i = 0; i < count; i++)
        {
            var id = new Guid(random.Next(), 1, 2, [1, 2, 3, 4, 5, 6, 7, (byte)(i % 250)]);
            rows.Add(new SampleCandidate(id, "SYM" + (i % 17), new DateTimeOffset(2023 + random.Next(0, 3), random.Next(1, 13), random.Next(1, 28), random.Next(4, 12), 0, 0, TimeSpan.Zero), cats[random.Next(cats.Length)]));
        }

        return rows;
    }

    private static string Fingerprint(AnnouncementSamplePlan plan) =>
        string.Join("|", plan.Rows.Select(r => $"{r.Id}/{r.Candidate.FilingId}/{r.Half}/{r.Reserve}/{r.Weight}")) + "#" + string.Join("|", plan.Strata.Select(s => $"{s.Half}/{s.Category}/{s.Year}/{s.Eligible}/{s.Sampled}"));

    [Fact]
    public void The_same_announcements_seed_and_baseline_give_the_same_sample_in_any_insert_order()
    {
        var world = World(3000);
        var expected = Fingerprint(AnnouncementSampler.Sample(world, Baseline, 1337).Value);

        for (var shuffle = 0; shuffle < 12; shuffle++)
        {
            var shuffled = world.OrderBy(_ => new Random(shuffle).Next()).OrderBy(c => c.FilingId.GetHashCode() ^ shuffle).ToList();
            Fingerprint(AnnouncementSampler.Sample(shuffled, Baseline, 1337).Value).ShouldBe(expected);
        }

        Fingerprint(AnnouncementSampler.Sample([.. world.AsEnumerable().Reverse()], Baseline, 1337).Value).ShouldBe(expected);
    }

    [Fact]
    public void Another_seed_gives_another_sample()
    {
        var world = World(3000);

        Fingerprint(AnnouncementSampler.Sample(world, Baseline, 1).Value).ShouldNotBe(Fingerprint(AnnouncementSampler.Sample(world, Baseline, 2).Value));
    }

    [Fact]
    public void It_draws_240_with_200_main_first_then_40_reserve_and_half_from_the_material_categories()
    {
        var plan = AnnouncementSampler.Sample(World(3000), Baseline, 1337).Value;

        plan.Rows.Count.ShouldBe(240);
        plan.Rows.Select(r => r.Id).ShouldBe(Enumerable.Range(1, 240).Select(i => $"a{i:000}"));
        plan.Rows.Take(200).ShouldAllBe(r => !r.Reserve);
        plan.Rows.Skip(200).ShouldAllBe(r => r.Reserve);
        plan.Rows.Count(r => r.Half == "material").ShouldBe(120);
        plan.Rows.Count(r => r.Half == "material" && !r.Reserve).ShouldBe(100);
        plan.Rows.Count(r => r.Half == "other" && !r.Reserve).ShouldBe(100);
        plan.Rows.Where(r => r.Half == "material").ShouldAllBe(r => Baseline.IsMaterial(r.Candidate.Category));
        plan.Rows.Where(r => r.Half == "other").ShouldAllBe(r => !Baseline.IsMaterial(r.Candidate.Category));
        plan.Rows.Select(r => r.Candidate.FilingId).Distinct().Count().ShouldBe(240);
    }

    [Fact]
    public void Every_non_empty_stratum_gets_a_place_when_places_allow_and_the_rest_follow_the_stratum_sizes()
    {
        var plan = AnnouncementSampler.Sample(World(6000, 3, "Results", "Credit Rating", "Trading Window", "Newspaper Publication"), Baseline, 1337).Value;

        plan.Strata.ShouldAllBe(s => s.Sampled >= 1 && s.Sampled <= s.Eligible);
        plan.Strata.Where(s => s.Half == "material").Sum(s => s.Sampled).ShouldBe(120);
        plan.Strata.Where(s => s.Half == "other").Sum(s => s.Sampled).ShouldBe(120);
        plan.Strata.Sum(s => s.Eligible).ShouldBe(6000);
        foreach (var half in new[] { "material", "other" })
        {
            var strata = plan.Strata.Where(s => s.Half == half).OrderByDescending(s => s.Eligible).ToList();
            strata[0].Sampled.ShouldBeGreaterThanOrEqualTo(strata[^1].Sampled);
        }

        plan.Rows.ShouldAllBe(r => Math.Abs(r.Weight - (double)r.StratumEligible / r.StratumSampled) < 1e-5);
    }

    [Fact]
    public void With_more_strata_than_places_the_choice_is_by_hash_not_by_order_and_the_sample_is_still_240()
    {
        var categories = Enumerable.Range(0, 120).Select(i => "Other " + i).ToArray();
        var world = World(8000, 5, categories);
        var expected = Fingerprint(AnnouncementSampler.Sample(world, Baseline, 7).Value);

        var plan = AnnouncementSampler.Sample([.. world.AsEnumerable().Reverse()], Baseline, 7).Value;

        Fingerprint(plan).ShouldBe(expected);
        plan.Rows.Count.ShouldBe(240);
        plan.Strata.Any(s => s.Sampled == 0).ShouldBeTrue();
    }

    [Fact]
    public void A_half_with_fewer_than_its_share_gives_all_it_has_and_the_other_half_makes_up_the_difference()
    {
        var world = World(60, 9, "Results").Concat(World(400, 11, "Trading Window").Select(c => c with { FilingId = Guid.NewGuid() })).ToList();

        var plan = AnnouncementSampler.Sample(world, Baseline, 1337).Value;

        plan.Rows.Count.ShouldBe(240);
        plan.Rows.Count(r => r.Half == "material").ShouldBe(60);
        plan.Rows.Count(r => r.Half == "other").ShouldBe(180);
        (plan.EligibleMaterial, plan.EligibleOther).ShouldBe((60, 400));
    }

    [Fact]
    public void Fewer_than_240_refuse_with_the_count_named()
    {
        var result = AnnouncementSampler.Sample(World(239), Baseline, 1337);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("eval.too_few_announcements");
        result.Error.Message.ShouldContain("239");
        AnnouncementSampler.Sample(World(240), Baseline, 1337).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void The_year_is_the_india_year_of_the_broadcast()
    {
        AnnouncementSampler.YearOf(new DateTimeOffset(2025, 12, 31, 20, 0, 0, TimeSpan.Zero)).ShouldBe(2026);
        AnnouncementSampler.YearOf(new DateTimeOffset(2025, 12, 31, 18, 0, 0, TimeSpan.Zero)).ShouldBe(2025);
    }

    [Fact]
    public void Sampling_160000_announcements_is_fast()
    {
        var world = World(160_000, 21);
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var plan = AnnouncementSampler.Sample(world, Baseline, 1337).Value;

        clock.Elapsed.TotalSeconds.ShouldBeLessThan(5);
        plan.Rows.Count.ShouldBe(240);
    }
}
