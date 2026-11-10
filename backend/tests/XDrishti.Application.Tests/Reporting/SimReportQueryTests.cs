using XDrishti.Application.Reporting;

namespace XDrishti.Application.Tests.Reporting;

/// <summary>AU-14: the purpose and the job of the one query model. Every refusal is <c>reports.invalid</c>; a training query is gross, names no runs and carries no run labels.</summary>
public sealed class SimReportQueryTests
{
    private const string Job = "aaaaaaaa-0000-0000-0000-000000000001";

    [Fact]
    public void Without_a_purpose_the_query_is_the_evidence_query_as_before()
    {
        ReportQuery.Parse(new ReportQueryInput()).Value.Sim.ShouldBeNull();
        ReportQuery.Parse(new ReportQueryInput(Purpose: "research")).Value.Sim.ShouldBeNull();
    }

    [Fact]
    public void Training_takes_a_job_or_none_and_a_range()
    {
        var named = ReportQuery.Parse(new ReportQueryInput(Purpose: "training", SimJob: Job, From: "2026-01-01", To: "2026-02-01", Symbol: "NSE:REL")).Value;
        var current = ReportQuery.Parse(new ReportQueryInput(Purpose: "training")).Value;

        named.Sim!.JobId.ShouldBe(Guid.Parse(Job));
        named.Filters.From.ShouldBe(new DateOnly(2026, 1, 1));
        current.Sim!.JobId.ShouldBeNull();
    }

    [Theory]
    [InlineData("research", Job, null, null, "simJob needs purpose=training")]
    [InlineData(null, Job, null, null, "simJob needs purpose=training")]
    [InlineData("evidence", null, null, null, "purpose 'evidence' is research or training")]
    [InlineData("training", "not-a-guid", null, null, "simJob is a guid")]
    [InlineData("training", null, "net", null, "gross")]
    [InlineData("training", null, null, "intraday", "labels of evidence runs")]
    public void A_wrong_combination_is_reports_invalid(string? purpose, string? job, string? basis, string? style, string reason)
    {
        var parsed = ReportQuery.Parse(new ReportQueryInput(Purpose: purpose, SimJob: job, Basis: basis, Style: style));

        parsed.IsFailure.ShouldBeTrue();
        parsed.Error.Code.ShouldBe("reports.invalid");
        parsed.Error.Message.ShouldContain(reason);
    }

    [Fact]
    public void A_training_query_that_names_evidence_runs_or_a_backwards_range_is_refused()
    {
        ReportQuery.Parse(new ReportQueryInput(Purpose: "training", Runs: new string('a', 64))).Error.Message.ShouldContain("selected by simJob");
        ReportQuery.Parse(new ReportQueryInput(Purpose: "training", From: "2026-03-01", To: "2026-02-01")).Error.Message.ShouldContain("from is after to");
    }
}
