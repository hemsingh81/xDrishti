using NSubstitute;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Research;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Tests.Research;

/// <summary>The guards of the read handlers (spec P3-10, D7 and criterion 11): a page size outside 1 to 1,000 and a key that is not 64 lower-case hex are refused before the store is asked.</summary>
public sealed class StoredRunReadHandlersTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string Key = new('b', 64);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    public async Task A_page_size_outside_1_to_1000_is_refused_without_asking_the_store(int size)
    {
        var store = Substitute.For<IRunStore>();

        var runs = await new ListStoredRunsHandler(store).HandleAsync(new ListStoredRunsQuery(new RunListFilter(), new PageRequest(size)), Ct);
        var trades = await new PageStoredTradesHandler(store).HandleAsync(new PageStoredTradesQuery(Key, new TradePageFilter(), new PageRequest(size)), Ct);

        runs.Error.Code.ShouldBe("store.page_invalid");
        trades.Error.Code.ShouldBe("store.page_invalid");
        store.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1000)]
    public async Task The_largest_and_smallest_pages_are_passed_to_the_store(int size)
    {
        var store = Substitute.For<IRunStore>();
        store.PageTradesAsync(Key, Arg.Any<TradePageFilter>(), Arg.Any<PageRequest>(), Ct).Returns(XDrishti.Domain.Common.Result.Success(new Page<RunTrade>([], null)));

        var result = await new PageStoredTradesHandler(store).HandleAsync(new PageStoredTradesQuery(Key, new TradePageFilter(), new PageRequest(size)), Ct);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_key_that_is_not_64_lower_case_hex_is_not_found_without_asking_the_store()
    {
        var store = Substitute.For<IRunStore>();

        var one = await new GetStoredRunHandler(store).HandleAsync(new GetStoredRunQuery("'; drop table research.sim_runs; --"), Ct);
        var two = await new PageStoredTradesHandler(store).HandleAsync(new PageStoredTradesQuery("ABC", new TradePageFilter(), new PageRequest(5)), Ct);

        one.Error.Code.ShouldBe("store.run_not_found");
        two.Error.Code.ShouldBe("store.run_not_found");
        store.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unknown_key_is_not_found()
    {
        var store = Substitute.For<IRunStore>();
        store.FindAsync(Key, Ct).Returns((StoredRunHeader?)null);

        var result = await new GetStoredRunHandler(store).HandleAsync(new GetStoredRunQuery(Key), Ct);

        result.Error.Code.ShouldBe("store.run_not_found");
        result.Error.Kind.ShouldBe(XDrishti.Domain.Common.ErrorKind.NotFound);
    }
}
