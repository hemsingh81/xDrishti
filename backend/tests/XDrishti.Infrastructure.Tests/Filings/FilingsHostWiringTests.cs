using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Filings;

namespace XDrishti.Infrastructure.Tests.Filings;

/// <summary>Spec P15-01 criterion 7: under the real Infrastructure wiring, with every HTTP client's handler set to throw, resolving the filings source and listing a populated inbox makes zero calls.</summary>
public sealed class FilingsHostWiringTests
{
    [Fact]
    public async Task Listing_a_populated_inbox_through_the_real_wiring_makes_no_http_call()
    {
        using var inbox = new InboxFixture();
        inbox.Drop("a.xml", "<r/>", InboxFixture.Sidecar());
        var calls = 0;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Filings:InboxPath"] = inbox.Inbox }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddInfrastructure(configuration);
        services.ConfigureHttpClientDefaults(builder => builder.ConfigurePrimaryHttpMessageHandler(() => new ThrowingHandler(() => calls++)));
        await using var provider = services.BuildServiceProvider();

        var listing = await provider.GetRequiredService<IFilingsSource>().ListAsync(FilingQuery.All, TestContext.Current.CancellationToken);

        listing.Accepted.ShouldHaveSingleItem();
        calls.ShouldBe(0);
    }

    private sealed class ThrowingHandler(Action onCall) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            onCall();
            throw new InvalidOperationException("no network in the filings source");
        }
    }
}
