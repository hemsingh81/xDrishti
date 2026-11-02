using System.Text.Json;

namespace XDrishti.Domain.Tests.Assistant.Evaluation;

/// <summary>P15-07 criteria 5 and 7: the labelled file of set D follows announcements.schema.json, and an invalid line is named.</summary>
public sealed class AnnouncementSetTests
{
    private static readonly LabelledAnnouncement Row = new("a001", "TCS", "2026-08-03T05:30:00.000000Z", "nse-announcement", "The board \"approved\" it.\nSecond line.", true, "Results");

    [Fact]
    public void Written_rows_read_back_and_validate_against_the_schema_file()
    {
        var text = AnnouncementSet.Write([Row, Row with { Id = "a002", Material = false, Category = null }]);

        AnnouncementSet.Parse(text).Value.ShouldBe([Row, Row with { Id = "a002", Material = false, Category = null }]);
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "eval", "thresholds.json")))
        {
            dir = dir.Parent;
        }

        using var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "eval", "sets", "announcements.schema.json")));
        var required = schema.RootElement.GetProperty("required").EnumerateArray().Select(e => e.GetString()!).ToList();
        var properties = schema.RootElement.GetProperty("properties");
        var labels = properties.GetProperty("label").GetProperty("enum").EnumerateArray().Select(e => e.GetString()).ToList();
        foreach (var line in text.TrimEnd().Split('\n'))
        {
            using var json = JsonDocument.Parse(line);
            var keys = json.RootElement.EnumerateObject().Select(p => p.Name).ToList();
            required.ShouldAllBe(k => keys.Contains(k));
            keys.ShouldAllBe(k => properties.EnumerateObject().Any(p => p.Name == k));
            labels.ShouldContain(json.RootElement.GetProperty("label").GetString());
            json.RootElement.GetProperty("source").GetString().ShouldBe("nse-announcement");
        }
    }

    [Theory]
    [InlineData("""{"id":"a","symbol":"S","published_at":"2026-08-03T05:30:00Z","source":"x","text":"t"}""")]
    [InlineData("""{"id":"a","symbol":"S","published_at":"2026-08-03T05:30:00Z","source":"x","text":"t","label":"unsure"}""")]
    [InlineData("""{"id":"a","symbol":"S","published_at":"2026-08-03T05:30:00Z","source":"x","text":"","label":"material"}""")]
    [InlineData("""{"id":"a","symbol":"S","published_at":"soon","source":"x","text":"t","label":"material"}""")]
    [InlineData("""{"id":"a","symbol":"S","published_at":"2026-08-03T05:30:00Z","source":"x","text":"t","label":"material","extra":1}""")]
    [InlineData("""{"id":"a","symbol":"S","published_at":"2026-08-03T05:30:00Z","source":"x","text":"t","label":"material","label":"material"}""")]
    [InlineData("[1]")]
    [InlineData("not json")]
    public void A_line_that_is_not_a_schema_row_fails_and_the_message_names_the_line_not_the_text(string line)
    {
        var good = AnnouncementSet.Write([Row]).TrimEnd();

        var result = AnnouncementSet.Parse(good + "\n" + line + "\n");

        result.IsFailure.ShouldBeTrue();
        result.Error.Message.ShouldContain("line 2");
        result.Error.Message.ShouldNotContain("approved");
    }

    [Fact]
    public void A_duplicate_id_fails_and_blank_lines_are_skipped()
    {
        var one = AnnouncementSet.Write([Row]).TrimEnd();

        AnnouncementSet.Parse(one + "\n\n" + one + "\n").Error.Message.ShouldContain("duplicate id");
        AnnouncementSet.Parse("\n" + one + "\n\n").Value.Count.ShouldBe(1);
    }
}
