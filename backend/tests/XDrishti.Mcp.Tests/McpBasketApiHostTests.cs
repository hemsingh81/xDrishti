using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Common;
using XDrishti.Mcp.Tests.Support;

namespace XDrishti.Mcp.Tests;

/// <summary>BK-12b: the real tool-server composition registers the two HTTP-backed handlers under the BK-07 interfaces, reads its settings from <c>Mcp:Api</c>, and refuses to start with a service token it shares with one of its own clients.</summary>
public sealed class McpBasketApiHostTests
{
    private static void Api(IConfigurationBuilder configuration, string token) =>
        configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Mcp:Api:BaseUrl"] = "http://xd-api:8080", ["Mcp:Api:Token"] = token, ["Mcp:Api:TimeoutSeconds"] = "5" });

    [Fact]
    public async Task The_host_resolves_the_two_handlers_of_the_basket_screen_as_http_backed_implementations_from_a_scope()
    {
        await using var host = await TestMcpHost.StartAsync(c => Api(c, "a-service-token-of-its-own-0123456789"));

        await using var scope = host.App.Services.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<IQueryHandler<GetBasketProfileQuery, Result<BasketProfileView>>>().GetType().Name.ShouldBe("ApiBasketProfileHandler");
        scope.ServiceProvider.GetRequiredService<IQueryHandler<GetBasketRankingQuery, Result<BasketRankingView>>>().GetType().Name.ShouldBe("ApiBasketRankingHandler");
    }

    [Fact]
    public async Task With_no_address_or_token_the_handlers_exist_and_answer_unavailable_without_any_call()
    {
        await using var host = await TestMcpHost.StartAsync();
        await using var scope = host.App.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IQueryHandler<GetBasketRankingQuery, Result<BasketRankingView>>>();

        var result = await handler.HandleAsync(new GetBasketRankingQuery($"{Guid.NewGuid()},{Guid.NewGuid()}", null, null, null), TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe("tool.unavailable");
        result.Error.Message.ShouldBe("basket reports are not reachable now");
    }

    [Fact]
    public async Task A_service_token_equal_to_a_client_token_of_the_server_stops_the_start()
    {
        var ex = await Should.ThrowAsync<Exception>(async () => await TestMcpHost.StartAsync(c => Api(c, TestMcpHost.ApiToken)));

        ex.Message.ShouldContain("Mcp:Api:Token equals a client token");
        ex.Message.ShouldNotContain(TestMcpHost.ApiToken);
    }

    [Fact]
    public async Task A_timeout_above_nine_seconds_stops_the_start_so_that_it_stays_inside_the_tool_timeout()
    {
        await Should.ThrowAsync<Exception>(async () => await TestMcpHost.StartAsync(c => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Mcp:Api:TimeoutSeconds"] = "10" })));
    }
}
