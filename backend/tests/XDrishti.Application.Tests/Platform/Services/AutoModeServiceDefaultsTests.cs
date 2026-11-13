using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Confidence;
using XDrishti.Application.LearnedSettings;
using XDrishti.Application.Learning;
using XDrishti.Application.Paper;
using XDrishti.Application.Platform.Services;
using XDrishti.Application.Shadow;
using XDrishti.Domain.Autonomy;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Tests.Platform.Services;

/// <summary>AM-01a (ADR 0282): the six evidence services (and, from AM-01b, the three shadow services) start enabled with no stored row, and a row the owner saved (paused) stays paused; table B (ticket auto-approval, the registration, null and cost-stress enforcement, the ML switches) is pinned OFF.</summary>
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

    [Fact]
    public void Ticket_auto_approval_and_reserve_promotion_stay_off()
    {
        AutonomySettings.Defaults.AutoApproveTickets.ShouldBeFalse();
        AutonomySettings.Defaults.PromoteFromReserve.ShouldBeFalse();
    }

    [Fact]
    public void The_table_B_configuration_defaults_stay_off()
    {
        new RegistrationOptions().Enforce.ShouldBeFalse();
        new RegistrationOptions().CountInFactor.ShouldBeFalse();
        new NullOptions().Enforce.ShouldBeFalse();
        new CostStressOptions().Enforce.ShouldBeFalse();
        // the three ML switches read OFF with no row (the stored-row read is pinned by EvidenceSetApiTests.Table_B_ML_switches_read_OFF_with_nothing_stored)
        MlSwitch.Names.ShouldBe([MlSwitch.Influence, MlSwitch.AutoPromote, MlSwitch.AutoRollback]);
        (MlSwitches.AllOff.Influence.On, MlSwitches.AllOff.AutoPromote.On, MlSwitches.AllOff.AutoRollback.On).ShouldBe((false, false, false));
    }
}
