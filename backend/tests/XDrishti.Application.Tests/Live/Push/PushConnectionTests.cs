using XDrishti.Application.Live.Push;

namespace XDrishti.Application.Tests.Live.Push;

public sealed class PushConnectionTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 12, 4, 30, 0, TimeSpan.Zero);
    private static readonly Guid A = new("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = new("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid C = new("00000000-0000-0000-0000-00000000000c");

    private static InstrumentPrice Price(Guid id, decimal price, int seconds = 0, string source = "live") => new(id, price, At.AddSeconds(seconds), source);

    private static Dictionary<Guid, InstrumentPrice> Current(params InstrumentPrice[] prices) => prices.ToDictionary(p => p.InstrumentId);

    [Fact]
    public void Only_the_followed_instruments_are_taken_from_what_is_current()
    {
        var connection = new PushConnection("c1", Guid.NewGuid());
        connection.Follow([A, B]);

        var changed = connection.TakeChanged(Current(Price(A, 10), Price(B, 20), Price(C, 30)));

        changed.Select(p => p.InstrumentId).ShouldBe([A, B]);
    }

    private static List<InstrumentPrice> Take(PushConnection connection, params InstrumentPrice[] current)
    {
        var changed = connection.TakeChanged(Current(current));
        connection.MarkSent(changed); // as if every send succeeded
        return changed;
    }

    [Fact]
    public void A_price_that_was_sent_is_not_sent_again_until_it_differs_in_price_time_or_source()
    {
        var connection = new PushConnection("c1", Guid.NewGuid());
        connection.Follow([A]);
        Take(connection, Price(A, 10)).Count.ShouldBe(1);

        Take(connection, Price(A, 10)).ShouldBeEmpty();
        Take(connection, Price(A, 11)).Count.ShouldBe(1);
        Take(connection, Price(A, 11, seconds: 5)).Count.ShouldBe(1);
        Take(connection, Price(A, 11, seconds: 5, source: "quote")).Count.ShouldBe(1);
    }

    [Fact]
    public void A_price_is_not_remembered_as_sent_until_the_send_succeeded()
    {
        var connection = new PushConnection("c1", Guid.NewGuid());
        connection.Follow([A, B]);
        var first = connection.TakeChanged(Current(Price(A, 10), Price(B, 20)));
        connection.MarkSent([first[0]]); // the message with B failed

        var again = connection.TakeChanged(Current(Price(A, 10), Price(B, 20)));

        again.Select(p => p.InstrumentId).ShouldBe([B]);
    }

    [Fact]
    public void A_price_older_than_the_one_last_sent_is_not_sent()
    {
        var connection = new PushConnection("c1", Guid.NewGuid());
        connection.Follow([A]);
        Take(connection, Price(A, 10, seconds: 10));

        Take(connection, Price(A, 9, seconds: 5)).ShouldBeEmpty();
        Take(connection, Price(A, 10, seconds: 10, source: "quote")).Count.ShouldBe(1); // same time, other source: still sent
        Take(connection, Price(A, 11, seconds: 11)).Count.ShouldBe(1);
    }

    [Fact]
    public void An_older_price_does_not_replace_a_newer_one_in_what_was_sent()
    {
        var connection = new PushConnection("c1", Guid.NewGuid());
        connection.Follow([A]);
        Take(connection, Price(A, 10, seconds: 10));

        connection.MarkSent([Price(A, 9, seconds: 5)]);

        connection.TakeChanged(Current(Price(A, 10, seconds: 10))).ShouldBeEmpty();
    }

    [Fact]
    public void A_new_subscription_keeps_what_was_sent_for_instruments_that_stay_followed_and_sends_the_new_ones()
    {
        var connection = new PushConnection("c1", Guid.NewGuid());
        connection.Follow([A]);
        Take(connection, Price(A, 10));

        connection.Follow([A, B]);

        Take(connection, Price(A, 10), Price(B, 20)).Select(p => p.InstrumentId).ShouldBe([B]);
    }

    [Fact]
    public void An_instrument_that_was_dropped_and_is_followed_again_is_sent_again()
    {
        var connection = new PushConnection("c1", Guid.NewGuid());
        connection.Follow([A, B]);
        Take(connection, Price(A, 10), Price(B, 20));

        connection.Follow([B]);
        connection.Follow([A, B]);

        Take(connection, Price(A, 10), Price(B, 20)).Select(p => p.InstrumentId).ShouldBe([A]);
    }

    [Fact]
    public void A_price_delivered_after_the_instrument_was_dropped_is_not_remembered()
    {
        var connection = new PushConnection("c1", Guid.NewGuid());
        connection.Follow([A]);
        var taken = connection.TakeChanged(Current(Price(A, 10)));
        connection.Follow([B]);

        connection.MarkSent(taken);
        connection.Follow([A]);

        connection.TakeChanged(Current(Price(A, 10))).Count.ShouldBe(1);
    }

    [Fact]
    public void An_instrument_without_a_price_is_skipped_until_it_has_one()
    {
        var connection = new PushConnection("c1", Guid.NewGuid());
        connection.Follow([A, B]);

        Take(connection, Price(A, 10)).Select(p => p.InstrumentId).ShouldBe([A]);
        Take(connection, Price(A, 10), Price(B, 20)).Select(p => p.InstrumentId).ShouldBe([B]);
    }

    [Fact]
    public void After_unfollowing_nothing_is_taken_and_nothing_is_followed()
    {
        var connection = new PushConnection("c1", Guid.NewGuid());
        connection.Follow([A]);

        connection.Unfollow();

        connection.Following.ShouldBeEmpty();
        connection.TakeChanged(Current(Price(A, 10))).ShouldBeEmpty();
    }
}
