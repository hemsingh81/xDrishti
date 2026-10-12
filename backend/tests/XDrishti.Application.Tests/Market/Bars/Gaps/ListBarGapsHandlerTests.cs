using XDrishti.Application.Market.Bars.Gaps;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Tests.Market.Bars.Gaps;

public sealed class ListBarGapsHandlerTests
{
    [Fact]
    public void Without_filters_the_first_page_of_fifty_is_asked_for()
    {
        var search = ListBarGapsHandler.Check(new GapRequest(null, null, null, null));

        search.Value.ShouldBe(new GapSearch(null, null, 1, 50));
    }

    [Theory]
    [InlineData("open", GapStatus.Open)]
    [InlineData("FILLED", GapStatus.Filled)]
    [InlineData(" Unfillable ", GapStatus.Unfillable)]
    public void A_status_is_read_case_insensitively(string text, GapStatus status)
    {
        var instrument = Guid.NewGuid();

        var search = ListBarGapsHandler.Check(new GapRequest(text, instrument, 3, 200));

        search.Value.ShouldBe(new GapSearch(status, instrument, 3, 200));
    }

    [Fact]
    public void Bad_values_are_refused_with_one_message_per_field()
    {
        var result = ListBarGapsHandler.Check(new GapRequest("closed", null, 0, 201));

        result.Error.Code.ShouldBe("data.invalid");
        result.Error.Fields!.Select(f => f.Field).ShouldBe(["status", "page", "pageSize"]);
    }

    [Fact]
    public void The_limits_of_page_and_page_size_are_accepted()
    {
        ListBarGapsHandler.Check(new GapRequest(null, null, GapSearch.MaxPage, GapSearch.MaxPageSize)).IsSuccess.ShouldBeTrue();
        ListBarGapsHandler.Check(new GapRequest(null, null, GapSearch.MaxPage + 1, 1)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task A_bad_request_never_reaches_the_store_and_a_good_one_returns_its_page()
    {
        var gaps = new FakeBarGaps();
        var handler = new ListBarGapsHandler(gaps);

        var bad = await handler.HandleAsync(new ListBarGapsQuery(new GapRequest("x", null, null, null)), TestContext.Current.CancellationToken);
        var good = await handler.HandleAsync(new ListBarGapsQuery(new GapRequest("open", null, 2, 10)), TestContext.Current.CancellationToken);

        bad.IsFailure.ShouldBeTrue();
        (good.Value.Page, good.Value.PageSize).ShouldBe((2, 10));
    }
}
