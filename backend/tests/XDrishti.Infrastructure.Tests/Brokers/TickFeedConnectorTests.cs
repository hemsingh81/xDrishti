using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Domain.Accounts;
using XDrishti.Infrastructure.Brokers;

namespace XDrishti.Infrastructure.Tests.Brokers;

/// <summary>
/// The trade-tick recorder's connector against a local binary-frame server (P7-16b criteria 1, 2, 12): the same allowed endpoint and query as the live feed (the allow-list check runs on the real URL first), quote subscriptions as request code 17 of
/// at most 100 instruments, a constructed quote frame (UNVERIFIED layout) parsed with its last quantity and volume, a refused token as a result, and no secret in any log.
/// </summary>
public sealed class TickFeedConnectorTests : IDisposable
{
    private const string TokenMarker = "FAKE-TICK-TOKEN-DO-NOT-LEAK";

    private const string ClientMarker = "FAKE-CLIENT-1000000002";

    private static readonly DateTimeOffset Now = FeedPackets.FixtureTime.AddSeconds(5);

    private readonly FeedTestServer _server = new();

    private readonly Capturing _logs = new();

    private readonly TickFeedConnector _connector;

    public TickFeedConnectorTests() => _connector = new TickFeedConnector(new FakeTimeProvider(Now), _logs, _server.Endpoint);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _server.Dispose();
        _logs.Dispose();
    }

    private static DataAccountCredentials Credentials()
    {
        SecretValue.TryCreate("token-" + TokenMarker, out var token, out _);
        return new DataAccountCredentials(token!, ClientMarker);
    }

    private async Task<(ILiveFeedConnection Client, ServerConnection Server)> OpenAsync()
    {
        var result = await _connector.ConnectAsync(Credentials(), Ct);
        result.Failure.ShouldBeNull();
        return (result.Connection.ShouldNotBeNull(), await _server.NextConnectionAsync(Ct));
    }

    [Fact]
    public async Task It_opens_the_live_feeds_allowed_endpoint_with_its_four_query_keys_and_no_path()
    {
        var (client, _) = await OpenAsync();
        await using var _ = client;

        var pairs = _server.Queries.ShouldHaveSingleItem().TrimStart('?').Split('&').Select(p => p.Split('=')[0]);
        pairs.ShouldBe(["version", "token", "clientId", "authType"], ignoreOrder: true);
        _server.Paths.ShouldHaveSingleItem().ShouldBe("/");
        BrokerEndpoints.IsAllowedWebSocket(BrokerEndpoints.BuildFeedUri(ClientMarker, "t")).ShouldBeTrue();
    }

    [Fact]
    public async Task A_quote_subscription_is_request_code_17_and_more_than_one_hundred_are_refused_before_sending()
    {
        var (client, server) = await OpenAsync();
        await using var _ = client;
        var fifty = Enumerable.Range(1, 50).Select(i => new FeedSecurity(FeedSegments.NseEquity, i)).ToList(); // the day's set is at most 50; the test server reads messages up to 4 KB
        var tooMany = Enumerable.Range(1, 101).Select(i => new FeedSecurity(FeedSegments.NseEquity, i)).ToList();

        await client.SubscribeAsync(FeedMode.Quote, fifty, Ct);
        var message = JsonDocument.Parse(await server.NextMessageAsync(Ct)).RootElement;

        message.GetProperty("RequestCode").GetInt32().ShouldBe(17);
        message.GetProperty("InstrumentCount").GetInt32().ShouldBe(50);
        await Should.ThrowAsync<ArgumentOutOfRangeException>(async () => await client.SubscribeAsync(FeedMode.Quote, tooMany, Ct));
    }

    [Fact]
    public async Task A_constructed_quote_frame_arrives_with_its_last_quantity_and_cumulative_volume()
    {
        var (client, server) = await OpenAsync();
        await using var _ = client;
        var frame = FeedPackets.Quote(1, 2885, 2950.5f, FeedPackets.Epoch(FeedPackets.FixtureTime), 1_234_567);

        await server.SendAsync(frame, Ct);
        FeedPacket? packet = null;
        await foreach (var received in client.ReadAsync(Ct))
        {
            packet = received;
            break;
        }

        packet.ShouldNotBeNull().Kind.ShouldBe(FeedPacketKind.Quote);
        (packet.LastQuantity, packet.Volume, packet.Price).ShouldBe((25, 1_234_567L, 2950.5m));
    }

    [Fact]
    public async Task A_refused_token_is_a_result_and_nothing_secret_reaches_a_log()
    {
        _server.RejectWith = 401;
        var refused = await _connector.ConnectAsync(Credentials(), Ct);
        _server.RejectWith = null;
        var (client, server) = await OpenAsync();
        await client.SubscribeAsync(FeedMode.Quote, [new FeedSecurity(FeedSegments.NseEquity, 1333)], Ct);
        await server.CloseAsync(Ct);
        await client.DisposeAsync();

        refused.Failure.ShouldBe(FeedConnectFailureKind.TokenRefused);
        var everything = string.Join('\n', _logs.Messages);
        everything.ShouldNotContain(TokenMarker, Case.Insensitive);
        everything.ShouldNotContain(ClientMarker, Case.Insensitive);
        everything.ShouldNotContain("api-feed", Case.Insensitive);
        everything.ShouldNotContain("token=", Case.Insensitive);
    }

    /// <summary>Collects every message any logger writes, with the text of its exception.</summary>
    private sealed class Capturing : ILoggerFactory
    {
        private readonly Lock _gate = new();
        private readonly List<string> _messages = [];

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (_gate)
                {
                    return [.. _messages];
                }
            }
        }

        public void AddProvider(ILoggerProvider provider)
        {
            // collects by itself
        }

        public ILogger CreateLogger(string categoryName) => new Capture(this);

        public void Dispose()
        {
            // nothing to release
        }

        private void Add(string text)
        {
            lock (_gate)
            {
                _messages.Add(text);
            }
        }

        private sealed class Capture(Capturing owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => NullLogger.Instance.BeginScope(state);

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                owner.Add(formatter(state, exception) + " " + exception);
        }
    }
}
