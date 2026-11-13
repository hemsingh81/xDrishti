using NSubstitute;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Confidence;
using XDrishti.Application.LearnedSettings;
using XDrishti.Application.Learning;
using XDrishti.Application.Paper;
using XDrishti.Application.Platform.Services;
using XDrishti.Application.Shadow;
using XDrishti.Domain.Autonomy;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Tests.Platform.Services;

/// <summary>AM-01a (ADR 0282): the six evidence services start enabled with no stored row, and a row the owner saved (paused) stays paused; table B (the shadow services, which wait for AM-01b, and ticket auto-approval) is pinned OFF.</summary>
public sealed class AutoModeServiceDefaultsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<ServiceDefinition> EnabledAtFirstStart => new()
    {
        MlMonitorService.Definition,
        ReadinessSnapshotService.Definition,
        DecayMonitorService.Definition,
        LearnedSettingsService.Definition,
        PaperCaptureService.Definition,
        PaperReviewService.Definition,
    };

    public static TheoryData<ServiceDefinition> StillOff => new()
    {
        ShadowWriteService.Definition,
        ShadowGradeService.Definition,
        ShadowDigestService.Definition,
    };

    [Theory]
    [MemberData(nameof(EnabledAtFirstStart))]
    public void The_service_starts_enabled(ServiceDefinition definition)
    {
        definition.Defaults.Enabled.ShouldBeTrue(definition.Id);
    }

    [Theory]
    [MemberData(nameof(EnabledAtFirstStart))]
    public async Task With_no_stored_row_the_service_runs_on_its_definition_and_an_owner_paused_row_stays_paused(ServiceDefinition definition)
    {
        var store = Substitute.For<IServiceScheduleStore>();
        var reader = new ServiceSettingsReader(new ServiceRegistry([]), store);
        store.ListAsync(Ct).Returns(Array.Empty<ServiceSchedule>());

        var none = await reader.ReadAsync(definition, Ct);

        none.Settings.Enabled.ShouldBeTrue("no row: the definition (enabled)");

        var paused = ServiceSchedule.Create(definition.Id, definition.Defaults with { Enabled = false }, "owner:me@x.test", DateTimeOffset.UnixEpoch);
        store.ListAsync(Ct).Returns([paused]);

        var owner = await reader.ReadAsync(definition, Ct);

        owner.Settings.Enabled.ShouldBeFalse("a service the owner paused stays paused");
    }

    [Theory]
    [MemberData(nameof(StillOff))]
    public void The_shadow_services_stay_off_until_AM_01b(ServiceDefinition definition)
    {
        definition.Defaults.Enabled.ShouldBeFalse(definition.Id);
    }

    [Fact]
    public void Ticket_auto_approval_and_reserve_promotion_stay_off()
    {
        AutonomySettings.Defaults.AutoApproveTickets.ShouldBeFalse();
        AutonomySettings.Defaults.PromoteFromReserve.ShouldBeFalse();
    }
}
