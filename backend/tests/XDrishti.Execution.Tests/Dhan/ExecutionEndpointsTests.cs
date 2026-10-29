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
    [InlineData("GET", "https://sandbox.dhan.co/v2/profile")]
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

    [Theory]
    [InlineData(ExecEnvironment.None)]
    [InlineData(ExecEnvironment.Live)]
    public void Only_the_sandbox_environment_passes_anything(ExecEnvironment environment)
    {
        ExecutionEndpoints.IsAllowed(environment, HttpMethod.Post, U("https://sandbox.dhan.co/v2/orders")).ShouldBeFalse();
        ExecutionEndpoints.IsAllowed(environment, HttpMethod.Get, U("https://sandbox.dhan.co/v2/orders/1")).ShouldBeFalse();
        ExecutionEndpoints.Build(environment, DhanCall.OrdersPost).ShouldBeNull();
        ExecutionEndpoints.Build(environment, DhanCall.OrderByCorrelationGet, "XD2610260000A1E1").ShouldBeNull();
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
        await Should.ThrowAsync<HttpRequestException>(() => client.GetAsync("https://sandbox.dhan.co/v2/profile", TestContext.Current.CancellationToken));
        inner.Calls.ShouldBe(0);
        (await client.GetAsync("https://sandbox.dhan.co/v2/orders/1", TestContext.Current.CancellationToken)).IsSuccessStatusCode.ShouldBeTrue();
        inner.Calls.ShouldBe(1);

        using var live = new HttpClient(new GuardHandler(ExecEnvironment.Live, inner));
        await Should.ThrowAsync<HttpRequestException>(() => live.GetAsync("https://sandbox.dhan.co/v2/orders/1", TestContext.Current.CancellationToken));
        inner.Calls.ShouldBe(1);
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
