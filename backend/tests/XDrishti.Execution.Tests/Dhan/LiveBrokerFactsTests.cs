using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Domain.Accounts;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Dhan;
using XDrishti.Execution.Host;
using XDrishti.Execution.Persistence;

namespace XDrishti.Execution.Tests.Dhan;

/// <summary>
/// ADR 0171 step 6: the live readiness reads take the account's token only from the sealed hand-over, only with <c>Execution:ReadinessLiveEnabled</c> in live, and are still refused by the guard table (no live row, no egress: step 5 is open). Built, tested and
/// inert in live: no request can exist, the class holds no HTTP client and is not a credential source of the order client.
/// </summary>
public sealed class LiveBrokerFactsTests
{
    private static readonly Guid Account = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Tokens(string? token) : ILiveTokenSource
    {
        public List<Guid> Asked { get; } = [];

        public Task<SecretValue?> FindAsync(Guid accountId, CancellationToken cancellationToken)
        {
            Asked.Add(accountId);
            return Task.FromResult(token is not null && SecretValue.TryCreate(token, out var value, out _) ? value : null);
        }
    }

    [Fact]
    public async Task With_the_switch_off_both_reads_are_token_unavailable_and_no_token_is_looked_up()
    {
        var tokens = new Tokens("FAKE-LIVE-TOKEN");
        var facts = new LiveBrokerFacts(tokens, enabled: false);

        (await facts.GetProfileAsync(Account, Ct)).Reason.ShouldBe("token_unavailable");
        (await facts.GetStaticIpAsync(Account, Ct)).Reason.ShouldBe("token_unavailable");
        tokens.Asked.ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_a_hand_over_token_both_reads_are_token_unavailable()
    {
        var tokens = new Tokens(null);
        var facts = new LiveBrokerFacts(tokens, enabled: true);

        var profile = await facts.GetProfileAsync(Account, Ct);
        var ip = await facts.GetStaticIpAsync(Account, Ct);

        (profile.Readable, profile.Reason, ip.Readable, ip.Reason).ShouldBe((false, "token_unavailable", false, "token_unavailable"));
        tokens.Asked.ShouldBe([Account, Account]);
    }

    [Fact]
    public async Task With_a_token_the_live_guard_still_refuses_both_reads()
    {
        var facts = new LiveBrokerFacts(new Tokens("FAKE-LIVE-TOKEN"), enabled: true);

        var profile = await facts.GetProfileAsync(Account, Ct);
        var ip = await facts.GetStaticIpAsync(Account, Ct);

        (profile.Readable, profile.Reason, ip.Readable, ip.Reason).ShouldBe((false, "guard_refused", false, "guard_refused"));
    }

    [Fact]
    public void It_reaches_only_the_two_reads_holds_no_http_client_and_is_no_credential_or_order_port()
    {
        typeof(LiveBrokerFacts).GetInterfaces().ShouldBe([typeof(IBrokerFacts)]);
        typeof(LiveBrokerFacts).GetMethods().Where(m => m.DeclaringType == typeof(LiveBrokerFacts)).Select(m => m.Name).Order().ShouldBe(["GetProfileAsync", "GetStaticIpAsync"]);
        typeof(LiveBrokerFacts).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Select(f => f.FieldType)
            .ShouldNotContain(t => t.IsAssignableTo(typeof(HttpMessageInvoker)) || t == typeof(DhanOrderClient) || t.IsAssignableTo(typeof(IExecCredentials)));
        typeof(ILiveTokenSource).IsAssignableTo(typeof(IExecCredentials)).ShouldBeFalse();
        typeof(PgExecTokenHandover).IsAssignableTo(typeof(IExecCredentials)).ShouldBeFalse();
    }

    private static IServiceCollection Compose(Dictionary<string, string?> values)
    {
        values["Database:ConnectionString"] = "Host=localhost;Port=1;Database=xdrishti;Username=xd_exec"; // never opened: no key, so no lookup reaches the database
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new ServiceCollection().AddLogging().AddExecution(config).AddExecutionRecord(config);
    }

    [Theory]
    [InlineData("live", null)]
    [InlineData("live", "false")]
    [InlineData("live", "yes")]
    [InlineData("none", "true")]
    [InlineData(null, "true")]
    [InlineData("sandbox", "true")]
    public void Outside_live_with_the_switch_on_no_live_reads_and_no_token_source_are_composed(string? environment, string? readinessLive)
    {
        var services = Compose(new() { ["Execution:Environment"] = environment, ["Execution:ReadinessLiveEnabled"] = readinessLive });

        services.Where(d => d.ServiceType == typeof(IBrokerFacts)).ShouldHaveSingleItem().ImplementationType.ShouldBe(typeof(ClosedBrokerFacts));
        services.ShouldNotContain(d => d.ServiceType == typeof(ILiveTokenSource));
    }

    [Fact]
    public async Task In_live_with_the_switch_on_the_live_reads_are_composed_and_without_the_key_they_answer_token_unavailable()
    {
        var services = Compose(new() { ["Execution:Environment"] = "live", ["Execution:ReadinessLiveEnabled"] = "true", ["Execution:SendEnabled"] = "true" });

        await using var provider = services.BuildServiceProvider();
        var facts = provider.GetRequiredService<IBrokerFacts>();

        facts.ShouldBeOfType<LiveBrokerFacts>();
        (await facts.GetProfileAsync(Account, Ct)).Reason.ShouldBe("token_unavailable");
        (await facts.GetStaticIpAsync(Account, Ct)).Reason.ShouldBe("token_unavailable");
        services.ShouldNotContain(d => d.ServiceType == typeof(IExecCredentials));
        services.ShouldNotContain(d => d.ServiceType.Name == "LegSender");
    }
}
