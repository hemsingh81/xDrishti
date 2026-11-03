#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using XDrishti.Application.Abstractions.Market.Eod;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Market.Eod;
using XDrishti.Domain.Common;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Tests.Market.Eod;

/// <summary>CF-04 criterion 1: the job makes no request with the switch off, and starts paused.</summary>
public sealed class HistoryEodServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ServiceRunContext Context() => new(Guid.NewGuid(), ServiceRunTrigger.Scheduled, 1, "system:test", new Dictionary<string, JsonElement>(), (_, _) => ValueTask.CompletedTask);

    [Fact]
    public async Task With_the_switch_off_the_run_is_skipped_and_the_loader_is_never_resolved()
    {
        var settings = Substitute.For<IEodSettings>();
        settings.Enabled.Returns(false);
        var scopes = Substitute.For<IServiceScopeFactory>();

        var outcome = await new HistoryEodService(scopes, settings).RunAsync(Context(), Ct);

        outcome.Status.ShouldBe(ServiceRunStatus.Skipped);
        outcome.Message.ShouldBe(HistoryEodService.DisabledReason);
        scopes.DidNotReceiveWithAnyArgs().CreateScope();
    }

    [Fact]
    public async Task With_the_switch_on_the_run_reports_the_summary_and_a_network_stop_is_a_failed_run()
    {
        var settings = Substitute.For<IEodSettings>();
        settings.Enabled.Returns(true);
        var handler = Substitute.For<ICommandHandler<LoadEodCommand, EodLoadSummary>>();
        handler.HandleAsync(default!, Ct).ReturnsForAnyArgs(Result.Success(new EodLoadSummary(1, 0, 0, 10, 0, 0, 2, "fetch_failed")));
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(ICommandHandler<LoadEodCommand, EodLoadSummary>)).Returns(handler);
        var scope = Substitute.For<IServiceScope, IAsyncDisposable>();
        scope.ServiceProvider.Returns(provider);
        var scopes = Substitute.For<IServiceScopeFactory>();
        scopes.CreateScope().Returns(scope);

        var outcome = await new HistoryEodService(scopes, settings).RunAsync(Context(), Ct);

        outcome.Status.ShouldBe(ServiceRunStatus.Failed);
    }

    [Fact]
    public void The_service_starts_paused_and_runs_in_the_api_host()
    {
        HistoryEodService.Definition.Defaults.Enabled.ShouldBeFalse();
        HistoryEodService.Definition.Id.ShouldBe("history-eod");
        HistoryEodService.Definition.RunsAs.ShouldBe("xd-api");
    }
}
