using Microsoft.Extensions.Options;
using XDrishti.Api.Exports;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Capabilities;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Common;

namespace XDrishti.Api.IntegrationTests.Exports;

/// <summary>A trade list read page by page is never given short: when the total moves between pages, or the pages end before the total, the export is refused (409 exports.changed) and no file is made.</summary>
public sealed class ExportChangedTests
{
    private sealed class OpenGate : ICapabilityGate
    {
        public ValueTask<Result> RequireAsync(string name, CancellationToken cancellationToken) => ValueTask.FromResult(Result.Success());

        public ValueTask<bool> IsEnabledAsync(string name, CancellationToken cancellationToken) => ValueTask.FromResult(true);
    }

    private sealed class Pages(params TradesView[] pages) : IQueryHandler<PageReportTradesQuery, Result<TradesView>>
    {
        private int _next;

        public ValueTask<Result<TradesView>> HandleAsync(PageReportTradesQuery query, CancellationToken cancellationToken) => ValueTask.FromResult(Result.Success(pages[_next++]));
    }

    private static readonly ReportEvidenceView Evidence = new("gross", 3, null, null, false, [], [], true, [], [], 0, 0, null);

    private static TradeLineView Trade(int ordinal) =>
        new("r", "k" + ordinal, "ORB", "15m", "BUY", "NSE:X", [], new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2), 1.0, 1, null, null, false, ordinal);

    private static ExportHandler Handler(Pages pages) =>
        new(new OpenGate(), null!, null!, null!, null!, pages, new AdjustableTimeProvider(), Options.Create(new ExportOptions()));

    private static ExportCommand Command() => new(ExportKinds.Trades, "csv", new ReportQueryInput());

    [Fact]
    public async Task The_pages_that_agree_with_the_total_give_the_file()
    {
        var result = await Handler(new Pages(new TradesView([Trade(0), Trade(1)], 3, "o2.aaaaaaaa", Evidence), new TradesView([Trade(2)], 3, null, Evidence))).HandleAsync(Command(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Rows.ShouldBe(3);
    }

    [Fact]
    public async Task A_total_that_moves_between_pages_is_a_conflict_and_no_file()
    {
        var result = await Handler(new Pages(new TradesView([Trade(0), Trade(1)], 3, "o2.aaaaaaaa", Evidence), new TradesView([Trade(2)], 2, null, Evidence))).HandleAsync(Command(), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("exports.changed");
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
    }

    [Fact]
    public async Task Pages_that_end_before_the_total_are_a_conflict_and_no_file()
    {
        var result = await Handler(new Pages(new TradesView([Trade(0), Trade(1)], 3, null, Evidence))).HandleAsync(Command(), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("exports.changed");
    }
}
