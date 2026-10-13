using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio;
using XDrishti.Application.Portfolio.Groups;
using XDrishti.Domain.Common;
using static XDrishti.Application.Tests.Portfolio.Groups.GroupFixtures;

namespace XDrishti.Application.Tests.Portfolio.Groups;

/// <summary>The groups and list handlers over made-up holdings and a fake master (clock: Monday 2026-10-12 10:00 India time).</summary>
internal sealed class GroupsHarness
{
    public GroupsHarness(params HoldingData[] holdings)
    {
        Queries = new StubHoldingQueries(holdings.Length == 0 ? AllHoldings() : holdings);
        Classifications = Fake();
        Clock.SetUtcNow(new DateTimeOffset(2026, 10, 12, 4, 30, 0, TimeSpan.Zero));
    }

    public StubHoldingQueries Queries { get; }

    public FakeHoldingClassifications Classifications { get; }

    public FakeTimeProvider Clock { get; } = new();

    public GetHoldingGroupsHandler Handler() => new(Queries, Classifications, Clock, NullLogger<GetHoldingGroupsHandler>.Instance);

    public ListHoldingsHandler Lister() => new(Queries, Classifications, Clock);

    public static HoldingGroupsRequest Req(string? by, string? sort = null, string? stackBy = null, string? status = null, string? q = null, Guid? account = null, Guid? bucket = null) =>
        new(account, bucket, status, q, by, sort, stackBy);

    public async Task<Result<HoldingGroups>> GroupsAsync(HoldingGroupsRequest request) =>
        await Handler().HandleAsync(new GetHoldingGroupsQuery(request), CancellationToken.None);

    public async Task<HoldingGroups> OkAsync(HoldingGroupsRequest request)
    {
        var result = await GroupsAsync(request);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code + ": " + result.Error.Message : string.Empty);
        return result.Value;
    }

    public async Task<Result<HoldingPage>> ListAsync(string? groupBy, string? groupKey, string? sort = "symbol", Guid? account = null, string? status = null) =>
        await Lister().HandleAsync(new ListHoldingsQuery(new HoldingsRequest(account, null, status, null, sort, null, null, groupBy, groupKey)), CancellationToken.None);
}
