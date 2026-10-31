using XDrishti.Execution.Dhan;
using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Tests.Dhan;

public sealed class ExecutionEndpointsTests
{
    private static Uri U(string s) => new(s, UriKind.Absolute);

    [Theory]
    [InlineData("POST", "https://sandbox.dhan.co/v2/orders")]
    [InlineData("POST", "https://sandbox.dhan.co/v2/super/orders")] // P12-04, UNVERIFIED path
    [InlineData("PUT", "https://sandbox.dhan.co/v2/orders/112111182198")]
    [InlineData("DELETE", "https://sandbox.dhan.co/v2/orders/112111182198")]
    [InlineData("GET", "https://sandbox.dhan.co/v2/orders/112111182198")]
    [InlineData("GET", "https://sandbox.dhan.co/v2/orders/external/XD2610260000A1E1")]
    [InlineData("GET", "https://sandbox.dhan.co/v2/orders")]
    [InlineData("GET", "https://sandbox.dhan.co/v2/trades")]
    [InlineData("GET", "https://sandbox.dhan.co/v2/positions")]
    [InlineData("GET", "https://sandbox.dhan.co/v2/fundlimit")]
    [InlineData("GET", "https://sandbox.dhan.co/v2/profile")] // P12-11 readiness, ADR 0170
    [InlineData("GET", "https://sandbox.dhan.co/v2/ip/getIP")] // P12-11 readiness, ADR 0170
    public void The_rows_of_this_slice_pass_in_the_sandbox(string method, string url) =>
        ExecutionEndpoints.IsAllowed(ExecEnvironment.Sandbox, new HttpMethod(method), U(url)).ShouldBeTrue();

    [Theory]
    [InlineData("POST", "https://sandbox.dhan.co/v2/orders/slicing")]
    [InlineData("PUT", "https://sandbox.dhan.co/v2/super/orders/1")]
    [InlineData("DELETE", "https://sandbox.dhan.co/v2/super/orders/1")]
    [InlineData("PUT", "https://sandbox.dhan.co/v2/ip/setIP")]
    [InlineData("PUT", "https://sandbox.dhan.co/v2/ip/modifyIP")]
    [InlineData("POST", "https://sandbox.dhan.co/v2/RenewToken")]
    [InlineData("POST", "https://sandbox.dhan.co/v2/positions/convert")]
    [InlineData("POST", "https://sandbox.dhan.co/v2/killswitch")]
    [InlineData("POST", "https://sandbox.dhan.co/v2/forever/orders")]
    [InlineData("POST", "https://sandbox.dhan.co/v2/orders/123")]
    [InlineData("POST", "https://sandbox.dhan.co/v2/trades")]
    [InlineData("PUT", "https://sandbox.dhan.co/v2/positions")]
    [InlineData("DELETE", "https://sandbox.dhan.co/v2/fundlimit")]
    [InlineData("GET", "https://sandbox.dhan.co/v2/trades/123")]
    [InlineData("GET", "https://sandbox.dhan.co/v2/positions?x=1")]
    [InlineData("POST", "https://sandbox.dhan.co/v2/profile")]
    [InlineData("PUT", "https://sandbox.dhan.co/v2/profile")]
    [InlineData("GET", "https://sandbox.dhan.co/v2/profile/1100003626")]
    [InlineData("GET", "https://sandbox.dhan.co/v2/profile?dhanClientId=1")]
    [InlineData("POST", "https://sandbox.dhan.co/v2/ip/getIP")]
    [InlineData("POST", "https://sandbox.dhan.co/v2/ip/setIP")]
    [InlineData("GET", "https://sandbox.dhan.co/v2/ip/setIP")]
    [InlineData("GET", "https://sandbox.dhan.co/v2/ip/modifyIP")]
    [InlineData("GET", "https://sandbox.dhan.co/v2/ip/getip")]
    [InlineData("GET", "https://sandbox.dhan.co/v2/ip/getIP/1")]
    [InlineData("GET", "https://sandbox.dhan.co/v2/ip/getIP?x=1")]
    [InlineData("GET", "https://sandbox.dhan.co/v2/ip")]
    [InlineData("GET", "https://sandbox.dhan.co/v2/RenewToken")]
    [InlineData("GET", "https://evil.test/v2/profile")]
    [InlineData("GET", "http://sandbox.dhan.co/v2/ip/getIP")]
    [InlineData("GET", "https://sandbox.dhan.co/v2/killswitch")]
    [InlineData("DELETE", "https://sandbox.dhan.co/v2/orders")]
    [InlineData("PATCH", "https://sandbox.dhan.co/v2/orders/123")]
    [InlineData("POST", "http://sandbox.dhan.co/v2/orders")]
    [InlineData("POST", "https://sandbox.dhan.co:8443/v2/orders")]
    [InlineData("POST", "https://user@sandbox.dhan.co/v2/orders")]
    [InlineData("POST", "https://sandbox.dhan.co/v2/orders?x=1")]
    [InlineData("POST", "https://sandbox.dhan.co/v2/orders#f")]
    [InlineData("POST", "https://sandbox.dhan.co.evil.test/v2/orders")]
    [InlineData("POST", "https://evil.test/v2/orders")]
    [InlineData("POST", "https://Sandbox.dhan.co/v2/Orders")]
    [InlineData("GET", "https://sandbox.dhan.co/v2/orders/a%2Fb")]
    [InlineData("GET", "https://sandbox.dhan.co/v2/orders/external/a/b")]
    [InlineData("GET", "https://sandbox.dhan.co/v2/orders/")]
    [InlineData("GET", "https://sandbox.dhan.co/v2/orders/..%2fprofile")]
    public void Everything_else_is_refused(string method, string url) =>
        ExecutionEndpoints.IsAllowed(ExecEnvironment.Sandbox, new HttpMethod(method), U(url)).ShouldBeFalse();

    [Fact]
    public void The_environment_none_passes_nothing()
    {
        foreach (var call in Enum.GetValues<DhanCall>())
        {
            ExecutionEndpoints.Build(ExecEnvironment.None, call).ShouldBeNull();
            ExecutionEndpoints.Build(ExecEnvironment.None, call, "1").ShouldBeNull();
        }

        ExecutionEndpoints.IsAllowed(ExecEnvironment.None, HttpMethod.Post, U("https://sandbox.dhan.co/v2/orders")).ShouldBeFalse();
        ExecutionEndpoints.IsAllowed(ExecEnvironment.None, HttpMethod.Get, U("https://api.dhan.co/v2/profile")).ShouldBeFalse();
    }

    // ADR 0172: the live environment has exactly the two readiness GETs on the live host, nothing else.
    [Theory]
    [InlineData("GET", "https://api.dhan.co/v2/profile")]
    [InlineData("GET", "https://api.dhan.co/v2/ip/getIP")]
    public void Live_passes_only_the_two_readiness_gets(string method, string url) =>
        ExecutionEndpoints.IsAllowed(ExecEnvironment.Live, new HttpMethod(method), U(url)).ShouldBeTrue();

    [Theory]
    [InlineData("POST", "https://api.dhan.co/v2/orders")]
    [InlineData("GET", "https://api.dhan.co/v2/orders")]
    [InlineData("GET", "https://api.dhan.co/v2/orders/112111182198")]
    [InlineData("PUT", "https://api.dhan.co/v2/orders/112111182198")]
    [InlineData("DELETE", "https://api.dhan.co/v2/orders/112111182198")]
    [InlineData("GET", "https://api.dhan.co/v2/orders/external/XD2610260000A1E1")]
    [InlineData("POST", "https://api.dhan.co/v2/super/orders")]
    [InlineData("POST", "https://api.dhan.co/v2/forever/orders")]
    [InlineData("POST", "https://api.dhan.co/v2/orders/slicing")]
    [InlineData("GET", "https://api.dhan.co/v2/trades")]
    [InlineData("GET", "https://api.dhan.co/v2/positions")]
    [InlineData("POST", "https://api.dhan.co/v2/positions/convert")]
    [InlineData("GET", "https://api.dhan.co/v2/fundlimit")]
    [InlineData("GET", "https://api.dhan.co/v2/holdings")]
    [InlineData("POST", "https://api.dhan.co/v2/killswitch")]
    [InlineData("GET", "https://api.dhan.co/v2/killswitch")]
    [InlineData("POST", "https://api.dhan.co/v2/ip/setIP")]
    [InlineData("PUT", "https://api.dhan.co/v2/ip/setIP")]
    [InlineData("GET", "https://api.dhan.co/v2/ip/setIP")]
    [InlineData("PUT", "https://api.dhan.co/v2/ip/modifyIP")]
    [InlineData("GET", "https://api.dhan.co/v2/ip/modifyIP")]
    [InlineData("POST", "https://api.dhan.co/v2/RenewToken")]
    [InlineData("GET", "https://api.dhan.co/v2/RenewToken")]
    [InlineData("POST", "https://api.dhan.co/v2/profile")]
    [InlineData("PUT", "https://api.dhan.co/v2/profile")]
    [InlineData("DELETE", "https://api.dhan.co/v2/profile")]
    [InlineData("POST", "https://api.dhan.co/v2/ip/getIP")]
    [InlineData("HEAD", "https://api.dhan.co/v2/profile")]
    [InlineData("GET", "https://api.dhan.co/v2/Profile")]
    [InlineData("GET", "https://api.dhan.co/v2/ip/getip")]
    [InlineData("GET", "https://api.dhan.co/V2/profile")]
    [InlineData("GET", "https://api.dhan.co/v2/profile/")]
    [InlineData("GET", "https://api.dhan.co/v2/profile/1100003626")]
    [InlineData("GET", "https://api.dhan.co/v2/ip/getIP/1")]
    [InlineData("GET", "https://api.dhan.co/v2/profile?dhanClientId=1")]
    [InlineData("GET", "https://api.dhan.co/v2/ip/getIP?x=1")]
    [InlineData("GET", "https://api.dhan.co/v2/profile#f")]
    [InlineData("GET", "https://api.dhan.co/v2/ip")]
    [InlineData("GET", "https://api.dhan.co/v2/profile/..%2forders")]
    [InlineData("GET", "http://api.dhan.co/v2/profile")]
    [InlineData("GET", "http://api.dhan.co/v2/ip/getIP")]
    [InlineData("GET", "https://api.dhan.co:8443/v2/profile")]
    [InlineData("GET", "https://api.dhan.co:80/v2/profile")]
    [InlineData("GET", "https://user@api.dhan.co/v2/profile")]
    [InlineData("GET", "https://sandbox.dhan.co/v2/profile")]
    [InlineData("GET", "https://api-order-update.dhan.co/v2/profile")]
    [InlineData("GET", "https://auth.dhan.co/v2/profile")]
    [InlineData("GET", "https://api.dhan.co.evil.test/v2/profile")]
    [InlineData("GET", "https://evil.test/v2/profile")]
    [InlineData("GET", "https://dhan.co/v2/profile")]
    public void Live_refuses_every_other_request(string method, string url) =>
        ExecutionEndpoints.IsAllowed(ExecEnvironment.Live, new HttpMethod(method), U(url)).ShouldBeFalse();

    [Fact]
    public void Live_builds_only_the_two_readiness_reads_without_an_id()
    {
        foreach (var call in Enum.GetValues<DhanCall>())
        {
            var built = ExecutionEndpoints.Build(ExecEnvironment.Live, call);
            if (call is DhanCall.ProfileGet or DhanCall.StaticIpGet)
            {
                ExecutionEndpoints.IsAllowed(ExecEnvironment.Live, ExecutionEndpoints.MethodOf(call), built).ShouldBeTrue();
            }
            else
            {
                built.ShouldBeNull(call.ToString());
            }

            ExecutionEndpoints.Build(ExecEnvironment.Live, call, "1").ShouldBeNull(call.ToString());
        }

        ExecutionEndpoints.Build(ExecEnvironment.Live, DhanCall.ProfileGet)!.ToString().ShouldBe("https://api.dhan.co/v2/profile");
        ExecutionEndpoints.Build(ExecEnvironment.Live, DhanCall.StaticIpGet)!.ToString().ShouldBe("https://api.dhan.co/v2/ip/getIP");
        ExecutionEndpoints.LiveHost.ShouldBe("api.dhan.co");
    }

    [Fact]
    public void The_sandbox_does_not_pass_the_live_host()
    {
        ExecutionEndpoints.IsAllowed(ExecEnvironment.Sandbox, HttpMethod.Get, U("https://api.dhan.co/v2/profile")).ShouldBeFalse();
        ExecutionEndpoints.IsAllowed(ExecEnvironment.Sandbox, HttpMethod.Post, U("https://api.dhan.co/v2/orders")).ShouldBeFalse();
    }

    // End to end through the guard with a stub socket: of every request below, only the two live GETs reach the inner handler.
    [Fact]
    public async Task Through_the_live_guard_only_the_two_readiness_gets_reach_the_socket()
    {
        var inner = new CountingHandler();
        using var live = new HttpClient(new GuardHandler(ExecEnvironment.Live, inner));
        (string Method, string Url)[] refused =
        [
            ("POST", "https://api.dhan.co/v2/orders"), ("GET", "https://api.dhan.co/v2/orders"), ("DELETE", "https://api.dhan.co/v2/orders/1"), ("PUT", "https://api.dhan.co/v2/orders/1"),
            ("POST", "https://api.dhan.co/v2/super/orders"), ("POST", "https://api.dhan.co/v2/killswitch"), ("PUT", "https://api.dhan.co/v2/ip/setIP"), ("PUT", "https://api.dhan.co/v2/ip/modifyIP"),
            ("POST", "https://api.dhan.co/v2/RenewToken"), ("GET", "https://api.dhan.co/v2/fundlimit"), ("GET", "https://api.dhan.co/v2/positions"), ("GET", "https://api.dhan.co/v2/trades"),
            ("GET", "http://api.dhan.co/v2/profile"), ("GET", "https://api.dhan.co/v2/Profile"), ("GET", "https://evil.test/v2/profile"), ("GET", "https://sandbox.dhan.co/v2/profile"),
            ("POST", "https://api.dhan.co/v2/profile"), ("GET", "https://api.dhan.co/v2/profile?x=1"),
        ];

        foreach (var (method, url) in refused)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), url);
            await Should.ThrowAsync<HttpRequestException>(() => live.SendAsync(request, TestContext.Current.CancellationToken), $"{method} {url}");
        }

        inner.Calls.ShouldBe(0);
        (await live.GetAsync("https://api.dhan.co/v2/profile", TestContext.Current.CancellationToken)).IsSuccessStatusCode.ShouldBeTrue();
        (await live.GetAsync("https://api.dhan.co/v2/ip/getIP", TestContext.Current.CancellationToken)).IsSuccessStatusCode.ShouldBeTrue();
        inner.Calls.ShouldBe(2);
    }

    [Fact]
    public void Build_pins_the_host_and_rejects_a_bad_id_or_a_missing_id()
    {
        ExecutionEndpoints.Build(ExecEnvironment.Sandbox, DhanCall.OrdersPost)!.ToString().ShouldBe("https://sandbox.dhan.co/v2/orders");
        ExecutionEndpoints.Build(ExecEnvironment.Sandbox, DhanCall.OrderDelete, "1")!.ToString().ShouldBe("https://sandbox.dhan.co/v2/orders/1");
        ExecutionEndpoints.Build(ExecEnvironment.Sandbox, DhanCall.OrderDelete, "1/../x").ShouldBeNull();
        ExecutionEndpoints.Build(ExecEnvironment.Sandbox, DhanCall.OrderDelete).ShouldBeNull();
        ExecutionEndpoints.Build(ExecEnvironment.Sandbox, DhanCall.OrdersPost, "1").ShouldBeNull();
    }

    [Fact]
    public async Task The_guard_handler_never_lets_a_refused_request_reach_the_inner_handler()
    {
        var inner = new CountingHandler();
        using var client = new HttpClient(new GuardHandler(ExecEnvironment.Sandbox, inner));

        await Should.ThrowAsync<HttpRequestException>(() => client.PostAsync("https://api.example.test/v2/orders", new StringContent("{}"), TestContext.Current.CancellationToken));
        await Should.ThrowAsync<HttpRequestException>(() => client.GetAsync("https://sandbox.dhan.co/v2/killswitch", TestContext.Current.CancellationToken));
        inner.Calls.ShouldBe(0);
        (await client.GetAsync("https://sandbox.dhan.co/v2/orders/1", TestContext.Current.CancellationToken)).IsSuccessStatusCode.ShouldBeTrue();
        inner.Calls.ShouldBe(1);

        using var live = new HttpClient(new GuardHandler(ExecEnvironment.Live, inner));
        await Should.ThrowAsync<HttpRequestException>(() => live.GetAsync("https://sandbox.dhan.co/v2/orders/1", TestContext.Current.CancellationToken));
        await Should.ThrowAsync<HttpRequestException>(() => live.GetAsync("https://sandbox.dhan.co/v2/profile", TestContext.Current.CancellationToken));
        inner.Calls.ShouldBe(1);
    }

    [Fact]
    public void The_readiness_reads_build_without_an_id_and_their_audit_templates_carry_no_id_or_query()
    {
        ExecutionEndpoints.Build(ExecEnvironment.Sandbox, DhanCall.ProfileGet)!.ToString().ShouldBe("https://sandbox.dhan.co/v2/profile");
        ExecutionEndpoints.Build(ExecEnvironment.Sandbox, DhanCall.StaticIpGet)!.ToString().ShouldBe("https://sandbox.dhan.co/v2/ip/getIP");
        ExecutionEndpoints.Build(ExecEnvironment.Sandbox, DhanCall.ProfileGet, "1").ShouldBeNull();
        ExecutionEndpoints.Build(ExecEnvironment.Live, DhanCall.ProfileGet, "1").ShouldBeNull();
        ExecutionEndpoints.Build(ExecEnvironment.None, DhanCall.StaticIpGet).ShouldBeNull();
        ExecutionEndpoints.MethodOf(DhanCall.ProfileGet).ShouldBe(HttpMethod.Get);
        ExecutionEndpoints.MethodOf(DhanCall.StaticIpGet).ShouldBe(HttpMethod.Get);
        ExecutionEndpoints.TemplateOf(DhanCall.ProfileGet).ShouldBe("/v2/profile");
        ExecutionEndpoints.TemplateOf(DhanCall.StaticIpGet).ShouldBe("/v2/ip/getIP");
        ExecutionEndpoints.TemplateOf(DhanCall.OrderByCorrelationGet).ShouldBe("/v2/orders/external/{id}");
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{}") });
        }
    }
}
