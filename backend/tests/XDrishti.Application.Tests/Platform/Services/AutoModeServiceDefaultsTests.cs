using XDrishti.Application.Confidence;
using XDrishti.Application.LearnedSettings;
using XDrishti.Application.Learning;
using XDrishti.Application.Paper;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Tests.Platform.Services;

/// <summary>AM-01a (ADR 0282): the six evidence services start enabled; the services of table B (the recorders, the ticket services) are not touched by this slice.</summary>
public sealed class AutoModeServiceDefaultsTests
{
    public static TheoryData<ServiceDefinition> EnabledAtFirstStart => new()
    {
        MlMonitorService.Definition,
        ReadinessSnapshotService.Definition,
        DecayMonitorService.Definition,
        LearnedSettingsService.Definition,
        PaperCaptureService.Definition,
        PaperReviewService.Definition,
    };

    [Theory]
    [MemberData(nameof(EnabledAtFirstStart))]
    public void The_service_starts_enabled(ServiceDefinition definition)
    {
        definition.Defaults.Enabled.ShouldBeTrue(definition.Id);
    }
}
