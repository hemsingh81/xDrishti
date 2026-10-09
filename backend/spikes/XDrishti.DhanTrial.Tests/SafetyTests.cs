using System.Net;
using Microsoft.Extensions.Time.Testing;

namespace XDrishti.DhanTrial.Tests;

public class EndpointPolicyTests
{
    [Theory]
    [InlineData("GET", "https://api.dhan.co/v2/orders")]
    [InlineData("POST", "https://api.dhan.co/v2/orders")]
    [InlineData("POST", "https://api.dhan.co/v2/super/orders")]
    [InlineData("GET", "https://api.dhan.co/v2/positions")]
    [InlineData("GET", "https://api.dhan.co/v2/holdings")]
    [InlineData("GET", "https://api.dhan.co/v2/fundlimit")]
    [InlineData("POST", "https://api.dhan.co/v2/ip/setIP")]
    [InlineData("PUT", "https://api.dhan.co/v2/ip/modifyIP")]
    [InlineData("DELETE", "https://api.dhan.co/v2/orders/123")]
    [InlineData("GET", "https://evil.example.com/v2/profile")]
    [InlineData("GET", "http://api.dhan.co/v2/profile")]
    public void Order_fund_ip_and_foreign_endpoints_are_blocked(string method, string url)
    {
        EndpointPolicy.IsAllowed(new HttpMethod(method), new Uri(url)).ShouldBeFalse();
    }

    [Theory]
    [InlineData("POST", "https://api.dhan.co/v2/charts/intraday")]
    [InlineData("POST", "https://api.dhan.co/v2/charts/historical")]
    [InlineData("GET", "https://api.dhan.co/v2/profile")]
    [InlineData("GET", "https://api.dhan.co/v2/RenewToken")]
    [InlineData("GET", "https://images.dhan.co/api-data/api-scrip-master.csv")]
    [InlineData("POST", "https://auth.dhan.co/app/generate-consent?client_id=1")]
    public void Authentication_profile_master_and_history_endpoints_are_allowed(string method, string url)
    {
        EndpointPolicy.IsAllowed(new HttpMethod(method), new Uri(url)).ShouldBeTrue();
    }

    [Fact]
    public async Task The_http_pipeline_refuses_a_blocked_request_before_sending()
    {
        var inner = new FakeHttp(_ => (HttpStatusCode.OK, "{}"));
        using var client = new HttpClient(new ReadOnlyGuardHandler(inner));

        await Should.ThrowAsync<InvalidOperationException>(() => client.PostAsync(new Uri("https://api.dhan.co/v2/orders"), new StringContent("{}")));

        inner.Requests.ShouldBeEmpty();
    }
}

public class GuardedHttpTests
{
    [Fact]
    public void Redirects_are_never_followed_so_credentials_cannot_travel_to_another_host()
    {
        using var handler = GuardedHttp.CreateHandler();

        handler.AllowAutoRedirect.ShouldBeFalse();
    }

    [Fact]
    public async Task A_redirect_response_is_reported_as_an_error_and_not_followed()
    {
        var http = new FakeHttp(_ => (HttpStatusCode.Found, "{}"));
        using var client = new HttpClient(new ReadOnlyGuardHandler(http));
        var api = new DhanApi(client, new NoRateLimiter(), new Redactor(), new FakeTimeProvider(), maxAttempts: 1);

        var response = await api.GetProfileAsync("t", CancellationToken.None);

        response.Ok.ShouldBeFalse();
        response.ErrorCode.ShouldBe("HTTP 302");
        http.Requests.Count.ShouldBe(1);
    }
}

public class RedactorTests
{
    [Fact]
    public void Known_secrets_jwts_and_token_query_parameters_are_masked()
    {
        var redactor = new Redactor(["my-api-secret-value"]);
        const string Jwt = "eyJhbGciOiJIUzUxMiJ9.eyJpc3MiOiJkaGFuIn0.c2lnbmF0dXJl";

        var text = redactor.Redact($"secret=my-api-secret-value token={Jwt} url=https://x/y?pin=123456&totp=987654&ok=1");

        text.ShouldNotContain("my-api-secret-value");
        text.ShouldNotContain("eyJhbGci");
        text.ShouldNotContain("123456");
        text.ShouldNotContain("987654");
        text.ShouldContain("ok=1");
    }

    [Fact]
    public void Secrets_added_later_are_masked_too()
    {
        var redactor = new Redactor();
        redactor.Add("late-token-value");

        redactor.Redact("x late-token-value y").ShouldBe("x <redacted> y");
    }

    [Fact]
    public void The_access_token_type_never_prints_its_value()
    {
        var token = new AccessToken("super-secret-token", DateTimeOffset.UnixEpoch, "1", "test", DateTimeOffset.UnixEpoch);

        token.ToString().ShouldNotContain("super-secret-token");
    }
}

public class SanitizerTests
{
    [Fact]
    public void Samples_drop_identity_fields_trim_arrays_and_mask_tokens()
    {
        var redactor = new Redactor();
        var json = "{\"dhanClientId\":\"1000000001\",\"accessToken\":\"abc\",\"dataPlan\":\"Active\",\"open\":[1,2,3,4,5,6,7,8],\"note\":\"x\"}";

        var sample = Sanitizer.Sanitize(json, redactor);

        sample.ShouldNotContain("1000000001");
        sample.ShouldNotContain("\"abc\"");
        sample.ShouldContain("Active");
        sample.ShouldNotContain("8");
    }
}

public class TotpTests
{
    [Fact]
    public void Matches_the_RFC_6238_reference_vector()
    {
        // RFC 6238 appendix B: ASCII secret "12345678901234567890", T=59 s, SHA-1 → 94287082 (8 digits).
        var code = Totp.Generate("12345678901234567890"u8.ToArray(), DateTimeOffset.FromUnixTimeSeconds(59), 8);

        code.ShouldBe("94287082");
    }

    [Fact]
    public void Decodes_base32_secrets()
    {
        Totp.DecodeBase32("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ").ShouldBe("12345678901234567890"u8.ToArray());
    }
}

public class RateLimiterTests
{
    [Fact]
    public async Task Calls_are_spaced_by_the_minimum_interval()
    {
        var clock = new FakeTimeProvider();
        using var limiter = new MinIntervalRateLimiter(TimeSpan.FromMilliseconds(200), clock);
        await limiter.WaitAsync(CancellationToken.None);

        var second = limiter.WaitAsync(CancellationToken.None);
        await Task.Yield();
        second.IsCompleted.ShouldBeFalse();

        clock.Advance(TimeSpan.FromMilliseconds(200));
        await second.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        second.IsCompletedSuccessfully.ShouldBeTrue();
    }
}
