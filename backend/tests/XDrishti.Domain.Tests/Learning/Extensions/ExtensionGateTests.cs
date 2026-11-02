using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Domain.Tests.Learning.Extensions;

/// <summary>P15-10 criterion 10: the extraction record gates the verdict, before any number is computed.</summary>
public sealed class ExtensionGateTests
{
    private const string Run = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string Clean = "{\"nan\":{\"a\":{}},\"parser_unverified\":{\"a\":0,\"b\":0}}";

    private static ExtensionExtractionRecord Record(string version = "fu-1", string? parser = "xbrl-1", string counts = Clean, int lag = 30, int rows = 10) =>
        new(Run, "fu", version, lag, parser, new string('a', 64), null, rows, counts, DateTimeOffset.UnixEpoch);

    private static IReadOnlyList<string> Check(ExtensionExtractionRecord? record, string version = "fu-1", int closed = 10) =>
        ExtensionGate.Problems(version, [(Run, closed, record)], 30, "xbrl-1");

    [Fact]
    public void A_verified_current_record_passes()
    {
        Check(Record()).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("xbrl-1-synthetic")]
    public void A_missing_empty_or_synthetic_parser_version_is_parser_unverified(string? parser)
    {
        Check(Record(parser: parser)).ShouldHaveSingleItem().ShouldStartWith("parser_unverified");
    }

    [Fact]
    public void Any_feature_built_on_an_unverified_parser_is_parser_unverified_even_with_a_verified_version()
    {
        Check(Record(counts: "{\"nan\":{},\"parser_unverified\":{\"a\":0,\"b\":3}}")).ShouldHaveSingleItem().ShouldContain("3 values");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"nan\":{}}")]
    [InlineData("{\"parser_unverified\":{\"a\":\"x\"}}")]
    public void Counts_that_cannot_be_read_fail_closed(string counts)
    {
        Check(Record(counts: counts)).ShouldHaveSingleItem().ShouldStartWith("parser_unverified");
    }

    [Fact]
    public void The_event_set_reads_no_parser_so_it_is_gated_only_by_staleness()
    {
        ExtensionGate.Problems("ev-1", [(Run, 10, Record("ev-1", parser: null))], 30, "xbrl-1").ShouldBeEmpty();
    }

    [Theory]
    [InlineData("lag")]
    [InlineData("rows")]
    [InlineData("version")]
    [InlineData("parser")]
    [InlineData("none")]
    public void A_record_that_does_not_match_the_run_or_the_current_versions_is_features_stale(string what)
    {
        var problems = what switch
        {
            "lag" => Check(Record(lag: 60)),
            "rows" => Check(Record(rows: 9)),
            "version" => Check(Record("ev-1")),
            "parser" => Check(Record(parser: "xbrl-0")),
            _ => Check(null),
        };

        problems.ShouldHaveSingleItem().ShouldStartWith("features_stale");
    }

    [Fact]
    public void Unverified_reasons_come_before_stale_ones()
    {
        var problems = ExtensionGate.Problems("fu-1", [(Run, 10, Record(lag: 99)), (Run, 10, Record(parser: "xbrl-1-synthetic"))], 30, "xbrl-1");

        problems[0].ShouldStartWith("parser_unverified");
    }
}
