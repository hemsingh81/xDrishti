using XDrishti.Infrastructure.Market;

namespace XDrishti.Infrastructure.Tests.Market;

/// <summary>Only the two public master files can be fetched, with GET (ADR 0010).</summary>
public sealed class MasterDataEndpointsTests
{
    [Theory]
    [InlineData("https://images.dhan.co/api-data/api-scrip-master-detailed.csv")]
    [InlineData("https://IMAGES.DHAN.CO/api-data/api-scrip-master-detailed.csv")]
    [InlineData("https://nsearchives.nseindia.com/content/indices/ind_niftytotalmarket_list.csv")]
    public void The_two_files_are_allowed(string url) => MasterDataEndpoints.IsAllowed(HttpMethod.Get, new Uri(url)).ShouldBeTrue();

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
    public void Everything_else_is_refused(string url) => MasterDataEndpoints.IsAllowed(HttpMethod.Get, new Uri(url)).ShouldBeFalse();

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("HEAD")]
    public void Only_GET_is_allowed(string method) =>
        MasterDataEndpoints.IsAllowed(new HttpMethod(method), MasterDataEndpoints.DhanScripMaster).ShouldBeFalse();

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
    public void The_download_limits_are_the_documented_ones()
    {
        MasterDataHttp.DhanMaxBytes.ShouldBe(100L * 1024 * 1024);
        MasterDataHttp.NseMaxBytes.ShouldBe(5L * 1024 * 1024);
        MasterDataHttp.DhanTimeout.ShouldBe(TimeSpan.FromSeconds(120));
        MasterDataHttp.NseTimeout.ShouldBe(TimeSpan.FromSeconds(30));
        MasterDataHttp.UserAgent.ShouldBe("xDrishti/0.1 (personal use)");
    }

    [Fact]
    public void The_primary_handler_follows_no_redirects_and_keeps_no_cookies()
    {
        using var handler = (SocketsHttpHandler)MasterDataHttp.CreatePrimaryHandler();

        (handler.AllowAutoRedirect, handler.UseCookies, handler.UseProxy, handler.Credentials).ShouldBe((false, false, false, null));
    }
}
