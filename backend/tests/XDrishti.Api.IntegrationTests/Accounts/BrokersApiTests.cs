using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Accounts.Connections;

namespace XDrishti.Api.IntegrationTests.Accounts;

/// <summary>The Dhan redirect URL, and the deployment settings behind the vault and the redirect URLs.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class BrokersApiTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_Dhan_redirect_URL_is_on_the_local_stack()
    {
        var client = (await factory.SignInAsync(Ct)).Client;

        var dhan = await (await client.GetAsync("/api/brokers/dhan", Ct)).ReadAsync<DhanRedirectView>(Ct);

        dhan.RedirectUrl.ShouldBe("http://localhost:8080/api/brokers/dhan/callback");
    }

    [Fact]
    public async Task The_public_base_URL_is_configurable()
    {
        await using var other = factory.WithWebHostBuilder(b => b.UseSetting("Brokers:PublicBaseUrl", "http://localhost:9090"));
        await using var scope = other.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<Application.Abstractions.Messaging.IQueryHandler<GetDhanRedirectQuery, DhanRedirectView>>();

        var dhan = await handler.HandleAsync(new GetDhanRedirectQuery(), Ct);

        dhan.RedirectUrl.ShouldBe("http://localhost:9090/api/brokers/dhan/callback");
    }

    [Theory]
    [InlineData("")]
    [InlineData("too-short-0123456789")]
    public void The_API_refuses_to_start_without_a_vault_key_of_at_least_32_characters(string key)
    {
        using var broken = factory.WithWebHostBuilder(b => b.UseSetting("BrokerVault:Key", key));

        var error = Should.Throw<OptionsValidationException>(() => broken.CreateClient());

        error.Message.ShouldContain("Key");
        if (key.Length > 0)
        {
            error.Message.ShouldNotContain(key);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("too-short-0123456789")]
    public void The_API_refuses_to_start_without_a_hand_over_key_of_at_least_32_characters(string key)
    {
        using var broken = factory.WithWebHostBuilder(b => b.UseSetting("ExecHandover:Key", key));

        var error = Should.Throw<OptionsValidationException>(() => broken.CreateClient());

        error.Message.ShouldContain("ExecHandover");
        if (key.Length > 0)
        {
            error.Message.ShouldNotContain(key);
        }
    }

    [Fact]
    public void The_API_refuses_to_start_with_an_invalid_public_base_URL()
    {
        using var broken = factory.WithWebHostBuilder(b => b.UseSetting("Brokers:PublicBaseUrl", "localhost:8080/app"));

        Should.Throw<OptionsValidationException>(() => broken.CreateClient());
    }
}
