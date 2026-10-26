using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Application.Abstractions.Depth;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Domain.Accounts;
using XDrishti.Infrastructure.Brokers;

namespace XDrishti.Infrastructure.Tests.Brokers;

/// <summary>
/// The depth client against a local server that speaks binary frames (P7-16 criteria 1, 2, 8, 11): the path and query of the upgrade, the subscription message, packets end to end including stacked ones, a refused token, drops, and no secret in any log.
/// The real endpoint is checked against the allow-list before the connection is made; the test server only replaces the address that is connected to.
/// </summary>
public sealed class DhanDepthConnectorTests : IDisposable
{
    private const string TokenMarker = "FAKE-TOKEN-MARKER-DO-NOT-LEAK";

    private const string ClientMarker = "FAKE-CLIENT-1000000001";

    private readonly FeedTestServer _server = new();

    private readonly CapturingLoggers _logs = new();

    private readonly DhanDepthConnector _connector;

    public DhanDepthConnectorTests() => _connector = new DhanDepthConnector(_logs, _server.Endpoint);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DataAccountCredentials Credentials()
    {
        SecretValue.TryCreate("token-" + TokenMarker, out var token, out _);
        return new DataAccountCredentials(token!, ClientMarker);
    }

    public void Dispose() => _server.Dispose();

    private async Task<(IDepthFeedConnection Client, ServerConnection Server)> OpenAsync()
    {
        var result = await _connector.ConnectAsync(Credentials(), Ct);
        result.Failure.ShouldBeNull();
        var server = await _server.NextConnectionAsync(Ct);
        return (result.Connection.ShouldNotBeNull(), server);
    }

    private static async Task<List<DepthPacket>> ReadAsync(IDepthFeedConnection client, int count)
    {
        var packets = new List<DepthPacket>();
        await foreach (var packet in client.ReadAsync(Ct))
        {
            packets.Add(packet);
            if (packets.Count == count)
            {
                break;
            }
        }

        return packets;
    }

    [Fact]
    public async Task The_upgrade_goes_to_the_depth_path_with_exactly_the_three_documented_query_keys_and_no_version()
    {
        var (client, _) = await OpenAsync();
        await using var _ = client;

        _server.Paths.ShouldHaveSingleItem().ShouldBe("/twentydepth");
        var pairs = _server.Queries.ShouldHaveSingleItem().TrimStart('?').Split('&').Select(p => p.Split('=')).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
        pairs.Keys.ShouldBe(["token", "clientId", "authType"], ignoreOrder: true);
        pairs["authType"].ShouldBe("2");
        pairs["clientId"].ShouldBe(ClientMarker);
        pairs["token"].ShouldBe("token-" + TokenMarker);
    }

    [Fact]
    public async Task One_subscribe_message_carries_request_code_23_the_count_and_the_nse_equity_instruments()
    {
        var (client, server) = await OpenAsync();
        await using var _ = client;
        server.StartReceiving(Ct);

        await client.SubscribeAsync([1333, 2885], Ct);

        using var json = JsonDocument.Parse(await server.NextMessageAsync(Ct));
        json.RootElement.GetProperty("RequestCode").GetInt32().ShouldBe(23);
        json.RootElement.GetProperty("InstrumentCount").GetInt32().ShouldBe(2);
        var list = json.RootElement.GetProperty("InstrumentList");
        list[0].GetProperty("ExchangeSegment").GetString().ShouldBe("NSE_EQ");
        list[0].GetProperty("SecurityId").GetString().ShouldBe("1333");
        list[1].GetProperty("SecurityId").GetString().ShouldBe("2885");
    }

    [Fact]
    public async Task More_than_fifty_instruments_are_refused_before_anything_is_sent()
    {
        var (client, _) = await OpenAsync();
        await using var _ = client;

        await Should.ThrowAsync<ArgumentOutOfRangeException>(async () => await client.SubscribeAsync([.. Enumerable.Range(1, 51)], Ct));
    }

    [Fact]
    public async Task Packets_arrive_parsed_end_to_end_stacked_ones_too_and_a_bad_one_is_counted_not_thrown()
    {
        var (client, server) = await OpenAsync();
        await using var _ = client;
        await server.SendAsync(DepthPackets.Frame(DepthPackets.Bid(2885, 2950.25), DepthPackets.Ask(2885, 2950.30)), Ct);
        await server.SendAsync([9, 9, 9], Ct); // not a packet
        await server.SendTextAsync("hello", Ct); // not binary
        await server.SendAsync(DepthPackets.Book(41, 2885, 1, 100, 0.05, segment: 4), Ct); // another segment
        await server.SendAsync(DepthPackets.Bid(1333), Ct);

        var packets = await ReadAsync(client, 3);

        packets.Select(p => (p.Kind, p.SecurityId)).ShouldBe([(DepthPacketKind.Bid, 2885), (DepthPacketKind.Ask, 2885), (DepthPacketKind.Bid, 1333)]);
        packets[0].Prices[0].ShouldBe(2950.25);
        client.DroppedPackets.ShouldBe(3);
    }

    [Fact]
    public async Task A_disconnect_packet_of_the_server_comes_through_as_a_packet()
    {
        var (client, server) = await OpenAsync();
        await using var _ = client;
        await server.SendAsync(DepthPackets.Disconnect(806), Ct);

        var packet = (await ReadAsync(client, 1)).ShouldHaveSingleItem();

        packet.Kind.ShouldBe(DepthPacketKind.Disconnect);
        packet.Reason.ShouldBe(806);
    }

    [Fact]
    public async Task A_server_that_closes_ends_the_stream_with_a_fixed_reason()
    {
        var (client, server) = await OpenAsync();
        await using var _ = client;
        await server.CloseAsync(Ct);

        (await ReadAsync(client, 5)).ShouldBeEmpty();

        client.EndReason.ShouldBe("closed by the server");
    }

    [Fact]
    public async Task A_cancelled_read_ends_as_stopped()
    {
        var (client, _) = await OpenAsync();
        await using var _ = client;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var reading = Task.Run(async () =>
        {
            await foreach (var packet in client.ReadAsync(cancel.Token))
            {
                packet.ShouldNotBeNull();
            }
        }, Ct);

        await cancel.CancelAsync();
        await reading;

        client.EndReason.ShouldBe("stopped");
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task A_refused_upgrade_is_a_refused_token_not_an_exception(int status)
    {
        _server.RejectWith = status;

        var result = await _connector.ConnectAsync(Credentials(), Ct);

        result.Connection.ShouldBeNull();
        result.Failure.ShouldBe(FeedConnectFailureKind.TokenRefused);
        result.Message.ShouldBe(DhanDepthConnector.TokenRefused);
    }

    [Fact]
    public async Task Another_failure_is_a_plain_failure()
    {
        _server.RejectWith = 500;

        var result = await _connector.ConnectAsync(Credentials(), Ct);

        result.Failure.ShouldBe(FeedConnectFailureKind.Failed);
        result.Message.ShouldBe(DhanDepthConnector.Unreachable);
    }

    [Fact]
    public async Task Nothing_secret_is_in_any_log_message_or_exception_text_whatever_happens()
    {
        _server.RejectWith = 401;
        await _connector.ConnectAsync(Credentials(), Ct);
        _server.RejectWith = 500;
        await _connector.ConnectAsync(Credentials(), Ct);
        _server.RejectWith = null;
        var (client, server) = await OpenAsync();
        await client.SubscribeAsync([2885], Ct);
        await server.SendAsync(DepthPackets.Bid(2885), Ct);
        await server.SendAsync([1, 2, 3], Ct);
        await server.CloseAsync(Ct);
        await ReadAsync(client, 10);
        await client.DisposeAsync();

        var everything = string.Join('\n', _logs.Messages);
        everything.ShouldNotContain(TokenMarker, Case.Insensitive);
        everything.ShouldNotContain(ClientMarker, Case.Insensitive);
        everything.ShouldNotContain("depth-api-feed", Case.Insensitive);
        everything.ShouldNotContain("127.0.0.1", Case.Insensitive);
        everything.ShouldNotContain("token=", Case.Insensitive);
        everything.ShouldNotContain("clientId", Case.Insensitive);
        everything.ShouldNotContain("2885");
    }

    [Fact]
    public void The_messages_of_a_failed_connect_carry_no_secret()
    {
        foreach (var text in new[] { DhanDepthConnector.TokenRefused, DhanDepthConnector.Unreachable, DhanDepthConnector.TimedOut })
        {
            text.ShouldNotContain("token=", Case.Insensitive);
            text.ShouldNotContain("wss://", Case.Insensitive);
            text.ShouldNotContain("depth-api", Case.Insensitive);
        }
    }

    private sealed class CapturingLoggers : ILoggerFactory
    {
        private readonly object _gate = new();
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
            // nothing: the factory collects by itself
        }

        public ILogger CreateLogger(string categoryName) => new Capture(this);

        public void Dispose()
        {
            // nothing to release
        }

        private sealed class Capture(CapturingLoggers owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => NullLogger.Instance.BeginScope(state);

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (owner._gate)
                {
                    owner._messages.Add(formatter(state, exception) + " " + exception);
                }
            }
        }
    }
}
