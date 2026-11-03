using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.Cli;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Eod;
using XDrishti.Infrastructure.Market;

namespace XDrishti.Api.IntegrationTests.Market;

/// <summary>CF-04 Part A criterion 1 through the real wiring: the handlers resolved from the container make no request at all while the switch is off, and none either when it is on but the allow-list is empty. The verbs' command line is parsed as documented.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class DailyHistoryWiringTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private WebApplicationFactory<Program> Host(CountingHandler counter, bool enabled) =>
        factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Data:History:Nse:Enabled", enabled ? "true" : "false");
            b.ConfigureTestServices(services => services.AddHttpClient(MasterDataHttpClientNames.NseHistory).ConfigurePrimaryHttpMessageHandler(() => counter));
        });

    [Fact]
    public async Task With_the_switch_off_the_load_and_the_probe_make_zero_requests()
    {
        var counter = new CountingHandler();
        await using var host = Host(counter, enabled: false);
        using var scope = host.Services.CreateScope();

        var load = await scope.ServiceProvider.GetRequiredService<ICommandHandler<LoadEodCommand, EodLoadSummary>>().HandleAsync(new LoadEodCommand(new DateOnly(2024, 7, 5), new DateOnly(2024, 7, 8)), Ct);
        var probe = await scope.ServiceProvider.GetRequiredService<ICommandHandler<EodProbeCommand, EodProbeReport>>().HandleAsync(new EodProbeCommand([new DateOnly(2024, 7, 5)]), Ct);

        load.Error.Code.ShouldBe("history.disabled");
        probe.Error.Code.ShouldBe("history.disabled");
        counter.Requests.ShouldBe(0);
    }

    [Fact]
    public async Task With_the_switch_on_but_the_allow_list_empty_the_guard_refuses_before_anything_is_sent()
    {
        var counter = new CountingHandler();
        await using var host = Host(counter, enabled: true);
        using var scope = host.Services.CreateScope();

        var load = await scope.ServiceProvider.GetRequiredService<ICommandHandler<LoadEodCommand, EodLoadSummary>>().HandleAsync(new LoadEodCommand(new DateOnly(2024, 7, 5), new DateOnly(2024, 7, 8)), Ct);

        load.Error.Code.ShouldBe("history.not_allowed");
        counter.Requests.ShouldBe(0);
    }

    [Fact]
    public void The_load_verb_reads_its_range_and_the_recheck_flag()
    {
        var (parsed, errors) = DataEodCommand.Parse(["eod-load", "--from", "2024-07-01", "--to", "2024-07-08", "--recheck-absent"]);

        errors.ShouldBeEmpty();
        parsed.ShouldNotBeNull().ShouldSatisfyAllConditions(
            p => p.Verb.ShouldBe("eod-load"),
            p => p.From.ShouldBe(new DateOnly(2024, 7, 1)),
            p => p.To.ShouldBe(new DateOnly(2024, 7, 8)),
            p => p.Recheck.ShouldBeTrue());
        DataEodCommand.Parse(["eod-load"]).Command!.Recheck.ShouldBeFalse();
    }

    [Theory]
    [InlineData("eod-probe")]
    [InlineData("eod-load", "--from", "2024-13-45")]
    [InlineData("eod-load", "--dates", "2024-07-05")]
    [InlineData("eod-probe", "--recheck-absent", "--dates", "2024-07-05")]
    [InlineData("eod-universe")]
    [InlineData("eod-check", "--from", "2024-07-01")]
    [InlineData("eod-actions")]
    [InlineData("eod-bogus")]
    [InlineData("eod-load", "--to")]
    public void A_wrong_command_line_is_refused_with_a_reason(params string[] args) => DataEodCommand.Parse(args).Command.ShouldBeNull();

    [Fact]
    public void The_other_verbs_read_their_arguments()
    {
        DataEodCommand.Parse(["eod-probe", "--dates", "2021-06-01,2024-07-05"]).Command!.Dates.ShouldBe([new DateOnly(2021, 6, 1), new DateOnly(2024, 7, 5)]);
        DataEodCommand.Parse(["eod-universe", "--as-of", "2023-05-31"]).Command!.From.ShouldBe(new DateOnly(2023, 5, 31));
        DataEodCommand.Parse(["eod-check", "--from", "2024-07-01", "--to", "2024-07-05"]).Command!.To.ShouldBe(new DateOnly(2024, 7, 5));
        DataEodCommand.Parse(["eod-actions", "actions.csv"]).Command!.File.ShouldBe("actions.csv");
    }
}
