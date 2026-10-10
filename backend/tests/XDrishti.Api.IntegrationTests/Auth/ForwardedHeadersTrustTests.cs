using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Auth;

/// <summary>The client address (rate-limit key) comes from X-Forwarded-For only when the connection is from a trusted proxy.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class ForwardedHeadersTrustTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task X_Forwarded_For_from_an_untrusted_peer_does_not_change_the_rate_limit_key()
    {
        await using var untrusted = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddTransient<IStartupFilter, PublicPeerStartupFilter>()));
        using var client = untrusted.CreateClient();

        // Ten attempts, each claiming to come from a different address: all of them are the one real peer.
        for (var i = 1; i <= 10; i++)
        {
            (await LoginAsync(client, $"198.51.100.{i}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        var limited = await LoginAsync(client, "198.51.100.200");

        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task X_Forwarded_For_from_a_trusted_proxy_separates_the_rate_limit_keys()
    {
        using var client = factory.CreateAnonymousClient(); // the test peer is loopback, a trusted proxy
        client.DefaultRequestHeaders.Remove("X-Forwarded-For");

        for (var i = 1; i <= 11; i++)
        {
            (await LoginAsync(client, $"198.18.77.{i}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
    }

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string forwardedFor)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/auth/login", UriKind.Relative))
        {
            Content = JsonContent.Create(new { email = "nobody@example.invalid", password = "wrong" }),
        };
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        return await client.SendAsync(request, Ct);
    }

    /// <summary>The peer is a public address, not one of the proxy networks. Runs after the factory's loopback filter and replaces its address.</summary>
    private sealed class PublicPeerStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.99");
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
