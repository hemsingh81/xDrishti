using System.Reflection;
using XDrishti.Application.AutoSwitch;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// AU-17 (ADR 0252): the Auto switch runs the data and planning chain and only suggests. Nothing of it reaches autonomy, plan approval, arming or the execution service, no order-capable port is resolvable from it, and only one class turns the stored switch.
/// Source pins are text pins: they stop the obvious, and a new reference needs this test updated in review.
/// </summary>
public sealed class AutoSwitchArchitectureTests
{
    private static string Src => Path.GetDirectoryName(Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Domain", "XDrishti.Domain.csproj"))!)!;

    private static IEnumerable<string> Sources() =>
        Directory.EnumerateFiles(Src, "*.cs", SearchOption.AllDirectories).Where(f => !RepositoryFiles.IsBuildOutput(f));

    private static List<string> AutoSwitchFiles() =>
    [
        .. Sources().Where(f => f.Contains(Path.Combine("XDrishti.Application", "AutoSwitch") + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || f.Contains(Path.Combine("Abstractions", "AutoSwitch") + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || Path.GetFileName(f) is "AutoSwitchLog.cs" or "AutoEndpoints.cs"),
    ];

    [Fact]
    public void The_auto_switch_does_not_reference_execution_autonomy_approval_arming_or_any_order_path()
    {
        var files = AutoSwitchFiles();
        files.Count.ShouldBeGreaterThanOrEqualTo(6);

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var forbidden in new[] { "XDrishti.Execution", "XDrishti.Application.Execution", "Abstractions.Autonomy", "XDrishti.Application.Autonomy", "XDrishti.Domain.Autonomy", "AutoApprovePlan", "IAutoApprovalStore", "IAutonomySettingsStore", "IIntentCreator", "IArmedTicketReader", "PlaceOrder", "IBrokerOrder", "ArmTicket", "xd_exec" })
            {
                text.ShouldNotContain(forbidden, Case.Sensitive, file);
            }
        }
    }

    [Fact]
    public void The_switch_and_the_switched_builder_are_built_only_from_these_ports()
    {
        Received(typeof(AutoSwitchService)).ShouldBe(
        [
            "IAutoSwitch", "IEssentialSettingStore", "IServiceScheduleReader", "ServiceRegistry", "ICommandHandler`2", "ICommandHandler`2", "IAutoScopeStore", "IDataAccountStatus", "IChargeSheetConfirmationSource", "IAutoSwitchLog", "IPlanStore", "TimeProvider", "ILogger`1", "IPlanBuilderSettings",
        ]);
        Received(typeof(SwitchedPlanBuilder)).ShouldBe(["IAutoSwitch", "SelectionPlanBuilder", "NullPlanBuilder"]);
    }

    [Fact]
    public void No_type_the_switch_is_built_from_is_in_an_execution_autonomy_or_order_namespace()
    {
        foreach (var type in new[] { typeof(AutoSwitchService), typeof(SwitchedPlanBuilder) }.SelectMany(t => t.GetConstructors().Single().GetParameters().SelectMany(p => new[] { p.ParameterType }.Concat(p.ParameterType.GenericTypeArguments))))
        {
            (type.Namespace ?? string.Empty).ShouldNotContain("Execution", Case.Sensitive, type.FullName);
            (type.Namespace ?? string.Empty).ShouldNotContain("Autonomy", Case.Sensitive, type.FullName);
            type.Name.ShouldNotContain("Order", Case.Insensitive, type.FullName);
        }
    }

    [Fact]
    public void Only_the_auto_switch_service_writes_the_stored_switch()
    {
        var writers = Sources()
            .Where(f => File.ReadAllText(f).Contains("SetAsync(Key, \"true\"", StringComparison.Ordinal) || File.ReadAllText(f).Contains("SetAsync(\"auto.enabled\", \"true\"", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToList();

        writers.ShouldBe(["AutoSwitchService.cs"]);
    }

    [Fact]
    public void Turning_on_needs_the_fresh_password_and_turning_off_a_session_only()
    {
        var endpoints = File.ReadAllText(Path.Combine(Src, "XDrishti.Api", "Endpoints", "AutoEndpoints.cs"));
        var on = endpoints[endpoints.IndexOf("TurnOnAsync(", StringComparison.Ordinal)..endpoints.IndexOf("TurnOffAsync(", StringComparison.Ordinal)];
        var off = endpoints[endpoints.LastIndexOf("TurnOffAsync(", StringComparison.Ordinal)..endpoints.IndexOf("GetScopeAsync(", endpoints.LastIndexOf("TurnOffAsync(", StringComparison.Ordinal), StringComparison.Ordinal)];

        on.ShouldContain("OwnerAct.FreshAsync");
        off.ShouldContain("OwnerAct.SessionOwner");
        off.ShouldNotContain("OwnerAct.FreshAsync");
    }

    private static List<string> Received(Type type) => [.. type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Single().GetParameters().Select(p => p.ParameterType.Name)];
}
