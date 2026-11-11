using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Domain.Accounts;
using XDrishti.Infrastructure.Brokers;

namespace XDrishti.Infrastructure.Tests.Brokers;

/// <summary>
/// The opening-minute recorder's connector against a local binary-frame server (BK-03b criteria 1, 2, 7): its own socket on the live feed's allowed endpoint (the allow-list check runs on the real URL first), a quote subscription as request code 17, a constructed
/// quote frame (UNVERIFIED layout) with the exchange time and the cumulative volume the bar builder reads, and a refused token as a result.
/// </summary>
public sealed class OpeningBarsFeedConnectorTests : IDisposable
{
    private static readonly DateTimeOffset Now = FeedPackets.FixtureTime.AddSeconds(5);

    private readonly FeedTestServer _server = new();

    private readonly OpeningBarsFeedConnector _connector;

    public OpeningBarsFeedConnectorTests() => _connector = new OpeningBarsFeedConnector(new FakeTimeProvider(Now), NullLoggerFactory.Instance, _server.Endpoint);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _server.Dispose();

    private static DataAccountCredentials Credentials()
    {
        SecretValue.TryCreate("token-FAKE-OPENING-TOKEN", out var token, out _);
        return new DataAccountCredentials(token!, "FAKE-CLIENT-1000000003");
    }

    private async Task<(ILiveFeedConnection Client, ServerConnection Server)> OpenAsync()
    {
        var result = await _connector.ConnectAsync(Credentials(), Ct);
        result.Failure.ShouldBeNull();
        return (result.Connection.ShouldNotBeNull(), await _server.NextConnectionAsync(Ct));
    }

    [Fact]
    public async Task It_opens_the_live_feeds_allowed_endpoint_and_a_quote_subscription_is_request_code_17()
    {
        var (client, server) = await OpenAsync();
        await using var _ = client;

        await client.SubscribeAsync(FeedMode.Quote, [new FeedSecurity(FeedSegments.NseEquity, 1333)], Ct);
        var message = JsonDocument.Parse(await server.NextMessageAsync(Ct)).RootElement;

        message.GetProperty("RequestCode").GetInt32().ShouldBe(17);
        _server.Paths.ShouldHaveSingleItem().ShouldBe("/");
        BrokerEndpoints.IsAllowedWebSocket(BrokerEndpoints.BuildFeedUri("FAKE-CLIENT-1000000003", "t")).ShouldBeTrue();
    }

    [Fact]
    public async Task A_constructed_quote_frame_arrives_with_the_exchange_time_and_the_cumulative_volume()
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
        (packet.Volume, packet.Price, packet.TradeTime).ShouldBe((1_234_567L, 2950.5m, FeedPackets.FixtureTime));
    }

    [Fact]
    public async Task A_refused_token_is_a_result_not_an_exception()
    {
        _server.RejectWith = 401;

        var refused = await _connector.ConnectAsync(Credentials(), Ct);

        refused.Failure.ShouldBe(FeedConnectFailureKind.TokenRefused);
    }
}
