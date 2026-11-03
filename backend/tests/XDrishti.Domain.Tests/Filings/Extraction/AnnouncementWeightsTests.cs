using XDrishti.Domain.Filings;

namespace XDrishti.Domain.Tests.Filings.Extraction;

/// <summary>P15-08 (left open by P15-07): the inclusion weight is recomputed from the labelled rows per stratum, so a reserve row that replaced an unsure one does not carry the weight of the drawn sample.</summary>
public sealed class AnnouncementWeightsTests
{
    private static LabelledAnnouncement Row(string id, string category, string at) => new(id, "TCS", at, "nse-announcement", "text", true, category);

    private static SampleFileRow Sampled(string id, string half) => new(id, Guid.NewGuid(), half, false, 100, 4, 25.0, new string('0', 64));

    [Fact]
    public void The_weight_is_the_strata_eligible_count_over_the_labelled_rows_now_in_it()
    {
        // The sample drew 4 rows of the stratum (weight 100 / 4 = 25); one was unsure and replaced by a reserve row of another stratum, so 3 labelled rows remain in it.
        var sample = new AnnouncementSampleFile("1", 1337, "2026-01-01", "2026-12-31", new string('b', 64), 1000, 600, 400, 0, 200, 40,
            [new SampleStratum("material", "Results", 2026, 100, 4), new SampleStratum("other", "Meeting", 2025, 300, 1)],
            [Sampled("a001", "material"), Sampled("a002", "material"), Sampled("a003", "material"), Sampled("a004", "material"), Sampled("a201", "other")]);
        var labelled = new[]
        {
            Row("a001", "Results", "2026-03-03T05:30:00.000000Z"), Row("a002", "Results", "2026-03-04T05:30:00.000000Z"), Row("a003", "Results", "2026-05-05T05:30:00.000000Z"),
            Row("a201", "Meeting", "2025-06-01T05:30:00.000000Z"),
        };

        var weights = AnnouncementWeights.Recompute(sample, labelled);

        weights["a001"].ShouldBe(100.0 / 3, 1e-5);
        weights["a002"].ShouldBe(100.0 / 3, 1e-5);
        weights["a003"].ShouldBe(100.0 / 3, 1e-5);
        weights["a201"].ShouldBe(300.0, 1e-9);
    }

    [Fact]
    public void The_india_year_decides_the_stratum()
    {
        var sample = new AnnouncementSampleFile("1", 1337, "2026-01-01", "2027-12-31", new string('b', 64), 10, 10, 0, 0, 200, 40,
            [new SampleStratum("material", "Results", 2026, 100, 2), new SampleStratum("material", "Results", 2027, 40, 1)],
            [Sampled("a001", "material"), Sampled("a002", "material")]);
        var labelled = new[] { Row("a001", "Results", "2026-06-01T05:30:00.000000Z"), Row("a002", "Results", "2026-12-31T20:00:00.000000Z") };

        var weights = AnnouncementWeights.Recompute(sample, labelled);

        weights["a001"].ShouldBe(100.0, 1e-9);
        weights["a002"].ShouldBe(40.0, 1e-9);
    }

    [Fact]
    public void A_row_with_no_known_stratum_or_no_sample_row_gets_weight_one_and_never_a_guess()
    {
        var sample = new AnnouncementSampleFile("1", 1337, "2026-01-01", "2026-12-31", new string('b', 64), 10, 10, 0, 0, 200, 40, [new SampleStratum("material", "Results", 2026, 100, 1)], [Sampled("a001", "material")]);
        var labelled = new[] { Row("a001", "Other category", "2026-06-01T05:30:00.000000Z"), Row("zzz", "Results", "2026-06-01T05:30:00.000000Z"), Row("a001b", "Results", "not a date") };

        var weights = AnnouncementWeights.Recompute(sample, labelled);

        weights.Values.ShouldAllBe(w => w == 1.0);
        weights.Count.ShouldBe(3);
    }
}
