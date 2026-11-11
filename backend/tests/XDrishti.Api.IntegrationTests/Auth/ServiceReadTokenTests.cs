using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Auth;

/// <summary>
/// BK-12b AC 1 to 3 (ADR 0267): the read-only service token of the tool server opens exactly the two basket report routes and nothing else. Every endpoint of the host is enumerated from the endpoint data source and called with the token and no session, with every method it declares; the
/// token is checked in fixed time (a wrong, empty or missing one is the same 401); an unset or shared token stops or disables the scheme; the principal has no owner claim and passes no policy but its own.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed partial class ServiceReadTokenTests(ApiFactory factory)
{
    private const string ProfileName = "GetBasketProfile";
    private const string RankingName = "GetBasketRanking";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient ClientWith(string? token)
    {
        var client = factory.CreateAnonymousClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    private static string Fill(string pattern) =>
        TemplateParameter().Replace(pattern, m => m.Groups["constraint"].Value switch
        {
            "int" or "long" => "1",
            "guid" => Guid.NewGuid().ToString(),
            _ => "x",
        });

    [GeneratedRegex(@"\{\*{0,2}\w+(?::(?<constraint>\w+)(?:\([^}]*\))?)?\??\}", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TemplateParameter();

    private List<(RouteEndpoint Endpoint, string Method, string Path)> Calls() =>
        [.. factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET", "POST", "PUT", "PATCH", "DELETE"]).Select(m => (e, m, Fill(e.RoutePattern.RawText!))))];

    [Fact]
    public async Task The_token_opens_the_two_basket_routes_and_every_other_endpoint_and_method_answers_401()
    {
        var calls = Calls();
        calls.Count.ShouldBeGreaterThan(300);
        var opened = new List<string>();
        var anonymous = new List<string>();
        var wrong = new List<string>();

        foreach (var (endpoint, method, path) in calls)
        {
            if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            {
                // open to everyone whatever the token: the token must change nothing there
                anonymous.Add(endpoint.RoutePattern.RawText!);
                if (method == "GET")
                {
                    using var plain = factory.CreateAnonymousClient();
                    using var bearer = ClientWith(factory.ServiceToken);
                    using var without = await plain.GetAsync(new Uri(path, UriKind.Relative), Ct);
                    using var with = await bearer.GetAsync(new Uri(path, UriKind.Relative), Ct);
                    with.StatusCode.ShouldBe(without.StatusCode, path);
                }

                continue;
            }

            using var client = ClientWith(factory.ServiceToken);
            using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative));
            using var response = await client.SendAsync(request, Ct);
            var name = endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName;
            if (name is ProfileName or RankingName)
            {
                opened.Add($"{method} {name} {(int)response.StatusCode}");
            }
            else if (response.StatusCode != HttpStatusCode.Unauthorized)
            {
                wrong.Add($"{method} {endpoint.RoutePattern.RawText} -> {(int)response.StatusCode}");
            }
        }

        wrong.ShouldBeEmpty();
        opened.ShouldBe([$"GET {ProfileName} 422", $"GET {RankingName} 422"], ignoreOrder: true); // past the guard, into the handlers: a malformed id, a ranking of fewer than two baskets
        anonymous.Distinct().Order().ShouldBe(["/api/auth/login", "/api/auth/session", "/api/brokers/dhan/callback", "/api/docs/favicon.svg", "/api/docs/scalar.aspnetcore.js", "/api/docs/scalar.js", "/health/live", "/health/ready"]);
    }

    [Fact]
    public async Task A_non_get_of_the_two_paths_with_the_token_is_never_executed()
    {
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete })
        {
            using var client = ClientWith(factory.ServiceToken);
            using var profile = await client.SendAsync(new HttpRequestMessage(method, new Uri($"/api/reports/baskets/{Guid.NewGuid()}/profile", UriKind.Relative)), Ct);
            using var ranking = await client.SendAsync(new HttpRequestMessage(method, new Uri("/api/reports/baskets/ranking", UriKind.Relative)), Ct);

            profile.StatusCode.ShouldBeOneOf(HttpStatusCode.MethodNotAllowed, HttpStatusCode.Unauthorized, HttpStatusCode.NotFound);
            ranking.StatusCode.ShouldBeOneOf(HttpStatusCode.MethodNotAllowed, HttpStatusCode.Unauthorized, HttpStatusCode.NotFound);
        }
    }

    [Fact]
    public async Task A_valid_service_token_reaches_the_profile_handler_with_no_session()
    {
        using var client = ClientWith(factory.ServiceToken);

        using var response = await client.GetAsync(new Uri($"/api/reports/baskets/{Guid.NewGuid()}/profile", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound); // the handler answered: no basket of that id
        (await response.ReadProblemAsync(Ct)).Code.ShouldBe("reports.not_found");
    }

    [Theory]
    [InlineData("wrong")]
    [InlineData("")]
    [InlineData(null)]
    public async Task A_wrong_empty_or_missing_token_is_the_same_401_with_no_detail(string? token)
    {
        using var client = ClientWith(token);

        using var response = await client.GetAsync(new Uri($"/api/reports/baskets/{Guid.NewGuid()}/profile", UriKind.Relative), Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.ReadProblemAsync(Ct)).Code.ShouldBe("auth.unauthenticated");
        body.ShouldNotContain(factory.ServiceToken);
        response.Headers.Location.ShouldBeNull();
    }

    [Fact]
    public async Task A_token_that_differs_only_in_the_last_character_or_in_length_is_refused()
    {
        foreach (var token in new[] { factory.ServiceToken[..^1] + (factory.ServiceToken[^1] == 'A' ? 'B' : 'A'), factory.ServiceToken[..^1], factory.ServiceToken + "x" })
        {
            using var client = ClientWith(token);
            using var response = await client.GetAsync(new Uri("/api/reports/baskets/ranking", UriKind.Relative), Ct);

            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
    }

    [Fact]
    public async Task With_no_service_token_configured_the_scheme_accepts_nothing_not_even_an_empty_bearer()
    {
        await using var unset = factory.WithWebHostBuilder(b => b.UseSetting("Api:ServiceTokens:Mcp", string.Empty));

        foreach (var token in new[] { string.Empty, "x", factory.ServiceToken })
        {
            using var anonymous = unset.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
            anonymous.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await anonymous.GetAsync(new Uri("/api/reports/baskets/ranking", UriKind.Relative), Ct);

            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
    }

    [Theory]
    [InlineData("BrokerVault:Key")]
    [InlineData("ExecHandover:Key")]
    [InlineData("Assistant:Mcp:Token")]
    public void The_host_refuses_to_start_when_the_service_token_equals_another_secret(string other)
    {
        var shared = other switch
        {
            "BrokerVault:Key" => factory.VaultKey,
            "ExecHandover:Key" => factory.HandoverKey,
            _ => Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
        };
        using var clash = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Api:ServiceTokens:Mcp", shared);
            b.UseSetting(other, shared);
        });

        Should.Throw<Exception>(() => clash.CreateClient()).ToString().ShouldContain("Api:ServiceTokens:Mcp equals " + other);
    }

    [Fact]
    public void The_host_refuses_to_start_with_a_short_service_token()
    {
        using var shortToken = factory.WithWebHostBuilder(b => b.UseSetting("Api:ServiceTokens:Mcp", "short"));

        Should.Throw<Exception>(() => shortToken.CreateClient()).ToString().ShouldContain("at least 32 characters");
    }

    [Fact]
    public async Task The_service_principal_has_no_owner_claim_and_passes_only_its_own_policy()
    {
        var provider = factory.Services.GetRequiredService<IAuthorizationPolicyProvider>();
        var evaluator = factory.Services.GetRequiredService<IPolicyEvaluator>();
        var names = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .SelectMany(e => e.Metadata.GetOrderedMetadata<IAuthorizeData>()).Select(a => a.Policy).Where(p => !string.IsNullOrEmpty(p)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Cast<string>().ToList();
        names.ShouldContain("AssistantBasketRead");
        var policies = new Dictionary<string, AuthorizationPolicy>();
        foreach (var name in names)
        {
            policies[name] = (await provider.GetPolicyAsync(name))!;
        }

        policies["(fallback)"] = (await provider.GetFallbackPolicyAsync())!;

        var passed = new List<string>();
        ClaimsPrincipal? principal = null;
        foreach (var (name, policy) in policies)
        {
            using var scope = factory.Services.CreateScope();
            var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            context.Request.Headers.Authorization = "Bearer " + factory.ServiceToken;
            var authentication = await evaluator.AuthenticateAsync(policy, context);
            if (!authentication.Succeeded)
            {
                continue;
            }

            var decision = await evaluator.AuthorizeAsync(policy, authentication, context, null);
            if (decision.Succeeded)
            {
                passed.Add(name);
                principal = authentication.Principal;
            }
        }

        passed.ShouldBe(["AssistantBasketRead"]);
        principal!.Claims.Select(c => (c.Type, c.Value)).ShouldBe([("xd:service", "mcp")]);
        principal.FindFirst(ClaimTypes.NameIdentifier).ShouldBeNull();
        principal.FindFirst(ClaimTypes.Email).ShouldBeNull();
        principal.FindFirst("xd:sid").ShouldBeNull();
    }

    [Fact]
    public async Task Calls_with_the_token_write_no_session_and_no_audit_row()
    {
        var before = await CountsAsync();
        using var client = ClientWith(factory.ServiceToken);

        using var response = await client.GetAsync(new Uri($"/api/reports/baskets/{Guid.NewGuid()}/profile", UriKind.Relative), Ct);
        using var wrong = await ClientWith("wrong").GetAsync(new Uri("/api/reports/baskets/ranking", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        wrong.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CountsAsync()).ShouldBe(before);
    }

    [Fact]
    public async Task The_token_never_reaches_the_logs()
    {
        using var client = ClientWith(factory.ServiceToken);
        using var response = await client.GetAsync(new Uri($"/api/reports/baskets/{Guid.NewGuid()}/profile", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        factory.Logs.All.ShouldNotContain(line => line.Contains(factory.ServiceToken, StringComparison.Ordinal));
    }

    private async Task<(long Sessions, long Audit)> CountsAsync()
    {
        await using var connection = new Npgsql.NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new Npgsql.NpgsqlCommand("SELECT (SELECT count(*) FROM auth.sessions), (SELECT count(*) FROM auth.audit_events)", connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        await reader.ReadAsync(Ct);
        return (reader.GetInt64(0), reader.GetInt64(1));
    }
}
