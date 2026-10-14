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
/// The feed client against a local server that speaks binary frames (ADR 0024, P2-10 criteria 1, 3, 4, 17): the query of the upgrade, the subscription messages, packets end to end, a refused token, drops, and no secret in any log.
/// The real endpoint is checked against the allow-list before the connection is made; the test server only replaces the address that is connected to.
/// </summary>
public sealed class DhanFeedConnectorTests : IDisposable
{
    private const string TokenMarker = "FAKE-TOKEN-MARKER-DO-NOT-LEAK";

    private const string ClientMarker = "FAKE-CLIENT-1000000001";

    private static readonly DateTimeOffset Now = FeedPackets.FixtureTime.AddSeconds(5);

    private readonly FeedTestServer _server = new();

    private readonly CapturingLoggers _logs = new();

    private readonly FeedPacketStats _stats = new();

    private readonly DhanFeedConnector _connector;

    public DhanFeedConnectorTests() => _connector = new DhanFeedConnector(new FakeTimeProvider(Now), _logs, _server.Endpoint, _stats);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DataAccountCredentials Credentials()
    {
        SecretValue.TryCreate("token-" + TokenMarker, out var token, out _);
        return new DataAccountCredentials(token!, ClientMarker);
    }

    public void Dispose() => _server.Dispose();

    private async Task<(ILiveFeedConnection Client, ServerConnection Server)> OpenAsync()
    {
        var result = await _connector.ConnectAsync(Credentials(), Ct);
        result.Failure.ShouldBeNull();
        var server = await _server.NextConnectionAsync(Ct);
        return (result.Connection.ShouldNotBeNull(), server);
    }

    private static async Task<List<FeedPacket>> ReadAsync(ILiveFeedConnection client, int count)
    {
        var packets = new List<FeedPacket>();
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
    public async Task The_upgrade_carries_exactly_the_four_documented_query_keys()
    {
        var (client, _) = await OpenAsync();
        await using var _ = client;

        var query = _server.Queries.ShouldHaveSingleItem();
        var pairs = query.TrimStart('?').Split('&').Select(p => p.Split('=')).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
        pairs.Keys.ShouldBe(["version", "token", "clientId", "authType"], ignoreOrder: true);
        pairs["version"].ShouldBe("2");
        pairs["authType"].ShouldBe("2");
        pairs["clientId"].ShouldBe(ClientMarker);
        pairs["token"].ShouldBe("token-" + TokenMarker);
    }

    [Fact]
    public async Task Packets_arrive_parsed_end_to_end_and_a_bad_one_is_counted_not_thrown()
    {
        var (client, server) = await OpenAsync();
        await using var _ = client;
        var epoch = FeedPackets.Epoch(FeedPackets.FixtureTime);
        await server.SendAsync(FeedPackets.Ticker(1, 1333, 1650.25f, epoch), Ct);
        await server.SendAsync([9, 9, 9], Ct); // not a packet
        await server.SendTextAsync("hello", Ct); // not binary
        await server.SendAsync(FeedPackets.Ticker(1, 1333, 0f, epoch), Ct); // a price of zero
        await server.SendAsync(FeedPackets.Quote(0, 13, 24800.5f, epoch), Ct);
        await server.SendAsync(FeedPackets.PreviousClose(1, 1333, 1640f), Ct);

        var packets = await ReadAsync(client, 3);

        packets.Select(p => p.Kind).ShouldBe([FeedPacketKind.Ticker, FeedPacketKind.Quote, FeedPacketKind.PreviousClose]);
        packets[0].Price.ShouldBe(1650.25m);
        packets[1].Security.ShouldBe(new FeedSecurity(FeedSegments.Index, 13));
        client.DroppedPackets.ShouldBe(3);
    }

    [Fact]
    public async Task Every_message_is_counted_by_response_code_with_its_outcome_and_lengths_and_never_its_bytes()
    {
        var (client, server) = await OpenAsync();
        await using var _ = client;
        var epoch = FeedPackets.Epoch(FeedPackets.FixtureTime);
        var unknown = FeedPackets.Header(99, 20, 1, 5); // code 99, 20 bytes, the header says 20
        var shortLength = FeedPackets.Ticker(1, 1333, 100f, epoch);
        shortLength[1] = 4; // declares less than a header
        await server.SendAsync(unknown, Ct);
        await server.SendAsync(shortLength, Ct);
        await server.SendAsync([2, 16], Ct); // truncated
        await server.SendTextAsync("hello", Ct);
        await server.SendAsync(FeedPackets.Ticker(1, 1333, 0f, epoch), Ct);
        await server.SendAsync(FeedPackets.Ticker(1, 1333, 1650.25f, epoch), Ct);
        await server.SendAsync(FeedPackets.Quote(1, 2885, 2950.5f, epoch), Ct);

        var packets = await ReadAsync(client, 2);

        packets.Count.ShouldBe(2);
        var summary = _stats.Summary(16);
        summary["99"].Reasons.ShouldBe(new Dictionary<string, long> { ["unknownCode"] = 1 });
        (summary["99"].FirstLength, summary["99"].LastLength, summary["99"].FirstDeclaredLength).ShouldBe((20, 20, 20));
        var ticker = summary["2"];
        (ticker.Decoded, ticker.Dropped).ShouldBe((1, 3)); // the good one; the short length, the truncated message and the zero price
        ticker.Reasons.ShouldBe(new Dictionary<string, long> { ["lengthMismatch"] = 1, ["tooShort"] = 1, ["badPrice"] = 1 }, ignoreOrder: true);
        (ticker.FirstLength, ticker.FirstDeclaredLength).ShouldBe((16, 4)); // the first ticker message declared 4: a wrong assumption would show here
        summary["none"].Reasons["notBinary"].ShouldBe(1);
        summary["4"].Decoded.ShouldBe(1);
        client.DroppedPackets.ShouldBe(_stats.TotalDropped);
    }

    [Fact]
    public async Task One_frame_with_several_packets_gives_every_packet_and_each_is_counted_with_the_length_of_the_frame()
    {
        var (client, server) = await OpenAsync();
        await using var _ = client;
        var frame = FeedPackets.Fixture("feed-multi-packet.hex"); // quote 50 + quote 50 + previous close 16
        await server.SendAsync(frame, Ct);
        var epoch = FeedPackets.Epoch(FeedPackets.FixtureTime);
        await server.SendAsync([.. FeedPackets.Ticker(1, 1333, 100f, epoch), .. new byte[5]], Ct); // a good packet and a stump
        await server.SendAsync(FeedPackets.Ticker(1, 1333, 101f, epoch), Ct);

        var packets = await ReadAsync(client, 5);

        packets.Select(p => p.Kind).ShouldBe([FeedPacketKind.Quote, FeedPacketKind.Quote, FeedPacketKind.PreviousClose, FeedPacketKind.Ticker, FeedPacketKind.Ticker]);
        var summary = _stats.Summary(16);
        var quote = summary["4"];
        (quote.Decoded, quote.Dropped, quote.FirstLength, quote.FirstDeclaredLength, quote.InMultiPacketFrames).ShouldBe((2, 0, 116, 50, 2));
        (summary["6"].Decoded, summary["6"].InMultiPacketFrames).ShouldBe((1, 1));
        summary["2"].Decoded.ShouldBe(2);
        summary["2"].InMultiPacketFrames.ShouldBe(1); // it shared its frame with the stump
        summary["0"].Reasons.ShouldBe(new Dictionary<string, long> { ["tooShort"] = 1 }); // the stump of 5 zero bytes, counted under its first byte
        client.DroppedPackets.ShouldBe(1); // the stump: the 5 bytes
        _stats.TotalDropped.ShouldBe(1);
    }

    [Fact]
    public async Task Subscriptions_are_json_messages_with_the_documented_request_codes()
    {
        var (client, server) = await OpenAsync();
        await using var _ = client;
        FeedSecurity[] securities = [new(FeedSegments.NseEquity, 1333), new(FeedSegments.Index, 13)];

        await client.SubscribeAsync(FeedMode.Ticker, securities, Ct);
        await client.SubscribeAsync(FeedMode.Quote, securities[..1], Ct);
        await client.UnsubscribeAsync(FeedMode.Ticker, securities[1..], Ct);
        await client.UnsubscribeAsync(FeedMode.Quote, securities[..1], Ct);

        var first = JsonDocument.Parse(await server.NextMessageAsync(Ct)).RootElement;
        first.GetProperty("RequestCode").GetInt32().ShouldBe(15);
        first.GetProperty("InstrumentCount").GetInt32().ShouldBe(2);
        var list = first.GetProperty("InstrumentList").EnumerateArray().ToList();
        list[0].GetProperty("ExchangeSegment").GetString().ShouldBe("NSE_EQ");
        list[0].GetProperty("SecurityId").GetString().ShouldBe("1333");
        list[1].GetProperty("ExchangeSegment").GetString().ShouldBe("IDX_I");
        list[1].GetProperty("SecurityId").GetString().ShouldBe("13");
        var codes = new List<int> { 15 };
        for (var i = 0; i < 3; i++)
        {
            codes.Add(JsonDocument.Parse(await server.NextMessageAsync(Ct)).RootElement.GetProperty("RequestCode").GetInt32());
        }

        codes.ShouldBe([15, 17, 16, 18]);
    }

    [Fact]
    public async Task More_than_one_hundred_securities_in_a_message_are_refused_before_they_are_sent()
    {
        var (client, _) = await OpenAsync();
        await using var _ = client;
        var tooMany = Enumerable.Range(1, 101).Select(i => new FeedSecurity(FeedSegments.NseEquity, i)).ToList();

        await Should.ThrowAsync<ArgumentOutOfRangeException>(async () => await client.SubscribeAsync(FeedMode.Quote, tooMany, Ct));
    }

    [Fact]
    public async Task A_refused_token_on_the_upgrade_is_a_result_not_an_exception()
    {
        _server.RejectWith = 401;

        var result = await _connector.ConnectAsync(Credentials(), Ct);

        result.Connection.ShouldBeNull();
        result.Failure.ShouldBe(FeedConnectFailureKind.TokenRefused);
        result.Message.ShouldBe("The Data account's token was refused by Dhan.");
    }

    [Fact]
    public async Task A_403_on_the_upgrade_is_a_refused_token_too()
    {
        _server.RejectWith = 403;

        (await _connector.ConnectAsync(Credentials(), Ct)).Failure.ShouldBe(FeedConnectFailureKind.TokenRefused);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(503)]
    [InlineData(404)]
    public async Task Any_other_failure_of_the_upgrade_is_a_failure_to_try_again(int status)
    {
        _server.RejectWith = status;

        var result = await _connector.ConnectAsync(Credentials(), Ct);

        result.Failure.ShouldBe(FeedConnectFailureKind.Failed);
        result.Message.ShouldBe("Dhan's feed could not be reached.");
    }

    [Fact]
    public async Task A_server_that_cannot_be_reached_is_a_failure_not_an_exception()
    {
        var free = new FeedTestServer();
        var endpoint = free.Endpoint;
        free.Dispose();
        var connector = new DhanFeedConnector(new FakeTimeProvider(Now), _logs, endpoint);

        var result = await connector.ConnectAsync(Credentials(), Ct);

        result.Failure.ShouldBe(FeedConnectFailureKind.Failed);
    }

    [Fact]
    public async Task When_the_server_closes_the_stream_of_packets_ends_with_the_reason()
    {
        var (client, server) = await OpenAsync();
        await using var _ = client;
        await server.SendAsync(FeedPackets.Ticker(1, 1333, 100f, FeedPackets.Epoch(FeedPackets.FixtureTime)), Ct);
        await server.CloseAsync(Ct);

        var packets = await ReadAsync(client, 10);

        packets.ShouldHaveSingleItem();
        client.EndReason.ShouldBe("closed by the server");
    }

    [Fact]
    public async Task A_cut_connection_ends_the_stream_instead_of_throwing()
    {
        var (client, server) = await OpenAsync();
        await using var _ = client;
        server.Abort();

        var packets = await ReadAsync(client, 10);

        packets.ShouldBeEmpty();
        client.EndReason.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Cancelling_the_read_ends_the_stream_quietly()
    {
        var (client, _) = await OpenAsync();
        await using var _ = client;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var reading = Task.Run(async () =>
        {
            var count = 0;
            await foreach (var packet in client.ReadAsync(cancel.Token))
            {
                count++;
                packet.ShouldNotBeNull();
            }

            return count;
        }, Ct);

        await cancel.CancelAsync();

        (await reading).ShouldBe(0);
        client.EndReason.ShouldBe("stopped");
    }

    [Fact]
    public async Task A_disconnect_packet_of_the_server_comes_through_as_a_packet()
    {
        var (client, server) = await OpenAsync();
        await using var _ = client;
        await server.SendAsync(FeedPackets.Disconnect(807), Ct);

        var packet = (await ReadAsync(client, 1)).ShouldHaveSingleItem();

        packet.Kind.ShouldBe(FeedPacketKind.Disconnect);
        packet.Reason.ShouldBe(807);
    }

    [Fact]
    public async Task Nothing_secret_is_in_any_log_message_or_exception_text_whatever_happens()
    {
        // a refused token, a working connection with subscriptions and packets, a bad packet, a close
        _server.RejectWith = 401;
        await _connector.ConnectAsync(Credentials(), Ct);
        _server.RejectWith = 500;
        await _connector.ConnectAsync(Credentials(), Ct);
        _server.RejectWith = null;
        var (client, server) = await OpenAsync();
        await client.SubscribeAsync(FeedMode.Quote, [new FeedSecurity(FeedSegments.NseEquity, 1333)], Ct);
        await server.SendAsync(FeedPackets.Ticker(1, 1333, 100f, FeedPackets.Epoch(FeedPackets.FixtureTime)), Ct);
        await server.SendAsync([1, 2, 3], Ct);
        await server.CloseAsync(Ct);
        await ReadAsync(client, 10);
        await client.DisposeAsync();

        var everything = string.Join('\n', _logs.Messages);
        everything.ShouldNotContain(TokenMarker, Case.Insensitive);
        everything.ShouldNotContain(ClientMarker, Case.Insensitive);
        everything.ShouldNotContain("api-feed", Case.Insensitive);
        everything.ShouldNotContain("127.0.0.1", Case.Insensitive);
        everything.ShouldNotContain("token=", Case.Insensitive);
        everything.ShouldNotContain("clientId", Case.Insensitive);
    }

    [Fact]
    public void The_messages_of_a_failed_connect_carry_no_secret()
    {
        var texts = new[] { DhanFeedConnector.TokenRefused, DhanFeedConnector.Unreachable, DhanFeedConnector.TimedOut };

        texts.ShouldAllBe(t => !t.Contains("token=", StringComparison.OrdinalIgnoreCase) && !t.Contains("wss", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Collects every message that any logger of the factory writes, with the text of its exception.</summary>
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
