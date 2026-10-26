using XDrishti.Infrastructure.Brokers;

namespace XDrishti.Infrastructure.Tests.Brokers;

/// <summary>Only Dhan's consent-flow endpoints can be called (specs P1-16c, P1-16d).</summary>
public sealed class BrokerEndpointsTests
{
    [Theory]
    [InlineData("POST", "https://auth.dhan.co/app/generate-consent?client_id=1000000001")]
    [InlineData("POST", "https://auth.dhan.co/app/consumeApp-consent?tokenId=abc")]
    [InlineData("POST", "https://AUTH.DHAN.CO/app/consumeApp-consent?tokenId=abc")]
    [InlineData("GET", "https://api.dhan.co/v2/holdings")] // the one data endpoint (ADR 0012)
    [InlineData("GET", "https://API.DHAN.CO/v2/holdings")]
    [InlineData("POST", "https://api.dhan.co/v2/charts/intraday")] // the 1-minute chart data of the Data API (ADR 0016 §7)
    [InlineData("POST", "https://API.DHAN.CO/v2/charts/intraday")]
    [InlineData("POST", "https://api.dhan.co/v2/marketfeed/quote")] // the market quote of the holdings (ADR 0019)
    [InlineData("POST", "https://API.DHAN.CO/v2/marketfeed/quote")]
    [InlineData("GET", "https://api.dhan.co/v2/trades/2026-09-01/2026-09-30/0")] // the trade history, a page at a time (ADR 0025)
    [InlineData("GET", "https://API.DHAN.CO/v2/trades/2026-09-01/2026-09-01/12")]
    [InlineData("GET", "https://api.dhan.co/v2/trades/2024-02-29/2026-09-30/10000")]
    [InlineData("GET", "https://api.dhan.co/v2/ledger?from-date=2026-09-01&to-date=2026-09-30")] // the ledger of a period
    [InlineData("GET", "https://api.dhan.co/v2/ledger?to-date=2026-09-30&from-date=2026-09-01")]
    public void The_documented_endpoints_are_allowed(string method, string url) =>
        BrokerEndpoints.IsAllowed(new HttpMethod(method), new Uri(url)).ShouldBeTrue();

    [Theory]
    [InlineData("GET", "https://api.dhan.co/v2/orders")] // orders
    [InlineData("POST", "https://api.dhan.co/v2/orders")]
    [InlineData("POST", "https://api.dhan.co/v2/super/orders")]
    [InlineData("POST", "https://api.dhan.co/v2/marketfeed/ltp")]
    [InlineData("GET", "https://api.dhan.co/v2/trades")]
    [InlineData("GET", "https://api.dhan.co/v2/trades/")] // the history needs two dates and a page
    [InlineData("GET", "https://api.dhan.co/v2/trades/2026-09-01")]
    [InlineData("GET", "https://api.dhan.co/v2/trades/2026-09-01/2026-09-30")]
    [InlineData("GET", "https://api.dhan.co/v2/trades/2026-09-01/2026-09-30/")]
    [InlineData("GET", "https://api.dhan.co/v2/trades/2026-09-01/2026-09-30/0/1")]
    [InlineData("GET", "https://api.dhan.co/v2/trades/2026-09-01/2026-09-30/-1")] // a page is digits
    [InlineData("GET", "https://api.dhan.co/v2/trades/2026-09-01/2026-09-30/x")]
    [InlineData("GET", "https://api.dhan.co/v2/trades/2026-09-01/2026-09-30/10001")] // more pages than any window has
    [InlineData("GET", "https://api.dhan.co/v2/trades/2026-09-01/2026-09-30/000000")]
    [InlineData("GET", "https://api.dhan.co/v2/trades/2026-9-1/2026-09-30/0")] // dates are yyyy-MM-dd
    [InlineData("GET", "https://api.dhan.co/v2/trades/2026-02-30/2026-09-30/0")] // and real dates
    [InlineData("GET", "https://api.dhan.co/v2/trades/2026-09-01/latest/0")]
    [InlineData("GET", "https://api.dhan.co/v2/trades/2026-09-01/2026-09-30/0?x=1")] // no query
    [InlineData("GET", "https://api.dhan.co/v2/trades/2026-09-01/2026-09-30/0?")]
    [InlineData("GET", "https://api.dhan.co/v2/trades/2026-09-01/2026-09-30/0#x")]
    [InlineData("GET", "https://api.dhan.co/v2/Trades/2026-09-01/2026-09-30/0")]
    [InlineData("GET", "https://api.dhan.co/v2/trades/../orders")]
    [InlineData("GET", "https://api.dhan.co/v2/trades/2026-09-01/2026-09-30/0/../../../orders")]
    [InlineData("POST", "https://api.dhan.co/v2/trades/2026-09-01/2026-09-30/0")] // read only
    [InlineData("DELETE", "https://api.dhan.co/v2/trades/2026-09-01/2026-09-30/0")]
    [InlineData("GET", "http://api.dhan.co/v2/trades/2026-09-01/2026-09-30/0")] // not HTTPS
    [InlineData("GET", "https://api.dhan.co:8443/v2/trades/2026-09-01/2026-09-30/0")]
    [InlineData("GET", "https://user@api.dhan.co/v2/trades/2026-09-01/2026-09-30/0")]
    [InlineData("GET", "https://auth.dhan.co/v2/trades/2026-09-01/2026-09-30/0")] // the path belongs to the API host only
    [InlineData("GET", "https://api.dhan.co.example.invalid/v2/trades/2026-09-01/2026-09-30/0")]
    [InlineData("GET", "https://api.dhan.co/v2/ledger")] // the ledger needs exactly its two dates
    [InlineData("GET", "https://api.dhan.co/v2/ledger?")]
    [InlineData("GET", "https://api.dhan.co/v2/ledger?from-date=2026-09-01")]
    [InlineData("GET", "https://api.dhan.co/v2/ledger?to-date=2026-09-30")]
    [InlineData("GET", "https://api.dhan.co/v2/ledger?from-date=2026-09-01&from-date=2026-09-02")] // each key once
    [InlineData("GET", "https://api.dhan.co/v2/ledger?from-date=2026-09-01&to-date=2026-09-30&page=1")] // nothing else
    [InlineData("GET", "https://api.dhan.co/v2/ledger?from-date=2026-09-01&to-date=2026-09-30&")]
    [InlineData("GET", "https://api.dhan.co/v2/ledger?fromDate=2026-09-01&toDate=2026-09-30")] // keys are spelled exactly
    [InlineData("GET", "https://api.dhan.co/v2/ledger?From-Date=2026-09-01&to-date=2026-09-30")]
    [InlineData("GET", "https://api.dhan.co/v2/ledger?from-date=2026-09-01&to-date=soon")] // valid dates
    [InlineData("GET", "https://api.dhan.co/v2/ledger?from-date=&to-date=2026-09-30")]
    [InlineData("GET", "https://api.dhan.co/v2/ledger?from-date=2026-02-30&to-date=2026-09-30")]
    [InlineData("GET", "https://api.dhan.co/v2/ledger/?from-date=2026-09-01&to-date=2026-09-30")] // paths are exact
    [InlineData("GET", "https://api.dhan.co/v2/Ledger?from-date=2026-09-01&to-date=2026-09-30")]
    [InlineData("GET", "https://api.dhan.co/v2/ledger/1?from-date=2026-09-01&to-date=2026-09-30")]
    [InlineData("GET", "https://api.dhan.co/v2/ledger?from-date=2026-09-01&to-date=2026-09-30#x")]
    [InlineData("POST", "https://api.dhan.co/v2/ledger?from-date=2026-09-01&to-date=2026-09-30")] // read only
    [InlineData("GET", "http://api.dhan.co/v2/ledger?from-date=2026-09-01&to-date=2026-09-30")]
    [InlineData("GET", "https://api.dhan.co:8443/v2/ledger?from-date=2026-09-01&to-date=2026-09-30")]
    [InlineData("GET", "https://auth.dhan.co/v2/ledger?from-date=2026-09-01&to-date=2026-09-30")]
    [InlineData("GET", "https://api.dhan.co/v2/positions")] // positions
    [InlineData("GET", "https://api.dhan.co/v2/holdings?x=1")] // holdings take no query
    [InlineData("GET", "https://api.dhan.co/v2/holdings?")]
    [InlineData("GET", "https://api.dhan.co/v2/holdings/")] // paths are exact
    [InlineData("GET", "https://api.dhan.co/v2/Holdings")]
    [InlineData("GET", "https://api.dhan.co/v2/holdings/1")]
    [InlineData("POST", "https://api.dhan.co/v2/holdings")] // read only
    [InlineData("DELETE", "https://api.dhan.co/v2/holdings")]
    [InlineData("GET", "http://api.dhan.co/v2/holdings")] // not HTTPS
    [InlineData("GET", "https://api.dhan.co:8443/v2/holdings")] // other port
    [InlineData("GET", "https://user@api.dhan.co/v2/holdings")]
    [InlineData("GET", "https://api.dhan.co.example.invalid/v2/holdings")] // other host
    [InlineData("GET", "https://auth.dhan.co/v2/holdings")] // the path belongs to the API host only
    [InlineData("GET", "https://api.dhan.co/v2/holdings#x")]
    [InlineData("GET", "https://api.dhan.co/v2/charts/intraday")] // the chart request is a POST with a body
    [InlineData("PUT", "https://api.dhan.co/v2/charts/intraday")]
    [InlineData("DELETE", "https://api.dhan.co/v2/charts/intraday")]
    [InlineData("POST", "https://api.dhan.co/v2/charts/intraday?x=1")] // no query
    [InlineData("POST", "https://api.dhan.co/v2/charts/intraday?")]
    [InlineData("POST", "https://api.dhan.co/v2/charts/intraday/")] // paths are exact
    [InlineData("POST", "https://api.dhan.co/v2/charts/Intraday")]
    [InlineData("POST", "https://api.dhan.co/v2/charts/intraday/1")]
    [InlineData("POST", "https://api.dhan.co/v2/charts/historical")] // other chart paths
    [InlineData("POST", "https://api.dhan.co/v2/charts/rollingoption")]
    [InlineData("POST", "https://api.dhan.co/v2/charts")]
    [InlineData("POST", "http://api.dhan.co/v2/charts/intraday")] // not HTTPS
    [InlineData("POST", "https://api.dhan.co:8443/v2/charts/intraday")] // other port
    [InlineData("POST", "https://user@api.dhan.co/v2/charts/intraday")]
    [InlineData("POST", "https://auth.dhan.co/v2/charts/intraday")] // the path belongs to the API host only
    [InlineData("POST", "https://api.dhan.co.example.invalid/v2/charts/intraday")] // other host
    [InlineData("POST", "https://api.dhan.co/v2/charts/intraday#x")]
    [InlineData("POST", "https://api.dhan.co/v2/marketfeed/ohlc")] // the quote is the only market-feed path
    [InlineData("POST", "https://api.dhan.co/v2/marketfeed/depth")]
    [InlineData("POST", "https://api.dhan.co/v2/marketfeed")]
    [InlineData("POST", "https://api.dhan.co/v2/marketfeed/")]
    [InlineData("POST", "https://api.dhan.co/v2/marketfeed/quote/")] // paths are exact
    [InlineData("POST", "https://api.dhan.co/v2/marketfeed/Quote")]
    [InlineData("POST", "https://api.dhan.co/v2/marketfeed/quote/1")]
    [InlineData("POST", "https://api.dhan.co/v2/marketfeed/quote?x=1")] // no query
    [InlineData("POST", "https://api.dhan.co/v2/marketfeed/quote?")]
    [InlineData("POST", "https://api.dhan.co/v2/marketfeed/quote#x")]
    [InlineData("GET", "https://api.dhan.co/v2/marketfeed/quote")] // the quote is a POST with a body
    [InlineData("PUT", "https://api.dhan.co/v2/marketfeed/quote")]
    [InlineData("DELETE", "https://api.dhan.co/v2/marketfeed/quote")]
    [InlineData("POST", "http://api.dhan.co/v2/marketfeed/quote")] // not HTTPS
    [InlineData("POST", "https://api.dhan.co:8443/v2/marketfeed/quote")] // other port
    [InlineData("POST", "https://user@api.dhan.co/v2/marketfeed/quote")]
    [InlineData("POST", "https://auth.dhan.co/v2/marketfeed/quote")] // the path belongs to the API host only
    [InlineData("POST", "https://api.dhan.co.example.invalid/v2/marketfeed/quote")] // other host
    [InlineData("POST", "https://api.upstox.com/v2/marketfeed/quote")]
    [InlineData("GET", "https://api.dhan.co/v2/fundlimit")] // funds
    [InlineData("GET", "https://api.dhan.co/v2/profile")] // no longer used
    [InlineData("GET", "https://api.dhan.co/v2/RenewToken")] // no longer used
    [InlineData("POST", "https://api.upstox.com/v2/login/authorization/token")] // Upstox is gone
    [InlineData("GET", "https://auth.dhan.co/login/consentApp-login?consentAppId=x")] // browser page, not for the server
    [InlineData("GET", "https://auth.dhan.co/app/consumeApp-consent?tokenId=abc")] // wrong method
    [InlineData("POST", "http://auth.dhan.co/app/consumeApp-consent?tokenId=abc")] // not HTTPS
    [InlineData("POST", "https://auth.dhan.co:8443/app/consumeApp-consent?tokenId=abc")] // other port
    [InlineData("POST", "https://auth.dhan.co.example.invalid/app/consumeApp-consent?tokenId=abc")] // other host
    [InlineData("POST", "https://user@auth.dhan.co/app/consumeApp-consent?tokenId=abc")]
    [InlineData("POST", "https://auth.dhan.co/app/consumeApp-consent?x=1")] // unexpected query
    [InlineData("POST", "https://auth.dhan.co/app/ConsumeApp-consent?tokenId=abc")] // paths are exact
    [InlineData("POST", "https://auth.dhan.co/app/generate-consent?client_id=1&tokenId=2")]
    [InlineData("POST", "https://auth.dhan.co/app/generate-consent?other=1")]
    public void Everything_else_is_refused(string method, string url) =>
        BrokerEndpoints.IsAllowed(new HttpMethod(method), new Uri(url)).ShouldBeFalse();

    [Fact]
    public async Task The_guard_refuses_before_anything_is_sent_and_names_no_query()
    {
        using var broker = new StubBroker();
        using var client = broker.CreateClient(BrokerHttpClientNames.Dhan);

        var error = await Should.ThrowAsync<BrokerEndpointNotAllowedException>(
            () => client.GetAsync(new Uri("https://api.dhan.co/v2/fundlimit?secret=FAKE-SECRET-MARKER"), TestContext.Current.CancellationToken));

        broker.Requests.ShouldBeEmpty();
        error.Message.ShouldContain("api.dhan.co/v2/fundlimit");
        error.Message.ShouldNotContain("FAKE-SECRET-MARKER");
    }

    // ---- the market-data WebSocket (ADR 0024 §8) ----

    [Theory]
    [InlineData("wss://api-feed.dhan.co?version=2&token=abc&clientId=1000000001&authType=2")]
    [InlineData("wss://api-feed.dhan.co/?version=2&token=abc&clientId=1000000001&authType=2")]
    [InlineData("wss://API-FEED.DHAN.CO?version=2&token=abc&clientId=1000000001&authType=2")]
    [InlineData("wss://api-feed.dhan.co?authType=2&clientId=1000000001&token=abc&version=2")] // any order
    public void The_feed_url_with_exactly_its_four_query_keys_is_allowed(string url) =>
        BrokerEndpoints.IsAllowedWebSocket(new Uri(url)).ShouldBeTrue();

    [Theory]
    [InlineData("ws://api-feed.dhan.co?version=2&token=abc&clientId=1&authType=2")] // not TLS
    [InlineData("https://api-feed.dhan.co?version=2&token=abc&clientId=1&authType=2")] // not a socket scheme
    [InlineData("wss://api-feed.dhan.co:8443?version=2&token=abc&clientId=1&authType=2")] // other port
    [InlineData("wss://user@api-feed.dhan.co?version=2&token=abc&clientId=1&authType=2")]
    [InlineData("wss://api-feed.dhan.co.example.invalid?version=2&token=abc&clientId=1&authType=2")] // other host
    [InlineData("wss://api.dhan.co?version=2&token=abc&clientId=1&authType=2")]
    [InlineData("wss://depth-api-feed.dhan.co?version=2&token=abc&clientId=1&authType=2")] // the depth feed is not carried
    [InlineData("wss://api-feed.dhan.co/feed?version=2&token=abc&clientId=1&authType=2")] // other path
    [InlineData("wss://api-feed.dhan.co//?version=2&token=abc&clientId=1&authType=2")]
    [InlineData("wss://api-feed.dhan.co")] // no query at all
    [InlineData("wss://api-feed.dhan.co?")]
    [InlineData("wss://api-feed.dhan.co?version=2&token=abc&clientId=1")] // a key missing
    [InlineData("wss://api-feed.dhan.co?version=2&token=abc&clientId=1&authType=2&extra=1")] // another key
    [InlineData("wss://api-feed.dhan.co?version=2&token=abc&clientId=1&authType=2&token=def")] // a key twice
    [InlineData("wss://api-feed.dhan.co?version=2&token=abc&clientId=1&authType=2&")]
    [InlineData("wss://api-feed.dhan.co?version=2&token=abc&clientid=1&authType=2")] // keys are spelled exactly
    [InlineData("wss://api-feed.dhan.co?version=2&Token=abc&clientId=1&authType=2")]
    [InlineData("wss://api-feed.dhan.co?version=2&token=&clientId=1&authType=2")] // a key needs a value
    [InlineData("wss://api-feed.dhan.co?version=2&token&clientId=1&authType=2")]
    [InlineData("wss://api-feed.dhan.co?version=2&token=abc&clientId=1&authType=2#x")] // no fragment
    public void Every_other_socket_is_refused(string url) =>
        BrokerEndpoints.IsAllowedWebSocket(new Uri(url)).ShouldBeFalse();

    // ---- the 20-level depth WebSocket (ADR 0133, P7-16) ----

    [Theory]
    [InlineData("wss://depth-api-feed.dhan.co/twentydepth?token=abc&clientId=1000000001&authType=2")]
    [InlineData("wss://depth-api-feed.dhan.co/twentydepth?authType=2&clientId=1000000001&token=abc")] // any order
    [InlineData("wss://DEPTH-API-FEED.DHAN.CO/twentydepth?token=abc&clientId=1&authType=2")]
    public void The_depth_url_with_exactly_its_three_query_keys_is_allowed(string url) =>
        BrokerEndpoints.IsAllowedDepthSocket(new Uri(url)).ShouldBeTrue();

    [Theory]
    [InlineData("wss://depth-api-feed.dhan.co/twentydepth?version=2&token=abc&clientId=1&authType=2")] // version is not a key of this socket
    [InlineData("wss://depth-api-feed.dhan.co/twentydepth?token=abc&clientId=1&authType=2&extra=1")]
    [InlineData("wss://depth-api-feed.dhan.co/twentydepth?token=abc&clientId=1")] // a key missing
    [InlineData("wss://depth-api-feed.dhan.co/twentydepth?token=abc&clientId=1&authType=2&token=def")] // a key twice
    [InlineData("wss://depth-api-feed.dhan.co/twentydepth?token=abc&clientid=1&authType=2")] // spelled exactly
    [InlineData("wss://depth-api-feed.dhan.co/twentydepth?token=&clientId=1&authType=2")] // a value is needed
    [InlineData("wss://depth-api-feed.dhan.co/twentydepth")] // no query
    [InlineData("wss://depth-api-feed.dhan.co/twentydepth?token=abc&clientId=1&authType=2#x")]
    [InlineData("wss://depth-api-feed.dhan.co/?token=abc&clientId=1&authType=2")] // no path
    [InlineData("wss://depth-api-feed.dhan.co/twohundreddepth?token=abc&clientId=1&authType=2")] // another path
    [InlineData("wss://depth-api-feed.dhan.co/twentydepth/?token=abc&clientId=1&authType=2")]
    [InlineData("wss://full-depth-api.dhan.co/twohundreddepth?token=abc&clientId=1&authType=2&version=2")] // the 200-level host
    [InlineData("wss://full-depth-api.dhan.co/twentydepth?token=abc&clientId=1&authType=2")]
    [InlineData("wss://depth-api-feed.dhan.co.example.invalid/twentydepth?token=abc&clientId=1&authType=2")]
    [InlineData("wss://api-feed.dhan.co/twentydepth?token=abc&clientId=1&authType=2")] // the live feed's host
    [InlineData("ws://depth-api-feed.dhan.co/twentydepth?token=abc&clientId=1&authType=2")] // not TLS
    [InlineData("wss://depth-api-feed.dhan.co:8443/twentydepth?token=abc&clientId=1&authType=2")] // other port
    [InlineData("wss://user@depth-api-feed.dhan.co/twentydepth?token=abc&clientId=1&authType=2")]
    public void Every_other_depth_socket_is_refused(string url) =>
        BrokerEndpoints.IsAllowedDepthSocket(new Uri(url)).ShouldBeFalse();

    [Fact]
    public void The_two_sockets_do_not_allow_each_others_urls_and_the_built_depth_url_is_allowed()
    {
        var depth = BrokerEndpoints.BuildDepthUri("1000000001", "a b/c+d");
        var live = BrokerEndpoints.BuildFeedUri("1000000001", "abc");

        BrokerEndpoints.IsAllowedDepthSocket(depth).ShouldBeTrue();
        BrokerEndpoints.IsAllowedWebSocket(depth).ShouldBeFalse();
        BrokerEndpoints.IsAllowedWebSocket(live).ShouldBeTrue();
        BrokerEndpoints.IsAllowedDepthSocket(live).ShouldBeFalse();
        BrokerEndpoints.IsAllowedDepthSocket(null).ShouldBeFalse();
        BrokerEndpoints.IsAllowed(HttpMethod.Get, new Uri("https://depth-api-feed.dhan.co/twentydepth?token=abc&clientId=1&authType=2")).ShouldBeFalse();
        depth.Query.ShouldNotContain("version");
    }

    [Fact]
    public void The_depth_socket_is_listed_in_the_describe_text_of_the_phase_check()
    {
        BrokerEndpoints.Describe().ShouldContain("WSS depth-api-feed.dhan.co/twentydepth?authType&clientId&token");
        BrokerEndpoints.Describe().ShouldContain("WSS api-feed.dhan.co?authType&clientId&token&version");
    }

    [Fact]
    public void No_socket_endpoint_is_also_a_plain_http_endpoint()
    {
        BrokerEndpoints.IsAllowed(HttpMethod.Get, new Uri("https://api-feed.dhan.co?version=2&token=abc&clientId=1&authType=2")).ShouldBeFalse();
        BrokerEndpoints.IsAllowedWebSocket(new Uri("wss://api.dhan.co/v2/holdings")).ShouldBeFalse();
        BrokerEndpoints.IsAllowedWebSocket(null).ShouldBeFalse();
    }

    [Fact]
    public void The_feed_url_is_built_from_the_credentials_and_passes_the_allow_list()
    {
        var uri = BrokerEndpoints.BuildFeedUri("1000000001", "abc.def-ghi");

        BrokerEndpoints.IsAllowedWebSocket(uri).ShouldBeTrue();
        uri.Query.ShouldBe("?version=2&token=abc.def-ghi&clientId=1000000001&authType=2");
    }

    [Fact]
    public void Values_that_try_to_add_a_query_key_are_escaped_and_cannot_widen_the_url()
    {
        var uri = BrokerEndpoints.BuildFeedUri("1&token=evil#frag", "a&extra=1=b c");

        BrokerEndpoints.IsAllowedWebSocket(uri).ShouldBeTrue();
        uri.Query.Split('&').Length.ShouldBe(4);
        uri.Fragment.ShouldBeEmpty();
    }

    [Fact]
    public void The_socket_refusal_names_no_query()
    {
        var error = BrokerEndpointNotAllowedException.ForSocket(new Uri("wss://evil.example.invalid/feed?token=FAKE-SECRET-MARKER"));

        error.Message.ShouldContain("evil.example.invalid/feed");
        error.Message.ShouldNotContain("FAKE-SECRET-MARKER");
    }
}
