using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Filings;
using XDrishti.Domain.Common;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Tests.Filings;

public sealed class FilingIngestServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void It_is_paused_by_default_daily_at_17_05_on_trading_days_with_one_retry_no_catch_up_and_a_failure_notice()
    {
        var settings = FilingIngestService.Definition.Defaults;

        FilingIngestService.Definition.Id.ShouldBe("filings-ingest");
        settings.Enabled.ShouldBeFalse();
        settings.Schedule.ShouldBe(Schedule.Daily(new TimeOnly(17, 5)));
        settings.Schedule.TradingDaysOnly.ShouldBeTrue();
        (settings.Retries, settings.CatchUp, settings.NotifyOnFailure).ShouldBe((1, false, true));
        FilingIngestService.Definition.RunsAs.ShouldBe("xd-api");
    }

    private static FilingIngestService ServiceReturning(FilingIngestSummary summary, out ICommandHandler<IngestFilingsCommand, FilingIngestSummary> handler)
    {
        handler = Substitute.For<ICommandHandler<IngestFilingsCommand, FilingIngestSummary>>();
        handler.HandleAsync(Arg.Any<IngestFilingsCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success(summary));
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(ICommandHandler<IngestFilingsCommand, FilingIngestSummary>)).Returns(handler);
        Loaders(provider, out _, out _);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(provider);
        var scopes = Substitute.For<IServiceScopeFactory>();
        scopes.CreateScope().Returns(scope);
        return new FilingIngestService(scopes, NullLogger<FilingIngestService>.Instance);
    }

    private static RowLoadSummary Loaded(string kind, int failures = 0) => new(kind, 1, 2, 0, 0, 0, 0, 0, new Dictionary<string, int>(), failures, []);

    private static void Loaders(IServiceProvider provider, out ICommandHandler<LoadCalendarCommand, RowLoadSummary> calendar, out ICommandHandler<LoadShareholdingCommand, RowLoadSummary> shareholding, int failures = 0)
    {
        calendar = Substitute.For<ICommandHandler<LoadCalendarCommand, RowLoadSummary>>();
        calendar.HandleAsync(Arg.Any<LoadCalendarCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Loaded("calendar", failures)));
        shareholding = Substitute.For<ICommandHandler<LoadShareholdingCommand, RowLoadSummary>>();
        shareholding.HandleAsync(Arg.Any<LoadShareholdingCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Loaded("shareholding")));
        provider.GetService(typeof(ICommandHandler<LoadCalendarCommand, RowLoadSummary>)).Returns(calendar);
        provider.GetService(typeof(ICommandHandler<LoadShareholdingCommand, RowLoadSummary>)).Returns(shareholding);
    }

    private static ServiceRunContext Context(ServiceRunTrigger trigger) => new(Guid.NewGuid(), trigger, 1, "system", new Dictionary<string, System.Text.Json.JsonElement>(), (_, _) => ValueTask.CompletedTask);

    private static FilingIngestSummary Summary(string? stopped = null, int failures = 0, params string[] problems) =>
        new(1, 1, 0, 0, 0, 0, 0, 0, new Dictionary<string, int>(), problems, failures, stopped);

    [Theory]
    [InlineData(ServiceRunTrigger.Manual, true)]
    [InlineData(ServiceRunTrigger.Scheduled, false)]
    public async Task A_run_carries_out_the_ingest_and_reports_the_counts_line(ServiceRunTrigger trigger, bool manual)
    {
        var service = ServiceReturning(Summary(), out var handler);

        var outcome = await service.RunAsync(Context(trigger), Ct);

        outcome.Status.ShouldBe(ServiceRunStatus.Succeeded);
        await handler.Received(1).HandleAsync(Arg.Is<IngestFilingsCommand>(c => c.Manual == manual), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_disk_stop_a_failed_file_or_an_inbox_problem_ends_the_run_failed()
    {
        (await ServiceReturning(Summary("disk_free"), out _).RunAsync(Context(ServiceRunTrigger.Scheduled), Ct)).Status.ShouldBe(ServiceRunStatus.Failed);
        (await ServiceReturning(Summary(failures: 1), out _).RunAsync(Context(ServiceRunTrigger.Scheduled), Ct)).Status.ShouldBe(ServiceRunStatus.Failed);
        (await ServiceReturning(Summary(problems: "inbox_permissions"), out _).RunAsync(Context(ServiceRunTrigger.Scheduled), Ct)).Status.ShouldBe(ServiceRunStatus.Failed);
    }

    [Fact]
    public async Task A_run_also_loads_the_calendar_and_the_shareholding_csv_files_and_puts_their_counts_in_the_line()
    {
        var provider = Substitute.For<IServiceProvider>();
        var ingest = Substitute.For<ICommandHandler<IngestFilingsCommand, FilingIngestSummary>>();
        ingest.HandleAsync(Arg.Any<IngestFilingsCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Summary()));
        provider.GetService(typeof(ICommandHandler<IngestFilingsCommand, FilingIngestSummary>)).Returns(ingest);
        Loaders(provider, out var calendar, out var shareholding);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(provider);
        var scopes = Substitute.For<IServiceScopeFactory>();
        scopes.CreateScope().Returns(scope);

        var outcome = await new FilingIngestService(scopes, NullLogger<FilingIngestService>.Instance).RunAsync(Context(ServiceRunTrigger.Scheduled), Ct);

        outcome.Status.ShouldBe(ServiceRunStatus.Succeeded);
        outcome.Message!.ShouldContain("calendar: files 1, loaded 2");
        outcome.Message!.ShouldContain("shareholding: files 1, loaded 2");
        await calendar.Received(1).HandleAsync(Arg.Any<LoadCalendarCommand>(), Arg.Any<CancellationToken>());
        await shareholding.Received(1).HandleAsync(Arg.Any<LoadShareholdingCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_loader_that_fails_ends_the_run_failed_after_both_loaders_ran()
    {
        var provider = Substitute.For<IServiceProvider>();
        var ingest = Substitute.For<ICommandHandler<IngestFilingsCommand, FilingIngestSummary>>();
        ingest.HandleAsync(Arg.Any<IngestFilingsCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Summary()));
        provider.GetService(typeof(ICommandHandler<IngestFilingsCommand, FilingIngestSummary>)).Returns(ingest);
        Loaders(provider, out _, out var shareholding, failures: 1);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(provider);
        var scopes = Substitute.For<IServiceScopeFactory>();
        scopes.CreateScope().Returns(scope);

        var outcome = await new FilingIngestService(scopes, NullLogger<FilingIngestService>.Instance).RunAsync(Context(ServiceRunTrigger.Scheduled), Ct);

        outcome.Status.ShouldBe(ServiceRunStatus.Failed);
        await shareholding.Received(1).HandleAsync(Arg.Any<LoadShareholdingCommand>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_loader_that_throws_marks_the_run_failed_by_type_and_the_other_loader_still_runs(bool calendarThrows)
    {
        var provider = Substitute.For<IServiceProvider>();
        var ingest = Substitute.For<ICommandHandler<IngestFilingsCommand, FilingIngestSummary>>();
        ingest.HandleAsync(Arg.Any<IngestFilingsCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Summary()));
        provider.GetService(typeof(ICommandHandler<IngestFilingsCommand, FilingIngestSummary>)).Returns(ingest);
        Loaders(provider, out var calendar, out var shareholding);
        if (calendarThrows)
        {
            provider.GetService(typeof(ICommandHandler<LoadCalendarCommand, RowLoadSummary>)).Returns(new ThrowingHandler<LoadCalendarCommand>());
        }
        else
        {
            provider.GetService(typeof(ICommandHandler<LoadShareholdingCommand, RowLoadSummary>)).Returns(new ThrowingHandler<LoadShareholdingCommand>());
        }

        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(provider);
        var scopes = Substitute.For<IServiceScopeFactory>();
        scopes.CreateScope().Returns(scope);

        var outcome = await new FilingIngestService(scopes, NullLogger<FilingIngestService>.Instance).RunAsync(Context(ServiceRunTrigger.Scheduled), Ct);

        outcome.Status.ShouldBe(ServiceRunStatus.Failed);
        outcome.Message!.ShouldContain("loader failed (InvalidOperationException)");
        outcome.Message!.ShouldNotContain("secret detail");
        if (calendarThrows)
        {
            await shareholding.Received(1).HandleAsync(Arg.Any<LoadShareholdingCommand>(), Arg.Any<CancellationToken>());
        }
        else
        {
            await calendar.Received(1).HandleAsync(Arg.Any<LoadCalendarCommand>(), Arg.Any<CancellationToken>());
        }
    }

    private sealed class ThrowingHandler<TCommand> : ICommandHandler<TCommand, RowLoadSummary>
        where TCommand : ICommand
    {
        public ValueTask<Result<RowLoadSummary>> HandleAsync(TCommand command, CancellationToken cancellationToken) => throw new InvalidOperationException("secret detail");
    }
}
