using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Live;
using static XDrishti.Application.Tests.Live.FeedTestData;

namespace XDrishti.Application.Tests.Live;

/// <summary>Subscriptions are sent in messages of at most 100, one mode per message, spaced by a throttle of their own (P2-10 criterion 4).</summary>
public sealed class SubscriptionSyncTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ActiveSet Holdings(int count, int firstId = 1000) =>
        ActiveSetBuilder.Build(Enumerable.Range(0, count).Select(i => Holding(Guid.NewGuid(), (firstId + i).ToString(System.Globalization.CultureInfo.InvariantCulture), 1000 - i)), 5000);

    [Fact]
    public void The_batcher_splits_into_messages_of_at_most_100_one_mode_each()
    {
        var members = ActiveSetBuilder.Build([.. Holdings(250).Members.Select(m => Holding(m.InstrumentId, m.SecurityId, 1)), Index(Nifty, "13"), Alert(Reliance, "2885")], 5000).Members;

        var batches = SubscriptionBatcher.Split(members);

        batches.Where(b => b.Mode == FeedMode.Quote).Select(b => b.Securities.Count).ShouldBe([100, 100, 50]);
        batches.Where(b => b.Mode == FeedMode.Ticker).Select(b => b.Securities.Count).ShouldBe([2]);
        batches.ShouldAllBe(b => b.Securities.Count <= 100);
    }

    [Fact]
    public async Task The_first_apply_subscribes_everything_in_batches_and_a_second_apply_sends_only_the_difference()
    {
        var connection = new FakeFeedConnection();
        var settings = LiveFeedSettings.Default with { SubscribeSpacing = TimeSpan.Zero };
        var sync = new SubscriptionSync(connection, settings, new FakeTimeProvider());
        var first = Holdings(230);

        await sync.ApplyAsync(first, Ct);
        connection.Subscribed.Select(s => s.Securities.Length).ShouldBe([100, 100, 30]);

        var next = ActiveSetBuilder.Build([.. first.Members.Skip(1).Select(m => Holding(m.InstrumentId, m.SecurityId, 1)), Holding(Guid.NewGuid(), "9999", 1)], 5000);
        await sync.ApplyAsync(next, Ct);

        connection.Unsubscribed.ShouldHaveSingleItem().Securities.ShouldBe([first.Members[0].Key]);
        connection.Subscribed.Count.ShouldBe(4);
        connection.Subscribed[3].Securities.ShouldBe([new FeedSecurity(FeedSegments.NseEquity, 9999)]);
        sync.Current.ShouldBe(next);
    }

    [Fact]
    public async Task Messages_are_spaced_by_the_throttle()
    {
        var connection = new FakeFeedConnection();
        var clock = new Platform.Services.SettableClock(new DateTimeOffset(2026, 10, 12, 4, 30, 0, TimeSpan.Zero));
        var sync = new SubscriptionSync(connection, LiveFeedSettings.Default with { SubscribeSpacing = TimeSpan.FromMilliseconds(250) }, clock);

        var applying = sync.ApplyAsync(Holdings(250), Ct).AsTask();
        await clock.WhenWaitingForAsync(TimeSpan.FromMilliseconds(250));
        connection.Subscribed.Count.ShouldBe(1);
        clock.Advance(TimeSpan.FromMilliseconds(250));
        await FeedWorld.UntilAsync(() => connection.Subscribed.Count >= 2 && clock.TimersCreated >= 2); // the second wait exists: a clock moved before it is made would never wake it

        clock.Advance(TimeSpan.FromMilliseconds(250));
        await applying;

        connection.Subscribed.Count.ShouldBe(3);
    }
}
