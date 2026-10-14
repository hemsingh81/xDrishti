using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Platform.Heartbeats;
using XDrishti.Domain.Platform;

namespace XDrishti.Application.Tests.Platform;

/// <summary>The feed's heartbeat (ADR 0024, P2-10 criterion 15): state and details written into the heartbeat row of <c>xd-feed</c> and read back; the row without details is a feed that is alive and says nothing.</summary>
public sealed class FeedHeartbeatsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 4, 30, 0, TimeSpan.Zero);
    private static readonly FeedHeartbeat Details = new(true, 42, Now.AddSeconds(-2), 12.5, 1, 3, 17);

    private readonly InMemoryHeartbeatStore _store = new();
    private readonly FakeTimeProvider _clock = new(Now);

    private FeedHeartbeats Feed() => new(_store, _clock);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_first_report_creates_the_row_of_the_feed_with_state_and_details()
    {
        await Feed().ReportAsync("connected", Details, "feed-1", "0.1.0", Ct);

        var row = _store.Rows.ShouldHaveSingleItem();
        (row.ServiceName, row.State, row.LastSeenAt).ShouldBe(("xd-feed", "connected", Now));
        row.Details.ShouldNotBeNull().ShouldContain("\"ticksPerSecond\":12.5");
    }

    [Fact]
    public async Task The_details_use_the_documented_camel_case_names()
    {
        await Feed().ReportAsync("connected", Details, "feed-1", "0.1.0", Ct);

        var json = _store.Rows.Single().Details!;

        foreach (var name in new[] { "connected", "subscribed", "lastTickAt", "ticksPerSecond", "reconnectsToday", "droppedPackets", "flushMs" })
        {
            json.ShouldContain($"\"{name}\":");
        }
    }

    [Fact]
    public async Task Details_written_are_read_back_and_the_next_report_updates_the_same_row()
    {
        await Feed().ReportAsync("connected", Details, "feed-1", "0.1.0", Ct);
        _clock.Advance(TimeSpan.FromSeconds(15));
        var next = Details with { Subscribed = 40, LastTickAt = null };
        await Feed().ReportAsync("idle", next, "feed-1", "0.1.0", Ct);

        var status = await Feed().GetAsync(Ct);

        _store.Rows.Count.ShouldBe(1);
        status.ShouldBe(new FeedStatus("idle", Now.AddSeconds(15), true, next));
    }

    [Fact]
    public async Task A_feed_that_has_not_reported_for_over_a_minute_is_not_alive()
    {
        await Feed().ReportAsync("connected", Details, "feed-1", "0.1.0", Ct);
        _clock.Advance(TimeSpan.FromSeconds(61));

        (await Feed().GetAsync(Ct))!.IsAlive.ShouldBeFalse();
    }

    [Fact]
    public async Task A_feed_that_never_reported_is_null()
    {
        (await Feed().GetAsync(Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task A_row_without_details_is_alive_with_state_unknown_and_no_figures()
    {
        _store.Rows.Add(ServiceHeartbeat.Start("xd-feed", "feed-1", "0.1.0", Now));

        (await Feed().GetAsync(Ct)).ShouldBe(new FeedStatus("unknown", Now, true, null));
    }

    [Fact]
    public async Task Details_that_cannot_be_read_give_no_figures_instead_of_failing()
    {
        var row = ServiceHeartbeat.Start("xd-feed", "feed-1", "0.1.0", Now);
        row.Beat("feed-1", "0.1.0", Now, "connected", "not json");
        _store.Rows.Add(row);

        (await Feed().GetAsync(Ct))!.Details.ShouldBeNull();
    }

    [Fact]
    public async Task The_workers_heartbeat_is_untouched_by_the_feeds()
    {
        _store.Rows.Add(ServiceHeartbeat.Start("xd-worker", "w1", "0.1.0", Now));

        await Feed().ReportAsync("connected", Details, "feed-1", "0.1.0", Ct);

        var worker = _store.Rows.Single(r => r.ServiceName == "xd-worker");
        (worker.State, worker.Details).ShouldBe((null, null));
    }

    private sealed class InMemoryHeartbeatStore : IServiceHeartbeatStore
    {
        public List<ServiceHeartbeat> Rows { get; } = [];

        public ValueTask<ServiceHeartbeat?> FindAsync(string serviceName, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Rows.FirstOrDefault(r => r.ServiceName == serviceName));

        public ValueTask<IReadOnlyList<ServiceHeartbeat>> ListAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<ServiceHeartbeat>>(Rows);

        public void Add(ServiceHeartbeat heartbeat) => Rows.Add(heartbeat);

        public ValueTask SaveChangesAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
