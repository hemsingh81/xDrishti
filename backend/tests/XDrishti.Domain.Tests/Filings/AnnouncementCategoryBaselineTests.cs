using XDrishti.Domain.Filings;

namespace XDrishti.Domain.Tests.Filings;

/// <summary>P15-07 criterion 6: the baseline is a pure lookup; a category the file does not list is non_material.</summary>
public sealed class AnnouncementCategoryBaselineTests
{
    private const string Json = """{"version":1,"categories":{"Results":"material","Trading Window":"non_material","Credit  Rating":"material"}}""";

    [Theory]
    [InlineData("Results", "material")]
    [InlineData("  results ", "material")]
    [InlineData("CREDIT RATING", "material")]
    [InlineData("Trading Window", "non_material")]
    [InlineData("Unlisted Category", "non_material")]
    [InlineData("Result", "non_material")]
    [InlineData("Results and more", "non_material")]
    [InlineData("", "non_material")]
    [InlineData(null, "non_material")]
    public void Predict_is_a_lookup_by_the_whole_name_and_unknown_is_non_material(string? category, string expected)
    {
        AnnouncementCategoryBaseline.Parse(Json).Value.Predict(category).ShouldBe(expected);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"version":2,"categories":{}}""")]
    [InlineData("""{"version":1}""")]
    [InlineData("""{"version":1,"categories":{"A":"maybe"}}""")]
    [InlineData("""{"version":1,"categories":{"A":"material","a ":"non_material"}}""")]
    [InlineData("""{"version":1,"categories":{"  ":"material"}}""")]
    [InlineData("""{"version":1,"categories":{"A":1}}""")]
    public void A_file_that_is_not_a_baseline_is_refused(string json)
    {
        var parsed = AnnouncementCategoryBaseline.Parse(json);

        parsed.IsFailure.ShouldBeTrue();
        parsed.Error.Code.ShouldBe("eval.baseline_invalid");
    }

    [Fact]
    public void The_hash_follows_the_text_and_ignores_the_line_ending()
    {
        var a = AnnouncementCategoryBaseline.Parse("{\n\"version\":1,\"categories\":{}}").Value;
        var b = AnnouncementCategoryBaseline.Parse("{\r\n\"version\":1,\"categories\":{}}").Value;
        var c = AnnouncementCategoryBaseline.Parse("{\n\"version\":1,\"categories\":{\"A\":\"material\"}}").Value;

        a.Sha256.ShouldBe(b.Sha256);
        a.Sha256.ShouldNotBe(c.Sha256);
        a.Sha256.Length.ShouldBe(64);
    }

    [Fact]
    public void The_committed_baseline_parses_and_holds_the_starting_list_of_the_spec()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "eval", "thresholds.json")))
        {
            dir = dir.Parent;
        }

        var baseline = AnnouncementCategoryBaseline.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "eval", "sets", "announcement-baseline.json"))).Value;

        foreach (var material in new[] { "Results", "Outcome of Board Meeting with results", "Credit Rating", "Order Win / Award of Contract", "Acquisition", "Merger or Demerger", "Fund Raising", "Change in Auditor", "Change of KMP at Board level", "Litigation or Regulatory Action", "Default" })
        {
            baseline.Predict(material).ShouldBe("material", material);
        }

        foreach (var other in new[] { "Trading Window", "Newspaper Publication", "Certificates under the Depository Regulations", "Investor Meet Intimation", "Shareholder Meeting Notices", "Loss of Share Certificates" })
        {
            baseline.Predict(other).ShouldBe("non_material", other);
        }

        baseline.Predict("A category nobody listed").ShouldBe("non_material");
    }
}
