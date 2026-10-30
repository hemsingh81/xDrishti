using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Execution.Application;
using XDrishti.Execution.Application.Halts;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Application.Recon;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Recon;
using XDrishti.Execution.Host;

namespace XDrishti.Execution.Tests.Recon;

/// <summary>P12-06 changes nothing a host can send with: nothing it adds is composed, the closed defaults stay closed, no socket client exists and no code path in the new slice writes to the broker or edits the record.</summary>
public sealed class ReconCompositionTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "XDrishti.Execution"));

    private static IEnumerable<string> Sources() => Directory.GetFiles(Root, "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    [Theory]
    [InlineData(null, null)]
    [InlineData("sandbox", "true")]
    [InlineData("live", "true")]
    public void No_environment_composes_the_poll_the_recovery_the_break_service_the_stream_or_a_protection_source(string? environment, string? sendEnabled)
    {
        var values = new Dictionary<string, string?>();
        if (environment is not null)
        {
            values["Execution:Environment"] = environment;
        }

        if (sendEnabled is not null)
        {
            values["Execution:SendEnabled"] = sendEnabled;
        }

        using var provider = new ServiceCollection().AddLogging().AddExecution(new ConfigurationBuilder().AddInMemoryCollection(values).Build()).BuildServiceProvider();

        provider.GetService<PollService>().ShouldBeNull();
        provider.GetService<RecoveryService>().ShouldBeNull();
        provider.GetService<BreakService>().ShouldBeNull();
        provider.GetService<BreakAdoption>().ShouldBeNull();
        provider.GetService<UpdateApplier>().ShouldBeNull();
        provider.GetService<IProtectionSource>().ShouldBeNull();
        provider.GetService<IBrokerOrders>().ShouldBeNull();
        provider.GetService<IExitSendPolicy>()?.GetType().Name.ShouldBe("ClosedExitSendPolicy");
        provider.GetService<IHaltRequests>().ShouldBeNull();
        provider.GetService<IHaltSource>()!.GetType().Name.ShouldBe("ClosedHaltSource");
    }

    [Fact]
    public void The_record_registration_adds_the_stores_only_and_none_of_the_services_the_closed_defaults_stay()
    {
        var host = File.ReadAllText(Path.Combine(Root, "Host", "ExecServices.cs"));

        host.ShouldContain("services.AddSingleton<IReconStore, PgReconStore>()");
        host.ShouldContain("services.AddSingleton<IReconOurSource, PgReconOurSource>()");
        host.ShouldContain("services.AddSingleton<IFillWriter, PgFillWriter>()");
        foreach (var composed in new[] { "BreakService", "BreakAdoption", "RecoveryService", "PollService", "UpdateApplier", "PollProtectionSource", "OrderUpdateFeed", "IOrderUpdateTransport", "IProtectionSource", "IExitSendPolicy", "IHaltRequests", "IBrokerOrders" })
        {
            host.ShouldNotContain(composed);
        }

        host.ShouldContain("services.AddSingleton<IHaltSource, ClosedHaltSource>()");
    }

    [Fact]
    public void No_source_file_of_the_project_opens_a_socket_or_names_the_live_stream_host()
    {
        foreach (var file in Sources())
        {
            var text = File.ReadAllText(file);
            text.ShouldNotContain("ClientWebSocket", Case.Sensitive, file);
            text.ShouldNotContain("System.Net.WebSockets", Case.Sensitive, file);
            text.ShouldNotContain("TcpClient", Case.Sensitive, file);
            text.ShouldNotContain("api-order-update", Case.Sensitive, file);
        }
    }

    [Fact]
    public void The_new_services_hold_no_order_sender_and_reconciliation_has_no_write_port_on_our_books()
    {
        foreach (var type in new[] { typeof(RecoveryService), typeof(BreakService), typeof(PollService), typeof(UpdateApplier), typeof(PollProtectionSource), typeof(BreakAdoption) })
        {
            type.GetConstructors().Single().GetParameters().Select(p => p.ParameterType).ShouldNotContain(t => t == typeof(IBrokerOrders) || t == typeof(IBrokerStops) || t == typeof(LegSender), type.Name);
        }

        typeof(IReconOurSource).GetMethods().Select(m => m.Name).ShouldBe(["AccountsAsync", "LoadAsync"]);
        typeof(IReconStore).GetMethods().Select(m => m.Name).ShouldBe(["RecordRunAsync", "ResolvedKeysAsync", "OpenKeysAsync", "BreaksAsync", "RunsAsync"], "one insert and four reads: no update, delete or resolve");
    }

    [Fact]
    public void Every_new_setting_is_a_number_inside_a_range_and_none_enables_anything()
    {
        var added = new[] { "exec.poll_busy_s", "exec.poll_idle_s", "exec.recon_interval_s", "exec.recon_grace_s", "exec.stream_backoff_max_s", "exec.recovery_max_s" };

        foreach (var key in added)
        {
            var setting = ExecSettingRegistry.Find(key)!;
            setting.Kind.ShouldBe(ExecSettingKind.Number);
            ExecSettingRegistry.IsValid(setting, setting.Default).ShouldBeTrue(key);
            ExecSettingRegistry.IsValid(setting, "0").ShouldBeFalse(key);
        }

        ReconSettings.FromValues(new Dictionary<string, string>()).ShouldNotBeNull();
    }

    // ---- the real protection snapshot source
    private readonly FakeTimeProvider _clock = new(ReconFixtures.Now);

    private async Task<(PollProtectionSource Source, PollService Poll, StaticReconSource Ours)> SourceAsync(bool poll = true)
    {
        var broker = new FakeBroker(_clock);
        await broker.PlaceAsync(new BrokerLeg(ReconFixtures.Cid, LegKind.Entry, ExecSide.Buy, ReconFixtures.Security, 10, 100m), CancellationToken.None);
        broker.SetPrice(ReconFixtures.Security, 99m);
        var counters = new UpdateCounters();
        var status = new StreamStatus(_clock, counters);
        var legs = new LegBook();
        legs.Add(ReconFixtures.Cid, 361, LegKind.Entry, 10);
        var pollService = new PollService(broker, new UpdateApplier(legs, counters, _clock), legs, status, new ReadBudget(_clock), new InMemoryFillWriter(), _clock);
        var ours = new StaticReconSource();
        ours.Set(ReconFixtures.Account, new OurAccountView([ReconFixtures.Leg()], [ReconFixtures.Fill()], [new OurPositionView(ReconFixtures.Security, 10)], [new OurIntentView(361, ReconFixtures.Account, ExecState.Filled, ReconFixtures.Security, ExecSide.Buy, 10, 10, 95m, 100m)]));
        if (poll)
        {
            await pollService.PollOnceAsync(true, CancellationToken.None);
        }

        return (new PollProtectionSource(pollService, status, ours, new HaltService(new InMemoryHaltStore(), _clock), () => new DateOnly(2026, 10, 30)), pollService, ours);
    }

    [Fact]
    public async Task The_snapshot_carries_the_polled_book_the_open_position_and_the_broker_net_when_the_streams_are_alive()
    {
        var (source, _, _) = await SourceAsync();

        var snapshot = await source.ReadAsync(CancellationToken.None);

        snapshot.ShouldNotBeNull();
        snapshot.BookReadAt.ShouldBe(ReconFixtures.Now);
        snapshot.Halt.ShouldBeNull();
        var p = snapshot.Positions.ShouldHaveSingleItem();
        (p.NetQty, p.BrokerNetQty, p.IntentStop, p.State).ShouldBe((10, 10, 95m, ExecState.Filled));
        snapshot.OrderBook.Count.ShouldBe(1);
    }

    [Fact]
    public async Task With_no_poll_a_stale_poll_or_an_unreadable_record_the_snapshot_is_null_never_an_empty_one()
    {
        (await (await SourceAsync(poll: false)).Source.ReadAsync(CancellationToken.None)).ShouldBeNull();
        var (source, _, ours) = await SourceAsync();
        _clock.Advance(TimeSpan.FromSeconds(16));
        (await source.ReadAsync(CancellationToken.None)).ShouldBeNull();

        var (second, poll, theirs) = await SourceAsync();
        theirs.Fail = true;
        (await second.ReadAsync(CancellationToken.None)).ShouldBeNull();
        ours.Fail.ShouldBeFalse();
        poll.Latest.ShouldNotBeNull();
    }
}
