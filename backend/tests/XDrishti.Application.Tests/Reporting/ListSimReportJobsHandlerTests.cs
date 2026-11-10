#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using NSubstitute;
using XDrishti.Application.Abstractions.SimAll;
using XDrishti.Application.Reporting;

namespace XDrishti.Application.Tests.Reporting;

/// <summary>AU-14: the page of jobs a screen picks from is bounded, its cursor is checked, and it carries the training label.</summary>
public sealed class ListSimReportJobsHandlerTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    private readonly ISimAllStore _store = Substitute.For<ISimAllStore>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SimAllSnapshot Snapshot(int i, string state = "completed", int failed = 0) =>
        new(new SimAllJob(new Guid(i, 0, 0, new byte[8]), state, null, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 1), new DateOnly(2026, 4, 1), 1, "owner:x", At, At, null), new SimAllCounts(10, 10 - failed, failed, 0, 0, 0, 0, 0));

    [Fact]
    public async Task The_page_asks_one_more_than_the_limit_to_know_whether_there_is_a_next_page()
    {
        _store.ListProgressAsync(0, 3, Arg.Any<CancellationToken>()).Returns(new ValueTask<IReadOnlyList<SimAllSnapshot>>([Snapshot(1), Snapshot(2), Snapshot(3)]));

        var view = (await new ListSimReportJobsHandler(_store).HandleAsync(new ListSimReportJobsQuery("2", null), Ct)).Value;

        view.Items.Count.ShouldBe(2);
        view.NextCursor.ShouldBe("o2");
        view.Label.ShouldContain("not evidence");
    }

    [Fact]
    public async Task A_job_with_a_failed_chunk_is_not_complete()
    {
        _store.ListProgressAsync(0, 21, Arg.Any<CancellationToken>()).Returns(new ValueTask<IReadOnlyList<SimAllSnapshot>>([Snapshot(1), Snapshot(2, "completed_with_failures", 1)]));

        var view = (await new ListSimReportJobsHandler(_store).HandleAsync(new ListSimReportJobsQuery(null, null), Ct)).Value;

        view.Items.Select(i => i.Complete).ShouldBe([true, false]);
        view.NextCursor.ShouldBeNull();
    }

    [Theory]
    [InlineData("0", null)]
    [InlineData("51", null)]
    [InlineData("x", null)]
    [InlineData(null, "o-1")]
    [InlineData(null, "2")]
    public async Task A_bad_limit_or_cursor_is_reports_invalid_and_nothing_is_read(string? limit, string? cursor)
    {
        var result = await new ListSimReportJobsHandler(_store).HandleAsync(new ListSimReportJobsQuery(limit, cursor), Ct);

        result.Error.Code.ShouldBe("reports.invalid");
        await _store.DidNotReceive().ListProgressAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
