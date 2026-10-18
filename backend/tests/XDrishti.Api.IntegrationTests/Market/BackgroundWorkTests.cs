using System.Net;
using System.Net.Http.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.Platform;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Refresh;
using XDrishti.Application.Platform.Services;

namespace XDrishti.Api.IntegrationTests.Market;

/// <summary>The hosted services of the API: the worker that carries out registered refreshes, the scheduler that starts the daily refresh (the instrument master service, ADR 0018) and the option checks at start.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class BackgroundWorkTests(ApiFactory factory) : MarketTestBase(factory)
{
    [Fact]
    public async Task The_background_worker_carries_out_a_registered_refresh_without_any_help()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var worker = Factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            // The real worker reads a real channel; the handler is wrapped only to tell the test when it has finished.
            services.AddSingleton<IRefreshJobChannel, WorkerChannel>();
            var original = services.Single(d => d.ServiceType == typeof(ICommandHandler<ExecuteRefreshCommand>));
            services.Remove(original);
            services.AddScoped<ICommandHandler<ExecuteRefreshCommand>>(sp =>
                new Signalling((ICommandHandler<ExecuteRefreshCommand>)ActivatorUtilities.CreateInstance(sp, original.ImplementationType!), done));
        }));
        var client = await SignInAsync(worker);

        using var response = await client.PostAsync("/api/instruments/refresh", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var started = (await response.Content.ReadFromJsonAsync<RefreshStarted>(Ct))!;
        await done.Task.WaitAsync(Ct);

        using var statusResponse = await client.GetAsync("/api/instruments/refresh/status", Ct);
        var status = await statusResponse.ReadAsync<RefreshStatusView>(Ct);
        status.Current.ShouldBeNull();
        status.Last!.Id.ShouldBe(started.RefreshId);
        (status.Last.Status, status.Last.Added).ShouldBe(("succeeded", 60));
    }

    [Fact]
    public async Task The_scheduler_starts_with_the_API_seeds_the_service_from_the_configuration_and_catches_up_a_list_that_was_never_refreshed()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var scheduled = Factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Instruments:AutoRefresh:Enabled", "true").UseSetting("Instruments:AutoRefresh:TimeIst", "03:30");
            b.ConfigureTestServices(services =>
            {
                services.AddHostedService<ServiceSchedulerHost>(); // the suite switches the loop off; this test is about it
                Wrap<StartRefreshCommand, RefreshStarted>(services, started);
            });
        });
        using var client = scheduled.CreateClient(); // starts the host and its hosted services
        await started.Task.WaitAsync(Ct);

        Factory.Logs.All.ShouldContain(l => l.Contains("service.catch_up service=\"instrument-master\"", StringComparison.Ordinal));
        var history = await Factory.WithDbAsync(async db => await db.RefreshRuns.AsNoTracking().ToListAsync(Ct));
        var run = history.ShouldHaveSingleItem();
        (run.Trigger, run.RequestedBy, run.Force).ShouldBe((XDrishti.Domain.Market.RefreshTrigger.Daily, "system:daily-refresh", false));
        Factory.RefreshJobs.PendingCount.ShouldBe(1);
        var row = await Factory.WithDbAsync(async db => await db.ServiceSchedules.AsNoTracking().SingleAsync(x => x.Id == "instrument-master", Ct));
        (row.Enabled, row.Schedule.StartTime, row.UpdatedBy).ShouldBe((true, new TimeOnly(3, 30), "system:first-start"));
    }

    [Fact]
    public async Task The_service_stays_off_when_the_configuration_seeds_it_switched_off()
    {
        await using var off = Factory.WithWebHostBuilder(b => b.UseSetting("Instruments:AutoRefresh:Enabled", "false"));
        var client = await SignInAsync(off);
        await off.Services.GetRequiredService<ServiceScheduler>().StartUpAsync(Ct); // what the hosted scheduler does at start, called here so the seeding is finished before the request

        using var response = await client.GetAsync("/api/services/instrument-master", Ct);
        var service = await response.ReadAsync<XDrishti.Application.Platform.Services.ServiceView>(Ct);

        (service.Enabled, service.Status, service.NextRun).ShouldBe((false, "paused", null));
        var settings = off.Services.GetRequiredService<IInstrumentRefreshSettings>();
        settings.AutoRefreshEnabled.ShouldBeFalse();
        (await Factory.WithDbAsync(async db => await db.RefreshRuns.CountAsync(Ct))).ShouldBe(0);
    }

    [Fact]
    public async Task Starting_the_API_ends_a_refresh_that_an_earlier_process_left_running()
    {
        await Factory.WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO market.master_refreshes (id, \"trigger\", \"force\", requested_by, started_at, status, phase, rows_read, added, updated, retired, revived, unchanged, skipped_by_scope, collisions, sector_matched, sector_unmatched, fno_underlyings, total_active, kept_by_type, warnings, rules_version, sectors_applied) "
                + "VALUES (gen_random_uuid(), 'manual', false, 'x', {0}, 'running', 'parsing', 10, 0,0,0,0,0,0,0,0,0,0,0, '{{}}'::jsonb, '[]'::jsonb, 1, false)",
                DateTimeOffset.UtcNow.AddMinutes(-5)); // real time: older than this test process, which started before
            return 0;
        });
        var recovered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var restarted = Factory.WithWebHostBuilder(b => b.ConfigureTestServices(services => Wrap<RecoverInterruptedRefreshesCommand>(services, recovered)));
        using var client = restarted.CreateClient();
        await recovered.Task.WaitAsync(Ct);

        var run = await Factory.WithDbAsync(async db => await db.RefreshRuns.AsNoTracking().SingleAsync(Ct));
        (run.Status, run.Phase).ShouldBe((XDrishti.Domain.Market.RefreshStatus.Failed, null));
        run.Error!.ShouldContain("did not finish");
        run.FinishedAt.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("25:00")]
    [InlineData("8:45")]
    [InlineData("noon")]
    [InlineData("")]
    public void A_wrong_time_of_day_stops_the_API_at_start(string time)
    {
        using var wrong = Factory.WithWebHostBuilder(b => b.UseSetting("Instruments:AutoRefresh:TimeIst", time));

        Should.Throw<OptionsValidationException>(() => wrong.CreateClient());
    }

    [Fact]
    public async Task The_configured_time_and_switch_are_what_the_status_reports()
    {
        await using var configured = Factory.WithWebHostBuilder(b => b.UseSetting("Instruments:AutoRefresh:Enabled", "false").UseSetting("Instruments:AutoRefresh:TimeIst", "07:05"));
        var client = await SignInAsync(configured);

        using var response = await client.GetAsync("/api/instruments/refresh/status", Ct);
        var status = await response.ReadAsync<RefreshStatusView>(Ct);

        (status.AutoRefresh.Enabled, status.AutoRefresh.TimeIst, status.AutoRefresh.NextRunAt).ShouldBe((false, "07:05", null));
    }

    /// <summary>Wraps the registered handler of a command so the test is told when it has run.</summary>
    private static void Wrap<TCommand>(IServiceCollection services, TaskCompletionSource done)
        where TCommand : ICommand
    {
        var original = services.Single(d => d.ServiceType == typeof(ICommandHandler<TCommand>));
        services.Remove(original);
        services.AddScoped<ICommandHandler<TCommand>>(sp =>
            new SignallingCommand<TCommand>((ICommandHandler<TCommand>)ActivatorUtilities.CreateInstance(sp, original.ImplementationType!), done));
    }

    private static void Wrap<TCommand, TResult>(IServiceCollection services, TaskCompletionSource done)
        where TCommand : ICommand
    {
        var original = services.Single(d => d.ServiceType == typeof(ICommandHandler<TCommand, TResult>));
        services.Remove(original);
        services.AddScoped<ICommandHandler<TCommand, TResult>>(sp =>
            new SignallingCommand<TCommand, TResult>((ICommandHandler<TCommand, TResult>)ActivatorUtilities.CreateInstance(sp, original.ImplementationType!), done));
    }

    /// <summary>Signs in on a derived host (same database, so the same owner).</summary>
    private async Task<HttpClient> SignInAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "198.18.250.77");
        using var response = await client.LoginAsync(ApiFactory.OwnerEmail, Factory.OwnerPassword, Ct);
        response.EnsureSuccessStatusCode();
        var session = (await response.Content.ReadFromJsonAsync<XDrishti.Application.Auth.SessionInfo>(Ct))!;
        client.DefaultRequestHeaders.Add("Cookie", $"xd.sid={ApiFactory.SessionCookieValue(response)}");
        client.DefaultRequestHeaders.Add("X-CSRF-Token", session.CsrfToken);
        return client;
    }

    private sealed class WorkerChannel : IRefreshJobChannel
    {
        private readonly Channel<RefreshJob> _channel = Channel.CreateUnbounded<RefreshJob>();

        public void Enqueue(RefreshJob job) => _channel.Writer.TryWrite(job);

        public IAsyncEnumerable<RefreshJob> ReadAllAsync(CancellationToken cancellationToken) => _channel.Reader.ReadAllAsync(cancellationToken);
    }

    private sealed class SignallingCommand<TCommand>(ICommandHandler<TCommand> inner, TaskCompletionSource done) : ICommandHandler<TCommand>
        where TCommand : ICommand
    {
        public async ValueTask<XDrishti.Domain.Common.Result> HandleAsync(TCommand command, CancellationToken cancellationToken)
        {
            var result = await inner.HandleAsync(command, cancellationToken);
            done.TrySetResult();
            return result;
        }
    }

    private sealed class SignallingCommand<TCommand, TResult>(ICommandHandler<TCommand, TResult> inner, TaskCompletionSource done) : ICommandHandler<TCommand, TResult>
        where TCommand : ICommand
    {
        public async ValueTask<XDrishti.Domain.Common.Result<TResult>> HandleAsync(TCommand command, CancellationToken cancellationToken)
        {
            var result = await inner.HandleAsync(command, cancellationToken);
            done.TrySetResult();
            return result;
        }
    }

    private sealed class Signalling(ICommandHandler<ExecuteRefreshCommand> inner, TaskCompletionSource done) : ICommandHandler<ExecuteRefreshCommand>
    {
        public async ValueTask<XDrishti.Domain.Common.Result> HandleAsync(ExecuteRefreshCommand command, CancellationToken cancellationToken)
        {
            var result = await inner.HandleAsync(command, cancellationToken);
            done.TrySetResult();
            return result;
        }
    }
}
