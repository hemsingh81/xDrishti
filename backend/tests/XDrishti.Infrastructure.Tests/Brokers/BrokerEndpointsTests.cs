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
    public void The_documented_endpoints_are_allowed(string method, string url) =>
        BrokerEndpoints.IsAllowed(new HttpMethod(method), new Uri(url)).ShouldBeTrue();

    [Theory]
    [InlineData("GET", "https://api.dhan.co/v2/orders")] // orders
    [InlineData("POST", "https://api.dhan.co/v2/orders")]
    [InlineData("POST", "https://api.dhan.co/v2/super/orders")]
    [InlineData("POST", "https://api.dhan.co/v2/marketfeed/ltp")]
    [InlineData("GET", "https://api.dhan.co/v2/trades")]
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
}
