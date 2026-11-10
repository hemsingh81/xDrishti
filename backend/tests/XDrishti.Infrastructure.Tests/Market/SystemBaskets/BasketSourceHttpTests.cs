using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Market.Baskets;
using XDrishti.Infrastructure.Market.SystemBaskets;

namespace XDrishti.Infrastructure.Tests.Market.SystemBaskets;

/// <summary>BK-01: the file fetch goes through its own guard, sends the stored validators, never follows a redirect, and with the setting off no HTTP client exists at all.</summary>
public sealed class BasketSourceHttpTests : HttpMessageHandler, IHttpClientFactory
{
    private const string Url = "https://nsearchives.nseindia.com/content/indices/ind_nifty50list.csv";

    private readonly List<HttpRequestMessage> _requests = [];

    private Func<HttpResponseMessage> _respond = () => new HttpResponseMessage(HttpStatusCode.OK);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public HttpClient CreateClient(string name) => new(new BasketSourceEndpointGuard { InnerHandler = this }, disposeHandler: false);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _requests.Add(request);
        return Task.FromResult(_respond());
    }

    private static BasketSourceInfo Source(string? alt = null, string? etag = null, string? lastModified = null) =>
        new(Guid.NewGuid(), "NSE Nifty 50", "ind_nifty50list.csv", Url, alt, 50, 50, true, "never", null, null, etag, lastModified, null, null, 0, 0);

    private NseBasketSourceFetcher Fetcher() => new(this, new FakeTimeProvider(), NullLogger<NseBasketSourceFetcher>.Instance);

    [Theory]
    [InlineData("https://nsearchives.nseindia.com/content/indices/ind_nifty50list.csv", true)]
    [InlineData("https://niftyindices.com/IndexConstituent/ind_nifty100Quality30list.csv", true)]
    [InlineData("https://www.nseindia.com/api/equity-stockIndices?index=NIFTY%2050", false)]
    [InlineData("https://nsearchives.nseindia.com/content/indices/ind_nifty50list.csv?x=1", false)]
    [InlineData("http://nsearchives.nseindia.com/content/indices/ind_nifty50list.csv", false)]
    [InlineData("https://nsearchives.nseindia.com/content/fo/fo_mktlots.csv", false)]
    [InlineData("https://nsearchives.nseindia.com/content/indices/../fo/x.csv", false)]
    [InlineData("https://niftyindices.com/content/indices/ind_nifty50list.csv", false)]
    [InlineData("https://nsearchives.nseindia.com:8443/content/indices/ind_nifty50list.csv", false)]
    [InlineData("https://evil.example.invalid/content/indices/ind_nifty50list.csv", false)]
    public void Only_a_plain_csv_of_the_two_index_folders_is_allowed(string url, bool allowed)
    {
        BasketSourceEndpointGuard.IsAllowed(HttpMethod.Get, new Uri(url)).ShouldBe(allowed);
    }

    [Fact]
    public void Only_get_is_allowed()
    {
        BasketSourceEndpointGuard.IsAllowed(HttpMethod.Post, new Uri(Url)).ShouldBeFalse();
    }

    [Fact]
    public async Task The_stored_validators_are_sent_and_a_304_is_passed_on()
    {
        _respond = () => new HttpResponseMessage(HttpStatusCode.NotModified);

        var response = await Fetcher().GetAsync(Source(etag: "\"v1\"", lastModified: "Tue, 21 Oct 2026 10:00:00 GMT"), Ct);

        response.Status.ShouldBe(304);
        var request = _requests.Single();
        request.Headers.GetValues("If-None-Match").Single().ShouldBe("\"v1\"");
        request.Headers.GetValues("If-Modified-Since").Single().ShouldBe("Tue, 21 Oct 2026 10:00:00 GMT");
    }

    [Fact]
    public async Task A_file_comes_back_with_its_content_type_validator_and_bytes()
    {
        _respond = () =>
        {
            var ok = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("a,b\n", Encoding.UTF8, "text/csv") };
            ok.Headers.ETag = System.Net.Http.Headers.EntityTagHeaderValue.Parse("\"abc\"");
            return ok;
        };

        var response = await Fetcher().GetAsync(Source(), Ct);

        (response.Status, response.ContentType, response.Etag).ShouldBe((200, "text/csv; charset=utf-8", "\"abc\""));
        Encoding.UTF8.GetString(response.Body!).ShouldBe("a,b\n");
    }

    [Fact]
    public async Task A_redirect_is_returned_as_it_is_and_never_followed()
    {
        _respond = () => new HttpResponseMessage(HttpStatusCode.Redirect);

        var response = await Fetcher().GetAsync(Source(), Ct);

        response.Status.ShouldBe(302);
        _requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_alternate_host_is_tried_after_a_404_and_not_after_a_403()
    {
        var calls = 0;
        _respond = () => ++calls == 1 ? new HttpResponseMessage(HttpStatusCode.NotFound) : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("x", Encoding.UTF8, "text/csv") };
        var alt = "https://niftyindices.com/IndexConstituent/ind_nifty50list.csv";

        var after404 = await Fetcher().GetAsync(Source(alt), Ct);
        calls = 0;
        _respond = () => { calls++; return new HttpResponseMessage(HttpStatusCode.Forbidden); };
        var after403 = await Fetcher().GetAsync(Source(alt), Ct);

        after404.Status.ShouldBe(200);
        (after403.Status, calls).ShouldBe((403, 1));
        _requests.Select(r => r.RequestUri!.Host).Take(2).ShouldBe(["nsearchives.nseindia.com", "niftyindices.com"]);
    }

    [Fact]
    public async Task A_catalogue_url_outside_the_guard_is_refused_before_sending()
    {
        var source = Source() with { SourceUrl = "https://www.nseindia.com/api/anything" };

        var response = await Fetcher().GetAsync(source, Ct);

        (response.Status, response.Error).ShouldBe((0, "not_allowed"));
        _requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_file_over_the_size_cap_is_not_read()
    {
        _respond = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[NseBasketSourceFetcher.MaxBytes + 1]) };

        var response = await Fetcher().GetAsync(Source(), Ct);

        (response.Body, response.Error).ShouldBe((null, "too_large"));
    }

    [Fact]
    public async Task With_the_setting_off_no_http_client_is_registered_and_the_fetcher_makes_no_request()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider());
        services.AddSystemBasketSources(new ConfigurationBuilder().Build());

        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IBasketSourceFetcher>().GetType().Name.ShouldBe("DisabledBasketSourceFetcher");
        provider.GetService<IHttpClientFactory>().ShouldBeNull();
        provider.GetRequiredService<IBasketFetchSettings>().Enabled.ShouldBeFalse();
        var response = await provider.GetRequiredService<IBasketSourceFetcher>().GetAsync(Source(), Ct);
        (response.Status, response.Error).ShouldBe((0, "disabled"));
    }

    [Fact]
    public void With_the_setting_on_the_guarded_client_and_the_real_fetcher_are_registered()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider());
        services.AddSystemBasketSources(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Baskets:System:Fetch:Enabled"] = "true", ["Baskets:System:Fetch:UserAgent"] = "xDrishti (owner@example.invalid)" }).Build());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IBasketSourceFetcher>().GetType().Name.ShouldBe("NseBasketSourceFetcher");
        scope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(NseBasketSourceFetcher.ClientName).DefaultRequestHeaders.UserAgent.ToString().ShouldContain("xDrishti");
    }
}
