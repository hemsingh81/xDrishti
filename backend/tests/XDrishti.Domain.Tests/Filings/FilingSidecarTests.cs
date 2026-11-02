using System.Text;
using XDrishti.Domain.Filings;

namespace XDrishti.Domain.Tests.Filings;

public sealed class FilingSidecarTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private const string Good = """{"schema":1,"symbol":"RELIANCE","kind":"results","period_end":"2026-06-30","broadcast_ts":"2026-07-24T16:12:00+05:30","source_ref":"note https://example.test/x"}""";

    private static Domain.Common.Result<FilingSidecar> Parse(string json, string extension = ".xml") => FilingSidecar.Parse(Encoding.UTF8.GetBytes(json), extension, Now);

    [Fact]
    public void A_complete_sidecar_is_read()
    {
        var sidecar = Parse(Good).Value;

        sidecar.Symbol.ShouldBe("RELIANCE");
        sidecar.Kind.ShouldBe(FilingKind.Results);
        sidecar.PeriodEnd.ShouldBe(new DateOnly(2026, 6, 30));
        sidecar.Broadcast.State.ShouldBe(BroadcastState.Ok);
        sidecar.SourceRef.ShouldBe("note https://example.test/x");
    }

    [Theory]
    [InlineData("""{"schema":2,"symbol":"A","kind":"announcement"}""", "bad_schema")]
    [InlineData("""{"symbol":"A","kind":"announcement"}""", "bad_schema")]
    [InlineData("""{"schema":1,"symbol":"A","kind":"news"}""", "bad_kind")]
    [InlineData("""{"schema":1,"kind":"announcement"}""", "bad_symbol")]
    [InlineData("""{"schema":1,"symbol":"abc","kind":"announcement"}""", "bad_symbol")]
    [InlineData("""{"schema":1,"symbol":"*","kind":"results","period_end":"2026-06-30"}""", "bad_symbol")]
    [InlineData("""{"schema":1,"symbol":"A","kind":"results"}""", "bad_period_end")]
    [InlineData("""{"schema":1,"symbol":"A","kind":"results","period_end":"30-06-2026"}""", "bad_period_end")]
    [InlineData("""{"schema":1,"symbol":"A","kind":"shareholding"}""", "bad_period_end")]
    [InlineData("""{"schema":1,"symbol":"A","kind":"announcement","extra":1}""", "unknown_key")]
    [InlineData("""{"schema":1,"schema":1,"symbol":"A","kind":"announcement"}""", "duplicate_key")]
    [InlineData("""{"schema":1,"symbol":"A","kind":"announcement"} x""", "invalid_json")]
    [InlineData("""[1]""", "not_an_object")]
    [InlineData("", "invalid_json")]
    [InlineData("not json", "invalid_json")]
    [InlineData("""{"schema":1,"symbol":"A","kind":"announcement",}""", "invalid_json")]
    public void The_first_problem_is_a_closed_code(string json, string code)
    {
        var result = Parse(json);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(code);
    }

    [Fact]
    public void A_source_ref_of_201_characters_is_refused()
    {
        Parse($$"""{"schema":1,"symbol":"A","kind":"announcement","source_ref":"{{new string('x', 201)}}"}""").Error.Code.ShouldBe("bad_source_ref");
    }

    [Fact]
    public void A_sidecar_above_4_kb_is_refused_before_it_is_read()
    {
        FilingSidecar.Parse(new byte[4097], ".xml", Now).Error.Code.ShouldBe("sidecar_too_large");
    }

    [Fact]
    public void Invalid_utf8_is_refused()
    {
        byte[] bytes = [.. Encoding.UTF8.GetBytes("""{"schema":1,"symbol":"A","kind":"announcement","source_ref":"x"""), 0xFF, 0xFE, .. Encoding.UTF8.GetBytes("\"}")];

        FilingSidecar.Parse(bytes, ".xml", Now).Error.Code.ShouldBe("invalid_json");
    }

    [Theory]
    [InlineData("""{"schema":1,"symbol":"A","kind":"announcement"}""")]
    [InlineData("""{"schema":1,"symbol":"A","kind":"announcement","broadcast_ts":null}""")]
    [InlineData("""{"schema":1,"symbol":"A","kind":"announcement","broadcast_ts":5}""")]
    [InlineData("""{"schema":1,"symbol":"A","kind":"announcement","broadcast_ts":"2026-07-24"}""")]
    public void A_missing_or_unusable_broadcast_time_keeps_the_filing_as_unavailable(string json)
    {
        var sidecar = Parse(json).Value;

        sidecar.Broadcast.State.ShouldBe(BroadcastState.Unavailable);
        sidecar.Broadcast.Instant.ShouldBeNull();
    }

    [Fact]
    public void A_csv_of_board_meetings_may_name_all_companies_and_carries_its_times_per_row()
    {
        var sidecar = Parse("""{"schema":1,"symbol":"*","kind":"board_meeting"}""", ".csv").Value;

        sidecar.Symbol.ShouldBe("*");
        sidecar.Broadcast.State.ShouldBe(BroadcastState.RowLevelTs);
        sidecar.Broadcast.Instant.ShouldBeNull();
    }

    [Fact]
    public void A_csv_of_shareholding_needs_no_period_end_but_an_xml_one_does()
    {
        Parse("""{"schema":1,"symbol":"A","kind":"shareholding"}""", ".csv").IsSuccess.ShouldBeTrue();
        Parse("""{"schema":1,"symbol":"A","kind":"shareholding"}""", ".xml").IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void A_leading_byte_order_mark_is_accepted()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(Good)];

        FilingSidecar.Parse(bytes, ".xml", Now).IsSuccess.ShouldBeTrue();
    }
}
