using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Market.History;
using XDrishti.Domain.Common;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Tests.Market.History;

/// <summary>CF-04 Part B criterion 1 for the service, and its declaration: paused, Convergent, off-hours.</summary>
public sealed class HistoryBackfillServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ServiceRunContext Context() => new(Guid.NewGuid(), ServiceRunTrigger.Scheduled, 1, "system:test", new Dictionary<string, JsonElement>(), (_, _) => ValueTask.CompletedTask);

    private static FakeTimeProvider At(int hourIst, int minute = 0) => new(new DateTimeOffset(2026, 11, 11, hourIst, minute, 0, TimeSpan.FromMinutes(330)));

    [Fact]
    public async Task With_the_switch_off_the_run_is_skipped_and_no_scope_is_ever_opened()
    {
        var scopes = Substitute.For<IServiceScopeFactory>();

        var outcome = await new HistoryBackfillService(scopes, new FakeBackfillSettings { Enabled = false }, At(22)).RunAsync(Context(), Ct);

        outcome.Status.ShouldBe(ServiceRunStatus.Skipped);
        outcome.Message.ShouldBe(HistoryBackfillService.DisabledReason);
        scopes.DidNotReceiveWithAnyArgs().CreateScope();
    }

    [Fact]
    public async Task Outside_the_run_window_the_run_is_skipped_without_a_scope()
    {
        var scopes = Substitute.For<IServiceScopeFactory>();

        var outcome = await new HistoryBackfillService(scopes, new FakeBackfillSettings(), At(11)).RunAsync(Context(), Ct);

        outcome.Status.ShouldBe(ServiceRunStatus.Skipped);
        outcome.Message.ShouldBe(HistoryBackfillService.OutsideWindowReason);
        scopes.DidNotReceiveWithAnyArgs().CreateScope();
    }

    [Fact]
    public async Task Inside_the_window_the_run_reports_the_summary_and_a_stopped_run_is_failed_but_failed_chunks_are_not()
    {
        var ok = new BackfillSummary(false, 1, 0, 0, 10, 9, 0, 1, 100, 0, 0, 10, 100, 10, 0.1, null);

        (await Run(ok)).Status.ShouldBe(ServiceRunStatus.Succeeded);
        (await Run(ok with { Stopped = "dhan_not_connected" })).Status.ShouldBe(ServiceRunStatus.Failed);
    }

    private static async Task<ServiceOutcome> Run(BackfillSummary summary)
    {
        var handler = Substitute.For<ICommandHandler<RunBackfillCommand, BackfillSummary>>();
        handler.HandleAsync(default!, Ct).ReturnsForAnyArgs(Result.Success(summary));
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(ICommandHandler<RunBackfillCommand, BackfillSummary>)).Returns(handler);
        var scope = Substitute.For<IServiceScope, IAsyncDisposable>();
        scope.ServiceProvider.Returns(provider);
        var scopes = Substitute.For<IServiceScopeFactory>();
        scopes.CreateScope().Returns(scope);

        return await new HistoryBackfillService(scopes, new FakeBackfillSettings(), At(22)).RunAsync(Context(), Ct);
    }

    [Theory]
    [InlineData(20, 0, true)]
    [InlineData(23, 59, true)]
    [InlineData(0, 0, true)]
    [InlineData(5, 59, true)]
    [InlineData(6, 0, false)]
    [InlineData(12, 0, false)]
    [InlineData(19, 59, false)]
    public void The_default_window_crosses_midnight_and_is_half_open(int hour, int minute, bool inside) =>
        HistoryBackfillService.InWindow(At(hour, minute).GetUtcNow(), new TimeOnly(20, 0), new TimeOnly(6, 0)).ShouldBe(inside);

    [Fact]
    public void The_service_starts_paused_is_convergent_and_runs_in_the_api_host()
    {
        HistoryBackfillService.Definition.Defaults.Enabled.ShouldBeFalse();
        HistoryBackfillService.Definition.Id.ShouldBe("history-backfill");
        HistoryBackfillService.Definition.RunsAs.ShouldBe("xd-api");
        HistoryBackfillService.Definition.Constraints.RecoveryClass.ShouldBe(RecoveryClass.Convergent);
    }
}
