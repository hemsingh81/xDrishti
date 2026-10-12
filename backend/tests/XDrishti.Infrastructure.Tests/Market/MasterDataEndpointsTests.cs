using XDrishti.Infrastructure.Market;

namespace XDrishti.Infrastructure.Tests.Market;

/// <summary>Only the two public master files and NSE's trading-holiday list can be fetched, with GET (ADR 0010, 0017).</summary>
public sealed class MasterDataEndpointsTests
{
    [Theory]
    [InlineData("https://images.dhan.co/api-data/api-scrip-master-detailed.csv")]
    [InlineData("https://IMAGES.DHAN.CO/api-data/api-scrip-master-detailed.csv")]
    [InlineData("https://nsearchives.nseindia.com/content/indices/ind_niftytotalmarket_list.csv")]
    [InlineData("https://www.nseindia.com/api/holiday-master?type=trading")]
    [InlineData("https://WWW.NSEINDIA.COM/api/holiday-master?type=trading")]
    public void The_allowed_endpoints_are_allowed(string url) => MasterDataEndpoints.IsAllowed(HttpMethod.Get, new Uri(url)).ShouldBeTrue();

    [Theory]
    [InlineData("https://images.dhan.co/api-data/api-scrip-master.csv")] // the compact file
    [InlineData("https://images.dhan.co/api-data/api-scrip-master-detailed.csv?x=1")] // no query
    [InlineData("https://images.dhan.co/api-data/API-SCRIP-MASTER-DETAILED.CSV")] // paths are exact
    [InlineData("https://images.dhan.co/api-data/")]
    [InlineData("https://images.dhan.co/")]
    [InlineData("http://images.dhan.co/api-data/api-scrip-master-detailed.csv")] // not HTTPS
    [InlineData("https://images.dhan.co:8443/api-data/api-scrip-master-detailed.csv")]
    [InlineData("https://user@images.dhan.co/api-data/api-scrip-master-detailed.csv")]
    [InlineData("https://images.dhan.co.example.invalid/api-data/api-scrip-master-detailed.csv")]
    [InlineData("https://nsearchives.nseindia.com/content/indices/ind_nifty50list.csv")]
    [InlineData("https://nsearchives.nseindia.com/content/indices/ind_niftytotalmarket_list.csv#frag")]
    [InlineData("https://nsearchives.nseindia.com/api/anything")]
    [InlineData("https://api.dhan.co/v2/orders")]
    [InlineData("https://www.nseindia.com/content/indices/ind_niftytotalmarket_list.csv")]
    [InlineData("https://www.nseindia.com/api/holiday-master")] // the holiday list needs its query, exactly
    [InlineData("https://www.nseindia.com/api/holiday-master?")]
    [InlineData("https://www.nseindia.com/api/holiday-master?type=clearing")]
    [InlineData("https://www.nseindia.com/api/holiday-master?type=trading&x=1")]
    [InlineData("https://www.nseindia.com/api/holiday-master?x=1&type=trading")]
    [InlineData("https://www.nseindia.com/api/holiday-master?TYPE=trading")]
    [InlineData("https://www.nseindia.com/api/holiday-master?type=Trading")]
    [InlineData("https://www.nseindia.com/api/holiday-master?type=trading#frag")]
    [InlineData("https://www.nseindia.com/api/holiday-master/?type=trading")]
    [InlineData("https://www.nseindia.com/api/holiday-master?type=trading%20")]
    [InlineData("http://www.nseindia.com/api/holiday-master?type=trading")]
    [InlineData("https://www.nseindia.com:8443/api/holiday-master?type=trading")]
    [InlineData("https://user@www.nseindia.com/api/holiday-master?type=trading")]
    [InlineData("https://nseindia.com/api/holiday-master?type=trading")]
    [InlineData("https://www.nseindia.com.example.invalid/api/holiday-master?type=trading")]
    [InlineData("https://www.nseindia.com/")] // the home page (where cookies come from) is not allowed
    [InlineData("https://www.nseindia.com/api/marketStatus")]
    [InlineData("https://images.dhan.co/api-data/api-scrip-master-detailed.csv?type=trading")]
    public void Everything_else_is_refused(string url) => MasterDataEndpoints.IsAllowed(HttpMethod.Get, new Uri(url)).ShouldBeFalse();

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("HEAD")]
    public void Only_GET_is_allowed(string method)
    {
        MasterDataEndpoints.IsAllowed(new HttpMethod(method), MasterDataEndpoints.DhanScripMaster).ShouldBeFalse();
        MasterDataEndpoints.IsAllowed(new HttpMethod(method), MasterDataEndpoints.NseHolidayMaster).ShouldBeFalse();
    }

    [Fact]
    public void Nothing_and_relative_addresses_are_refused()
    {
        MasterDataEndpoints.IsAllowed(null, MasterDataEndpoints.DhanScripMaster).ShouldBeFalse();
        MasterDataEndpoints.IsAllowed(HttpMethod.Get, null).ShouldBeFalse();
        MasterDataEndpoints.IsAllowed(HttpMethod.Get, new Uri("/api-data/api-scrip-master-detailed.csv", UriKind.Relative)).ShouldBeFalse();
    }

    [Fact]
    public async Task The_guard_refuses_before_anything_is_sent_and_names_no_query()
    {
        using var source = new StubMasterData();
        using var client = source.CreateClient(MasterDataHttpClientNames.Dhan);

        var error = await Should.ThrowAsync<MasterDataEndpointNotAllowedException>(
            () => client.GetAsync(new Uri("https://images.dhan.co/api-data/other.csv?secret=FAKE-SECRET-MARKER"), TestContext.Current.CancellationToken));

        source.Requests.ShouldBeEmpty();
        error.Message.ShouldContain("images.dhan.co/api-data/other.csv");
        error.Message.ShouldNotContain("FAKE-SECRET-MARKER");
    }

    [Fact]
    public async Task The_guard_lets_the_holiday_list_through_and_sends_no_cookie_or_credentials()
    {
        using var source = new StubMasterData();
        using var client = source.CreateClient(MasterDataHttpClientNames.NseApi);

        using var response = await client.GetAsync(MasterDataEndpoints.NseHolidayMaster, TestContext.Current.CancellationToken);

        var (method, uri, headers) = source.Requests.ShouldHaveSingleItem();
        (method, uri.ToString()).ShouldBe((HttpMethod.Get, "https://www.nseindia.com/api/holiday-master?type=trading"));
        headers.Keys.ShouldNotContain(k => k == "Authorization" || k == "Cookie" || k == "Proxy-Authorization" || k == "access-token" || k == "client-id");
        headers["User-Agent"].ShouldBe(MasterDataHttp.BrowserUserAgent);
        headers["Accept"].ShouldStartWith("application/json");
    }

    [Fact]
    public async Task The_guard_still_refuses_the_holiday_path_with_another_query()
    {
        using var source = new StubMasterData();
        using var client = source.CreateClient(MasterDataHttpClientNames.NseApi);

        var error = await Should.ThrowAsync<MasterDataEndpointNotAllowedException>(
            () => client.GetAsync(new Uri("https://www.nseindia.com/api/holiday-master?type=trading&secret=FAKE-SECRET-MARKER"), TestContext.Current.CancellationToken));

        source.Requests.ShouldBeEmpty();
        error.Message.ShouldNotContain("FAKE-SECRET-MARKER");
    }

    [Fact]
    public void The_download_limits_are_the_documented_ones()
    {
        MasterDataHttp.DhanMaxBytes.ShouldBe(100L * 1024 * 1024);
        MasterDataHttp.NseMaxBytes.ShouldBe(5L * 1024 * 1024);
        MasterDataHttp.DhanTimeout.ShouldBe(TimeSpan.FromSeconds(120));
        MasterDataHttp.NseTimeout.ShouldBe(TimeSpan.FromSeconds(30));
        MasterDataHttp.UserAgent.ShouldBe("xDrishti/0.1 (personal use)");
        MasterDataHttp.HolidayMaxBytes.ShouldBe(1L * 1024 * 1024);
        MasterDataHttp.HolidayTimeout.ShouldBe(TimeSpan.FromSeconds(20));
    }

    [Fact]
    public void The_primary_handler_follows_no_redirects_and_keeps_no_cookies()
    {
        using var handler = (SocketsHttpHandler)MasterDataHttp.CreatePrimaryHandler();

        (handler.AllowAutoRedirect, handler.UseCookies, handler.UseProxy, handler.Credentials).ShouldBe((false, false, false, null));
    }
}
